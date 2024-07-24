using System.Security.Cryptography;

namespace JobSync
{
    internal class Synchronizer
    {
        private readonly string sourcePath;
        private readonly string replicaPath;
        private readonly Logger logger;
        private readonly int interval;
        private readonly CancellationTokenSource cancellationToken;

        private readonly bool fragile;
        public bool IsActive { get; private set; }

        public Synchronizer(string sourcePath, string replicaPath, Logger logger, int interval, bool fragile, CancellationTokenSource cancellationToken)
        {
            this.sourcePath = EnsureTrailingSlash(Path.GetFullPath(sourcePath));
            this.replicaPath = EnsureTrailingSlash(Path.GetFullPath(replicaPath));
            this.logger = logger;
            this.interval = interval;
            this.fragile = fragile;
            this.cancellationToken = cancellationToken;
            IsActive = false;
        }
        private async Task SyncDirectoriesAsync()
        {
            try
            {
                logger.Log("Starting synchronization.");
                DateTime start = DateTime.Now;
                DirectoryInfo sourceDirectory = new(sourcePath);
                DirectoryInfo replicaDirectory = new(replicaPath);

                if (!sourceDirectory.Exists)
                {
                    logger.LogError($"Source directory '{sourcePath}' does not exist.");
                    if(fragile)
                    {   
                        Stop();
                    }
                    return;
                }

                if (!replicaDirectory.Exists)
                {
                    logger.LogImportant($"Replica directory '{replicaPath}' does not exist. Creating...");
                    replicaDirectory.Create();
                }

                await SyncFilesAndDirectoriesAsync(sourceDirectory);
                await CleanReplicaAsync(replicaDirectory);

                logger.Log($"Finishing synchronization, {(DateTime.Now - start).TotalMilliseconds} ms elapsed.");
            }
            catch (Exception ex)
            {
                logger.LogError($"Error during synchronization: {ex.Message}");
                if (fragile)
                {
                    logger.LogError("Error was encountered, unable to safely proceed.");
                    Stop();
                    return;
                }
            }
        }

        private async Task SyncFilesAndDirectoriesAsync(DirectoryInfo sourceDirectory)
        {
            FileInfo[] sourceFiles = sourceDirectory.GetFiles("*", SearchOption.AllDirectories);
            DirectoryInfo[] sourceDirectories = sourceDirectory.GetDirectories("*", SearchOption.AllDirectories);

            foreach (DirectoryInfo dir in sourceDirectories)
            {
                try
                {
                    string targetDirPath = dir.FullName.Replace(sourcePath, replicaPath);
                    DirectoryInfo targetDir = new(targetDirPath);
                    if (!targetDir.Exists)
                    {
                        targetDir.Create();
                        logger.LogImportant($"Created directory '{targetDirPath}'.");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError($"Error during synchronization: {ex.Message}");
                    if (fragile)
                    {
                        logger.LogError("Error was encountered, unable to safely proceed.");
                        Stop();
                        return;
                    }
                }
            }

            IEnumerable<Task> fileTasks = sourceFiles.Select(file => Task.Run(async () =>
            {
                try
                {
                    string targetFilePath = file.FullName.Replace(sourcePath, replicaPath);
                    if (!File.Exists(targetFilePath) || file.LastWriteTime > File.GetLastWriteTime(targetFilePath) || !await CompareMD5Async(file.FullName, targetFilePath))
                    {
                        using (FileStream sourceStream = new(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (FileStream targetStream = new(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await sourceStream.CopyToAsync(targetStream);
                        }
                        logger.LogImportant($"Copied file '{file.FullName}' to '{targetFilePath}'.");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError($"Error during synchronization: {ex.Message}");
                    if (fragile)
                    {
                        logger.LogError("Error was encountered, unable to safely proceed.");
                        Stop();
                        return;
                    }
                }
            }, cancellationToken.Token)).ToArray();

            await Task.WhenAll(fileTasks);
        }

        private async Task CleanReplicaAsync(DirectoryInfo replicaDirectory)
        {
            FileInfo[] replicaFiles = replicaDirectory.GetFiles("*", SearchOption.AllDirectories);
            foreach (FileInfo file in replicaFiles)
            {
                try
                {
                    string sourceFilePath = file.FullName.Replace(replicaPath, sourcePath);
                    if (!File.Exists(sourceFilePath))
                    {
                        file.Delete();
                        logger.LogImportant($"Deleted file '{file.FullName}' from replica.");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError($"Error during synchronization: {ex.Message}");
                    if (fragile)
                    {
                        logger.LogError("Error was encountered, unable to safely proceed.");
                        Stop();
                        return;
                    }
                }
            }

            IOrderedEnumerable<DirectoryInfo> replicaDirs = replicaDirectory.GetDirectories("*", SearchOption.AllDirectories)
                .OrderByDescending(d => d.FullName.Length);
            foreach (DirectoryInfo dir in replicaDirs)
            {
                try
                {
                    string sourceDirPath = dir.FullName.Replace(replicaPath, sourcePath);
                    if (!Directory.Exists(sourceDirPath))
                    {
                        dir.Delete(true);
                        logger.LogImportant($"Deleted directory '{dir.FullName}' from replica.");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError($"Error during synchronization: {ex.Message}");
                    if (fragile)
                    {
                        logger.LogError("Error was encountered, unable to safely proceed.");
                        Stop();
                        return;
                    }
                }
            }
        }

        private static string EnsureTrailingSlash(string path)
        {
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                path += Path.DirectorySeparatorChar;
            }
            return path;
        }

        private async Task<bool> CompareMD5Async(string filePath1, string filePath2)
        {
            using MD5 md5 = MD5.Create();
            using FileStream stream1 = new(filePath1, FileMode.Open, FileAccess.Read, FileShare.Read);
            using FileStream stream2 = new(filePath2, FileMode.Open, FileAccess.Read, FileShare.Read);

            byte[] hash1 = await md5.ComputeHashAsync(stream1);
            byte[] hash2 = await md5.ComputeHashAsync(stream2);
            stream1.Close();
            stream2.Close();
            if (!hash1.SequenceEqual(hash2))
            {
                logger.Log($"{filePath1} and {filePath2} didn't match binary");
                return false;
            }

            return true;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await SyncDirectoriesAsync();
                    await Task.Delay(interval, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogImportant("Sync operation was cancelled.");
            }
            finally
            {
                IsActive = false;
            }
        }

        public void Stop()
        {
            logger.Log("Disabling synchronization process.");
            cancellationToken.Cancel();
            IsActive = false;
        }
    }
}
