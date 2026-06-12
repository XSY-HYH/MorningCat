using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Logging
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Critical,
        None
    }

    public static class LogColors
    {
        public const string Debug = "\u001b[36m";
        public const string Info = "\u001b[32m";
        public const string Warning = "\u001b[33m";
        public const string Error = "\u001b[31m";
        public const string Critical = "\u001b[35m";
        public const string Reset = "\u001b[0m";
    }

    public static class Log
    {
        private static string _logSource = "未知来源";
        private static string _logDirectory = null;
        private static LogLevel _consoleLevel = LogLevel.Debug;
        private static LogLevel _fileLevel = LogLevel.Debug;
        private static StreamWriter _fileWriter = null;
        private static readonly object _lock = new object();
        private static readonly object _consoleLock = new object();

        public static event Action<string>? OnLogOutput;

        private static int _warningCount = 0;
        private static int _errorCount = 0;

        public static int WarningCount => _warningCount;
        public static int ErrorCount => _errorCount;

        private static string? _lastWarningMessage;
        private static string? _lastErrorMessage;

        public static string? LastWarningMessage => _lastWarningMessage;
        public static string? LastErrorMessage => _lastErrorMessage;

        static Log()
        {
            SetLogDirectory(null);
            SetConsoleLevel(LogLevel.Debug);
            SetFileLevel(LogLevel.Debug);
        }

        public static void Name(string source)
        {
            _logSource = source;
        }

        public static void SetLogDirectory(string directory)
        {
            lock (_lock)
            {
                if (_fileWriter != null)
                {
                    _fileWriter.Flush();
                    _fileWriter.Close();
                    _fileWriter.Dispose();
                }

                if (string.IsNullOrEmpty(directory))
                {
                    string mainDir = AppDomain.CurrentDomain.BaseDirectory;
                    _logDirectory = Path.Combine(mainDir, "logs");
                }
                else
                {
                    _logDirectory = directory;
                }

                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string logFile = Path.Combine(_logDirectory, $"{timestamp}.log");
                _fileWriter = new StreamWriter(logFile, true, System.Text.Encoding.UTF8);
                _fileWriter.AutoFlush = true;
            }
        }

        public static void SetConsoleLevel(LogLevel level)
        {
            _consoleLevel = level;
        }

        public static void SetFileLevel(LogLevel level)
        {
            _fileLevel = level;
        }

        private static string GetAnsiColor(LogLevel level)
        {
            return level switch
            {
                LogLevel.Debug => LogColors.Debug,
                LogLevel.Info => LogColors.Info,
                LogLevel.Warning => LogColors.Warning,
                LogLevel.Error => LogColors.Error,
                LogLevel.Critical => LogColors.Critical,
                _ => LogColors.Reset
            };
        }

        private static void WriteColoredLine(LogLevel level, string message)
        {
            string color = GetAnsiColor(level);
            string coloredMessage = $"{color}{message}{LogColors.Reset}";

            OnLogOutput?.Invoke(coloredMessage);

            lock (_consoleLock)
            {
                Console.WriteLine(coloredMessage);
            }
        }

        private static void WriteLog(LogLevel level, string message, string filePath, int lineNumber, string memberName)
        {
            if (level == LogLevel.Warning) System.Threading.Interlocked.Increment(ref _warningCount);
            if (level == LogLevel.Error || level == LogLevel.Critical) System.Threading.Interlocked.Increment(ref _errorCount);

            string fileName = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = "unknown";
            }

            if (level == LogLevel.Warning)
            {
                _lastWarningMessage = message;
            }
            else if (level == LogLevel.Error || level == LogLevel.Critical)
            {
                _lastErrorMessage = message;
            }

            string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss,fff} - {level.ToString().ToUpper()} - [{_logSource}] [{fileName}:{lineNumber}] - {message}";

            if (_consoleLevel != LogLevel.None && level >= _consoleLevel)
            {
                WriteColoredLine(level, logMessage);
            }

            if (_fileLevel != LogLevel.None && level >= _fileLevel)
            {
                lock (_lock)
                {
                    if (_fileWriter != null)
                    {
                        _fileWriter.WriteLine(logMessage);
                    }
                }
            }
        }

        public static void Debug(string message,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            WriteLog(LogLevel.Debug, message, filePath, lineNumber, memberName);
        }

        public static void Info(string message,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            WriteLog(LogLevel.Info, message, filePath, lineNumber, memberName);
        }

        public static void Warning(string message,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            WriteLog(LogLevel.Warning, message, filePath, lineNumber, memberName);
        }

        public static void Error(string message,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            WriteLog(LogLevel.Error, message, filePath, lineNumber, memberName);
        }

        public static void Critical(string message,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            WriteLog(LogLevel.Critical, message, filePath, lineNumber, memberName);
        }

        public static void Exception(Exception ex, string message = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            string msg = message == null ? ex.ToString() : $"{message}: {ex.Message}\n{ex.StackTrace}";
            WriteLog(LogLevel.Error, msg, filePath, lineNumber, memberName);
        }
    }
}
