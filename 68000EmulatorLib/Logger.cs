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
        }

        /// <summary>
        /// Current log level.
        /// </summary>
        public static LogLevel Level { get; set; } = LogLevel.Error;

        /// <summary>
        /// Subscribe to this event to receive log entries.
        /// </summary>
        /// <param name="e"></param>
        public delegate void LogEventHandler(LogEntry e);

        /// <summary>
        /// Providers register event handlers here, they are
        /// called in turn.
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
                               [CallerFilePath]   string sourceFilePath = "",
                               [CallerLineNumber] int sourceLineNumber = 0)
        {
            if (!IsEnabled(level)) return;
            if (!IsEnabled(feature)) return;

            string logMessage = $"[{Path.GetFileName(sourceFilePath)}:{sourceLineNumber}:{memberName}] {message}";

            var logEntry = new LogEntry(level, feature, logMessage);
            LogEvent?.Invoke(logEntry);
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

            string logMessage = $"[{Path.GetFileName(sourceFilePath)}:{sourceLineNumber}:{memberName}] {messageFactory()}";

            var logEntry = new LogEntry(level, feature, logMessage);
            LogEvent?.Invoke(logEntry);
        }
    }

    public record LogEntry
    {
        public LogEntry(LogLevel level, string feature, string message)
        {
            Level = level;
            Feature = feature;
            Message = message;
        }

        public LogLevel Level;
        public string Feature;
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
        None = 6
    }
}
