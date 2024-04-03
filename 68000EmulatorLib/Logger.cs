namespace PendleCodeMonkey.MC68000EmulatorLib
{
    /// <summary>
    /// Logger.
    /// </summary>
    public static class Logger
    {
        public delegate void LogEventHandler(LogEventArgs e);

        /// <summary>
        /// Providers register event handlers here, they are
        /// called in turn.
        /// </summary>
        public static event LogEventHandler? LogEvent;

        /// <summary>
        /// Default level when calling <see cref="Log"/> without the
        /// level argument.
        /// </summary>
        public static LogLevel DefaultLevel { get; set; } = LogLevel.Info;

        /// <summary>
        /// Level at or above which logging occurs.
        /// </summary>
        public static LogLevel Level { get; set; } = LogLevel.Error;

        /// <summary>
        /// Log a message at Information level.
        /// </summary>
        /// <param name="message"></param>
        //[Conditional("DEBUG")]
        public static void Log(string message)
        {
            if (Level != LogLevel.None && Level >= DefaultLevel)
            {
                LogEvent?.Invoke(new LogEventArgs(DefaultLevel, message));
            }
        }

        /// <summary>
        /// Log a message at the specified level.
        /// </summary>
        /// <param name="level"></param>
        /// <param name="message"></param>
        //[Conditional("DEBUG")]
        public static void Log(LogLevel level, string message)
        {
            if (level != LogLevel.None && level >= Level)
            {
                LogEvent?.Invoke(new LogEventArgs(level, message));
            }
        }

        public static void Log(LogLevel level, string feature, string message)
        {
            if (level != LogLevel.None && level >= Level)
            {
                LogEvent?.Invoke(new LogEventArgs(level, message, feature));
            }
        }
    }

    public class LogEventArgs
    {
        public LogEventArgs(LogLevel level, string message, string? feature = null)
        {
            Level = level;
            Message = message;
            Feature = feature;
        }

        public LogLevel Level;
        public string Message;
        public string? Feature;
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
