namespace JobSync
{
    public class Program
    {
        // Main entry point of the program
        static async Task Main(string[] args)
        {
            // Parse command line arguments to get parameters
            Parameters? parameters = CommandLineProcessor.ParseArguments(args);
            if (parameters != null)
            {
                try
                {
                    // Create a logger instance with specified log file path and verbosity level
                    using Logger logger = new(parameters.LogFilePath, parameters.Verbose);
                    using CancellationTokenSource cts = new();

                    // Initialize the synchronizer with provided parameters
                    Synchronizer synchronizer = new(parameters.SourcePath, parameters.ReplicaPath, logger,
                        parameters.Interval, parameters.Fragile, cts, parameters.Comparator);

                    // Start the synchronization process in a separate task
                    Task syncTask = Task.Run(() => synchronizer.StartAsync(cts.Token), cts.Token);

                    // Handle cancellation requests (e.g., when Ctrl+C is pressed)
                    Console.CancelKeyPress += (sender, e) =>
                    {
                        e.Cancel = true;
                        logger.Log("Cancellation requested. Stopping synchronization...");
                        cts.Cancel();
                    };

                    await syncTask;

                    logger.Log("Synchronization process completed.");
                    logger.Dispose();
                }
                catch (Exception ex)
                {
                    // Log any critical error (Exception) that occurs and exit the application.
                    Logger.LogCriticalError($"Critical error occured, unable to proceed. {ex.Message}");
                    Environment.Exit(1);
                }
            }
        }
    }
}
