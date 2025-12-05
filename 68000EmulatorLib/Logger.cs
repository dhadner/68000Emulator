using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Logger.
    /// </summary>
    public static class Logger
    {
        static readonly ConcurrentDictionary<string, bool> _features = [];

        private static long _sequenceNumber = 0;

        /// <summary>
        /// Current sequence number. Debugger can reset.
        /// Thread-safe using Interlocked operations.
        /// </summary>
        public static long SequenceNumber
        {
            get => Interlocked.Read(ref _sequenceNumber);
            set => Interlocked.Exchange(ref _sequenceNumber, value);
        }

        /// <summary>
        /// Return a sorted list of features and their enabled status.
        /// </summary>
        /// <returns></returns>
        public static List<(string feature, bool enabled)> GetFeatures()
        {
            List<(string feature, bool enabed)> list;
            list = [.. _features.Select(f => (f.Key, f.Value)).OrderBy(f => f.Key)];
            list.Remove(list.Find(f => f.feature == "All"));

            // Make sure "All" is first in the list.
            list.Insert(0, ("All", Logger.IsEnabled("All")));
            return list;
        }

        public static bool IsEnabled(LogLevel level)
        {
            return level != LogLevel.None && level >= Level;
        }

        /// <summary>
        /// Check if a feature is enabled. If the feature does not exist,
        /// add it with a value of false.
        /// </summary>
        /// <param name="feature"></param>
        /// <returns></returns>
        public static bool IsEnabled(string feature)
        {
            bool exists = _features.TryGetValue(feature, out bool enabled);
            if (!exists)
            {
                // If the feature does not exist, it is not enabled.
                _features.TryAdd(feature, false);
                return false;
            }
            return enabled;
        }

        /// <summary>
        /// Delegate for the <see cref="FeatureChanged"/> event.
        /// </summary>
        /// <param name="feature"></param>
        /// <param name="enabled"></param>
        public delegate void FeatureChangedEventHandler(string feature, bool enabled);

        /// <summary>
        /// Event raised when a feature is added, enabled, or disabled.
        /// </summary>
        public static event FeatureChangedEventHandler? FeatureChanged;

        public static void SetFeature(string feature, bool enabled = false)
        {
            bool exists = _features.TryGetValue(feature, out bool currentValue);
            if (!exists)
            {
                _features.TryAdd(feature, enabled);
                currentValue = enabled;
            }
            if (feature == "All")
            {
                // If the feature is "All", set all features to the same value.
                foreach (var key in _features.Keys)
                {
                    _features[key] = enabled;
                }
            }
            else if (enabled != currentValue)
            {
                // Update its value.
                _features[feature] = enabled;

                // Reset "All" if this was disabled
                if (!enabled)
                {
                    _features["All"] = false;
                }
            }
            FeatureChanged?.Invoke(feature, enabled);
        }

        /// <summary>
        /// Delegate for <see cref="LevelChanged"/> event.
        /// </summary>
        /// <param name="newLevel"></param>
        public delegate void LogLevelChangedEventHandler(LogLevel newLevel);

        /// <summary>
        /// Event raised when the log level is changed.
        /// </summary>
        public static event LogLevelChangedEventHandler? LevelChanged;

        /// <summary>
        /// Get or set log level.
        /// Setting raises the <see cref="LevelChanged"/> event.
        /// </summary>
        public static LogLevel Level
        {
            get => field;
            set
            {
                field = value;
                LevelChanged?.Invoke(value);
            }
        } = LogLevel.Error;

        /// <summary>
        /// Delegate for <see cref="LogEvent"/> event handler.
        /// </summary>
        /// <param name="e"></param>
        public delegate void LogEventHandler(LogEntry e);

        /// <summary>
        /// Event raised when <see cref="Log(LogLevel, string, Func{string}, string, string, int)"/>
        /// or <see cref="Log(LogLevel, string, string, string, string, int)"/> is called.
        /// If this event is not subscribed to, log messages are ignored.
        /// </summary>
        public static event LogEventHandler? LogEvent;

        /// <summary>
        /// Log a message for the specified feature at the specified level.
        /// </summary>
        /// <param name="level"></param>
        /// <param name="feature"></param>
        /// <param name="message"></param>
        public static void Log(LogLevel level, string feature, string message,
                               [CallerMemberName] string memberName = "",
                               [CallerFilePath] string sourceFilePath = "",
                               [CallerLineNumber] int sourceLineNumber = 0)
        {
            if (!IsEnabled(level)) return;
            if (!IsEnabled(feature)) return;

            string caller = $"{Path.GetFileName(sourceFilePath)}:{sourceLineNumber}:{memberName}";

            var entry = new LogEntry(Interlocked.Increment(ref _sequenceNumber), level, feature, caller, message);

            LogEvent?.Invoke(entry);
        }

        /// <summary>
        /// Log a message for the specified feature at the specified level.
        /// Defers evaluation of the message until it is known that the LogLevel
        /// is enabled in order to avoid unnecessary computation.
        /// </summary>
        /// <param name="level"></param>
        /// <param name="feature"></param>
        /// <param name="message"></param>
        public static void Log(LogLevel level, string feature, Func<string> messageFactory,
                               [CallerMemberName] string memberName = "",
                               [CallerFilePath] string sourceFilePath = "",
                               [CallerLineNumber] int sourceLineNumber = 0)
        {
            if (!IsEnabled(level)) return;
            if (!IsEnabled(feature)) return;

            string caller = $"{Path.GetFileName(sourceFilePath)}:{sourceLineNumber}:{memberName}";
            string logMessage = $"{messageFactory()}";

            var entry = new LogEntry(Interlocked.Increment(ref _sequenceNumber), level, feature, caller, logMessage);
            LogEvent?.Invoke(entry);
        }
    }

    public record LogEntry
    {
        public LogEntry(long sequenceNumber, LogLevel level, string feature, string caller, string message)
        {
            SequenceNumber = sequenceNumber;
            Level = level;
            Feature = feature;
            Caller = caller;
            Message = message;
        }

        public long SequenceNumber;
        public LogLevel Level;
        public string Feature;
        public string Caller;
        public string Message;
    }

    /// <summary>
    /// Log level.
    /// </summary>
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
        Critical = 5,
        Always = 6,
        None = 7
    }
}