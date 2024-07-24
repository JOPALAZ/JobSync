using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace JobSync
{
    internal class Logger : IDisposable
    {
        private readonly string logFilePath;
        private readonly int verbose;
        private readonly BlockingCollection<LogMessage> logQueue = new();
        private readonly Task logTask;

        public Logger(string logFilePath, int verbose)
        {
            this.logFilePath = Path.GetFullPath(logFilePath);
            this.verbose = verbose;

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
                    Console.Error.Flush();
                }
                else
                {
                    Console.Write(logMessage.Message);
                }

                File.AppendAllText(logFilePath, logMessage.Message);
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
        }
        private class LogMessage
        {
            public string Message { get; set; } = string.Empty;
            public bool IsError { get; set; } = false;
        }
    }
}
