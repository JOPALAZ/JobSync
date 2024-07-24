namespace JobSync
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            Parameters? parameters = CommandLineProcessor.ParseArguments(args);
            if (parameters != null)
            {
                try
                {
                    using Logger logger = new(parameters.LogFilePath, parameters.Verbose);
                    using CancellationTokenSource cts = new();

                    Synchronizer synchronizer = new(parameters.SourcePath, parameters.ReplicaPath, logger,
                        parameters.Interval, parameters.Fragile, cts, parameters.Comparator);

                    Task syncTask = Task.Run(() => synchronizer.StartAsync(cts.Token), cts.Token);

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
                    Logger.LogCriticalError($"Critical error occured, unable to proceed. {ex.Message}");
                    Environment.Exit(1);
                }
            }
        }
    }
}
