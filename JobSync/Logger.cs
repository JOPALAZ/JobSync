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

        // Constructor initializes the logger with the log file path and verbosity level.
        public Logger(string logFilePath, int verbose)
        {
            this.logFilePath = Path.GetFullPath(logFilePath);
            this.verbose = verbose;

            // Initialize the StreamWriter with the log file path. The file is opened in append mode.
            this.logWriter = new StreamWriter(new FileStream(this.logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) 
            {
                AutoFlush = true
            };
            // Start a background task to process log messages from the queue.
            logTask = Task.Factory.StartNew(ProcessLogQueue, TaskCreationOptions.LongRunning);
            Log($"Log started in file {this.logFilePath}");
        }

        // Logs an important message if the verbosity level is above 0.
        public void LogImportant(string message)
        {
            if (verbose > 0)
            {
                logQueue.Add(new LogMessage { Message = FormatMessage(message), IsError = false });
            }
        }

        // Logs a general message if the verbosity level is above 1.
        public void Log(string message)
        {
            if (verbose > 1)
            {
                logQueue.Add(new LogMessage { Message = FormatMessage(message), IsError = false });
            }
        }

        // Logs an error message. Error messages are logged regardless of the verbosity level.
        public void LogError(string message)
        {
            logQueue.Add(new LogMessage { Message = FormatMessage($"ERROR: {message}"), IsError = true });
        }

        // Logs a critical error message directly to the console error output.
        public static void LogCriticalError(string message)
        {
            Console.Error.Write(FormatMessage($"ERROR: {message}"));
        }

        // Processes the log queue, writing messages to both the console and the log file.
        private void ProcessLogQueue()
        {
            foreach (LogMessage logMessage in logQueue.GetConsumingEnumerable())
            {
                if (logMessage.IsError)
                {
                    Console.Error.Write(logMessage.Message); // Write error messages to the error console.
                }
                else
                {
                    Console.Write(logMessage.Message); // Write general messages to the console.
                }

                logWriter.Write(logMessage.Message); // Always write messages to the log file.
            }
        }

        // Formats log messages with a timestamp.
        private static string FormatMessage(string input)
        {
            return $"[{DateTime.Now}]: {input}{Environment.NewLine}";
        }

        // Disposes the logger, ensuring that all queued messages are processed and resources are released.
        public void Dispose()
        {
            logQueue.CompleteAdding();  // Mark the queue as complete for adding new items.
            logTask.Wait(); // Wait for the logging task to finish processing all messages.
            logWriter.Close(); // Close the log writer.
        }

        // Represents a log message with the message text and a flag indicating if it's an error.
        private class LogMessage
        {
            public string Message { get; set; } = string.Empty;
            public bool IsError { get; set; } = false;
        }
    }
}
