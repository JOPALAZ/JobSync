﻿using System.Security.Cryptography;

namespace JobSync
{
    public class Synchronizer(string sourcePath, string replicaPath, Logger logger, int interval, bool fragile, CancellationTokenSource cancellationToken, string comparingMethod)
    {
        private const int maxRetryAttempts = 5;
        private const int retryDelayMilliseconds = 1000;
        private readonly string sourcePath = EnsureTrailingSlash(Path.GetFullPath(sourcePath));
        private readonly string replicaPath = EnsureTrailingSlash(Path.GetFullPath(replicaPath));
        private readonly Logger logger = logger;
        private readonly int interval = interval;
        private readonly CancellationTokenSource cancellationToken = cancellationToken;
        private readonly string comparingMethod = comparingMethod.ToUpper();
        private readonly bool fragile = fragile;

        // Synchronizes directories and files between source and replica
        private void HandleException(Exception ex) 
        {
            logger.LogError($"Error during synchronization: {ex.Message}");
            if (fragile)
            {
                logger.LogError("Error was encountered, unable to safely proceed.");
                Stop();
                //Environment.Exit(1);
            }
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
                // Synchronize files and directories asynchronosly, and clean up replica (possible because deleting and copying contradict one another)
                Task syncTask = SyncFilesAndDirectoriesAsync(sourceDirectory);
                Task cleanTask = CleanReplicaAsync(replicaDirectory);
                await Task.WhenAll(syncTask,cleanTask);

                logger.Log($"Finishing synchronization, {(DateTime.Now - start).TotalMilliseconds} ms elapsed.");
            }
            catch (TaskCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }

