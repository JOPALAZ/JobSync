using System.Collections.Concurrent;

namespace JobSync
{
    public class Logger : IDisposable
    {
        public readonly string logFilePath;
        private readonly int verbose;
        private readonly BlockingCollection<LogMessage> logQueue = [];
        private readonly Task logTask;
        private readonly StreamWriter logWriter;

        public Logger(string logFilePath, int verbose)
        {
            this.logFilePath = Path.GetFullPath(logFilePath);
            this.verbose = verbose;
            this.logWriter = new StreamWriter(new FileStream(this.logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true
            };
            logTask = Task.Factory.StartNew(ProcessLogQueue, TaskCreationOptions.LongRunning);
            Log($"Log started in file {this.logFilePath}");
        }

        public void LogImportant(string message)
        {
            if (verbose > 0)
            {
                logQueue.Add(new LogMessage { Message = FormatMessage(message), IsError = false });
            }
        }
        public void Log(string message)
        {
            if (verbose > 1)
            {
                logQueue.Add(new LogMessage { Message = FormatMessage(message), IsError = false });
            }
        }
        public void LogError(string message)
        {
            logQueue.Add(new LogMessage { Message = FormatMessage($"ERROR: {message}"), IsError = true });
        }

        public static void LogCriticalError(string message)
        {
            Console.Error.Write(FormatMessage($"ERROR: {message}"));
        }

        private void ProcessLogQueue()
        {
            foreach (LogMessage logMessage in logQueue.GetConsumingEnumerable())
            {
                if (logMessage.IsError)
                {
                    Console.Error.Write(logMessage.Message);
                }
                else
                {
                    Console.Write(logMessage.Message);
                }

                logWriter.Write(logMessage.Message);
            }
        }

        private static string FormatMessage(string input)
        {
            return $"[{DateTime.Now}]: {input}{Environment.NewLine}";
        }

        public void Dispose()
        {
            logQueue.CompleteAdding();
            logTask.Wait();
            logWriter.Close();
        }
        private class LogMessage
        {
            public string Message { get; set; } = string.Empty;
            public bool IsError { get; set; } = false;
        }
    }
}
