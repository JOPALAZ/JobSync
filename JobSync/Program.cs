using System;
using System.Threading;
using System.Threading.Tasks;

namespace JobSync
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Parameters? parameters = CommandLineProcessor.ParseArguments(args);
            if (parameters != null)
            {
                using Logger logger = new(parameters.LogFilePath, parameters.Verbose);
                using CancellationTokenSource cts = new();
                Synchronizer synchronizer = new(parameters.SourcePath, parameters.ReplicaPath, logger, parameters.Interval, parameters.Fragile, cts);

                Task syncTask = Task.Run(() => synchronizer.StartAsync(cts.Token), cts.Token);

                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    logger.Log("Cancellation requested. Stopping sync...");
                    cts.Cancel();
                };

                await syncTask;

                logger.Log("Sync process completed.");
                logger.Dispose();
            }
            else
            {
                Logger.LogCriticalError("Failed to parse command line arguments.");
            }
        }
    }
}