        // Copies files and directories from source to replica
        private async Task SyncFilesAndDirectoriesAsync(DirectoryInfo sourceDirectory)
        {
            FileInfo[] sourceFiles = sourceDirectory.GetFiles("*", SearchOption.AllDirectories);
            DirectoryInfo[] sourceDirectories = sourceDirectory.GetDirectories("*", SearchOption.AllDirectories);

            // Synchronize directories
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
                catch (TaskCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            // Synchronize files asynchronously, a task for each file.
            IEnumerable<Task> fileTasks = sourceFiles.Select(file => Task.Run(async () =>
            {
                try
                {
                    bool shouldCopy = false;
                    string targetFilePath = file.FullName.Replace(sourcePath, replicaPath);
                    FileInfo targetFile = new(targetFilePath);
                    for (int attempt = 0; attempt < maxRetryAttempts; attempt++) // If FS error occurs (e.g, file is open elsewhere) try N times.
                    {
                        try
                        {
                            shouldCopy = !File.Exists(targetFilePath) ||
                                          file.LastWriteTime > targetFile.LastWriteTime ||
                                          file.Length != targetFile.Length ||
                                          !await CompareFilesAsync(file.FullName, targetFile.FullName);
                            break;
                        }
                        catch (IOException ioEx) when (attempt < maxRetryAttempts - 1)
                        {
                            logger.Log($"Attempt {attempt + 1} failed: {ioEx.Message} Retrying...");
                            await Task.Delay(retryDelayMilliseconds);
                        }
                        catch (IOException ioEx)
                        {
                            logger.Log($"Attempt {attempt + 1} failed: {ioEx.Message}");
                            throw;
                        }
                    }
                    if (shouldCopy)
                    {
                        await CopyFile(file.FullName, targetFilePath);
                    }
                }
                catch (TaskCanceledException)
                {
                    return; // Task was canceled, dont have to log.
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }, cancellationToken.Token)).ToArray();

            await Task.WhenAll(fileTasks);
        }

        // Cleans up replica directory by removing files and directories not present in the source
        private async Task CleanReplicaAsync(DirectoryInfo replicaDirectory)
        {
            FileInfo[] replicaFiles = replicaDirectory.GetFiles("*", SearchOption.AllDirectories);
            // Deliting files asynchronously, a task for each file.
            IEnumerable<Task> fileTasks = replicaFiles.Select(file => Task.Run(async () =>
            {
                try
                {
                    string sourceFilePath = file.FullName.Replace(replicaPath, sourcePath);
                    if (!File.Exists(sourceFilePath))
                    {
                        if (File.Exists(file.FullName))
                        {
                            await DeleteFile(file);
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    return; // Task was canceled, dont have to log.
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }, cancellationToken.Token)).ToArray();

            await Task.WhenAll(fileTasks);

            IOrderedEnumerable<DirectoryInfo> replicaDirs = replicaDirectory.GetDirectories("*", SearchOption.AllDirectories)
                .OrderByDescending(d => d.FullName.Length);
            //Deleting directories from inner to outer to avoid trying possible errors tryng to delete non-existent subdirs in non-existent dirs.
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
                catch (TaskCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }
        }

        // Copies a file from source to target
        private async Task CopyFile(string source, string target)
        {
            for (int attempt = 0; attempt < maxRetryAttempts; attempt++) // If FS error occurs (e.g, file is open elsewhere) try N times.
            {
                try
                {
                    using (FileStream sourceStream = new(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (FileStream targetStream = new(target, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await sourceStream.CopyToAsync(targetStream);
                    }
                    logger.LogImportant($"Copied file '{source}' to '{target}'.");
                    break;
                }
                catch (IOException ioEx) when (attempt < maxRetryAttempts - 1)
                {
                    logger.Log($"Attempt {attempt + 1} failed: {ioEx.Message} Retrying...");
                    await Task.Delay(retryDelayMilliseconds);
                }
                catch (IOException ioEx)
                {
                    logger.Log($"Attempt {attempt + 1} failed: {ioEx.Message}");
                    throw;
                }
            }
        }

        // Deletes a file from the replica
        private async Task DeleteFile(FileInfo file)
        {
            for (int attempt = 0; attempt < maxRetryAttempts; attempt++) // If FS error occurs (e.g, file is open elsewhere) try N times.
            {
                try
                {
                    file.Delete();
                    logger.LogImportant($"Deleted file '{file.FullName}' from replica.");
                    break;
                }
                catch (IOException ioEx) when (attempt < maxRetryAttempts - 1)
                {
                    logger.Log($"Attempt {attempt + 1} failed: {ioEx.Message} Retrying...");
                    await Task.Delay(retryDelayMilliseconds);
                }
                catch (IOException ioEx)
                {
                    logger.Log($"Attempt {attempt + 1} failed: {ioEx.Message}");
                    throw;
                }
            }
        }

        // Ensures a path has a trailing slash
        private static string EnsureTrailingSlash(string path)
        {
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                path += Path.DirectorySeparatorChar; //If there is no '\' or '/' at the end, add it, so Replace method can be used safely.
            }
            return path;
        }

        // Compares two files using MD5 hash
        public static async Task<bool> CompareMD5Async(string filePath1, string filePath2)
        {
            using MD5 md5 = MD5.Create();
            using FileStream stream1 = new(filePath1, FileMode.Open, FileAccess.Read, FileShare.Read); //ensuring that files wont be changed during the check.
            using FileStream stream2 = new(filePath2, FileMode.Open, FileAccess.Read, FileShare.Read);

            byte[] hash1 = await md5.ComputeHashAsync(stream1);
            byte[] hash2 = await md5.ComputeHashAsync(stream2);
            return hash1.SequenceEqual(hash2);
        }

        // Compares two files by binary content
        public static async Task<bool> CompareFilesBinaryAsync(string filePath1, string filePath2)
        {
            const int bufferSize = 1024 * 1024; // 1MB
            byte[] buffer1 = new byte[bufferSize];
            byte[] buffer2 = new byte[bufferSize];

            using FileStream fs1 = new(filePath1, FileMode.Open, FileAccess.Read, FileShare.Read); //ensuring that files wont be changed during the check.
            using FileStream fs2 = new(filePath2, FileMode.Open, FileAccess.Read, FileShare.Read);
            int bytesRead1, bytesRead2;
            while ((bytesRead1 = await fs1.ReadAsync(buffer1.AsMemory(0, bufferSize))) > 0 &&
                   (bytesRead2 = await fs2.ReadAsync(buffer2.AsMemory(0, bufferSize))) > 0)
            {
                if (bytesRead1 != bytesRead2 || !buffer1.AsSpan(0, bytesRead1).SequenceEqual(buffer2.AsSpan(0, bytesRead2)))
                {
                    return false;
                }
            }
            return true;
        }

        // Compares two files using SHA256 hash
        public static async Task<bool> CompareSHA256Async(string filePath1, string filePath2)
        {
            using SHA256 sHA256 = SHA256.Create();
            using FileStream stream1 = new(filePath1, FileMode.Open, FileAccess.Read, FileShare.Read); //ensuring that files wont be changed during the check.
            using FileStream stream2 = new(filePath2, FileMode.Open, FileAccess.Read, FileShare.Read);

            byte[] hash1 = await sHA256.ComputeHashAsync(stream1);
            byte[] hash2 = await sHA256.ComputeHashAsync(stream2);
            return hash1.SequenceEqual(hash2);
        }

        // Compares two files according to method chosen.
        private async Task<bool> CompareFilesAsync(string filePath1, string filePath2)
        {
            bool result = comparingMethod switch
            {
                "MD5" => await CompareMD5Async(filePath1, filePath2),
                "SHA256" => await CompareSHA256Async(filePath1, filePath2),
                "NONE" => true, // Checking files to be binary identical is extemely slow, it may be sufficient just to rely on file size and time modified.
                _ => await CompareFilesBinaryAsync(filePath1, filePath2),
            };
            if (!result) 
            {
                logger.Log($"{filePath1} and {filePath2} didn't match");
            }
            return result;

        }

        // Starts the synchronization process, repeatedly syncing at specified intervals
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
                logger.Log("Synchronization operation was cancelled.");
            }
        }

        // Stops the synchronization process
        public void Stop()
        {
            logger.Log("Disabling synchronization process.");
            cancellationToken.Cancel();
        }
    }
}
