namespace JobSync.Tests
{
    public class SynchronizerTests : IDisposable
    {
        private readonly string _sourcePath;
        private readonly string _replicaPath;

        public SynchronizerTests()
        {
            _sourcePath = Path.Combine(Path.GetTempPath(), "JobSyncTests_Source");
            _replicaPath = Path.Combine(Path.GetTempPath(), "JobSyncTests_Replica");
            Directory.CreateDirectory(_sourcePath);
            Directory.CreateDirectory(_replicaPath);
        }

        [Fact]
        public async Task TestFileSync()
        {
            using var logger = new Logger(Path.Combine(Path.GetTempPath(), "JobSyncTests_Log.txt"), 2);
            using var cts = new CancellationTokenSource();
            var synchronizer = new Synchronizer(_sourcePath, _replicaPath, logger, 1000, true, cts, "Binary");

            string sourceFile = Path.Combine(_sourcePath, "testFile.txt");
            await File.WriteAllTextAsync(sourceFile, "Hello World");

            _ = synchronizer.StartAsync(cts.Token);
            Thread.Sleep(1000);
            cts.Cancel();

            string replicaFile = Path.Combine(_replicaPath, "testFile.txt");
            Assert.True(File.Exists(replicaFile));
            Assert.Equal(await File.ReadAllTextAsync(sourceFile), await File.ReadAllTextAsync(replicaFile));
        }

        [Fact]
        public async Task TestFileComparisonBinary()
        {
            using var logger = new Logger(Path.Combine(Path.GetTempPath(), "JobSyncTests_Log.txt"), 2);
            using var cts = new CancellationTokenSource();

            string sourceFile = Path.Combine(_sourcePath, "testFile1.txt");
            string replicaFile = Path.Combine(_replicaPath, "testFile1.txt");
            await File.WriteAllTextAsync(sourceFile, "Hello World");
            await File.WriteAllTextAsync(replicaFile, "Hello World");

            bool comparisonResult = await Synchronizer.CompareFilesBinaryAsync(sourceFile, replicaFile);

            Assert.True(comparisonResult);
        }

        [Fact]
        public async Task TestErrorHandling()
        {
            var logger = new Logger(Path.Combine(Path.GetTempPath(), "JobSyncTests_Log.txt"), 2);
            using var cts = new CancellationTokenSource();
            var synchronizer = new Synchronizer(_sourcePath, _replicaPath, logger, 1000, true, cts, "Binary");

            string sourceFile = Path.Combine(_sourcePath, "testFile3.txt");
            var stream = new StreamWriter(new FileStream(sourceFile, FileMode.Create, FileAccess.Write, FileShare.None));
            await stream.WriteAsync("Hello World");

            _ = synchronizer.StartAsync(cts.Token);
            Thread.Sleep(4500);
            cts.Cancel();
            stream.Close();
            logger.Dispose(); // Needed to read data from log, as it would be locked otherwise.
            string logContent = File.ReadAllText(logger.logFilePath);
            Assert.Contains("Error during synchronization", logContent);
        }

        [Fact]
        public async Task TestFileDeletionInReplica()
        {
            using var logger = new Logger(Path.Combine(Path.GetTempPath(), "JobSyncTests_Log.txt"), 2);
            using var cts = new CancellationTokenSource();
            var synchronizer = new Synchronizer(_sourcePath, _replicaPath, logger, 1000, true, cts, "Binary");

            string replicaFile = Path.Combine(_replicaPath, "testFile2.txt");
            await File.WriteAllTextAsync(replicaFile, "Hello World");

            _ = synchronizer.StartAsync(cts.Token);
            Thread.Sleep(500);
            cts.Cancel();

            Assert.False(File.Exists(replicaFile));

        }

        public void Dispose()
        {
            Directory.Delete(_sourcePath, true);
            File.Delete(Path.Combine(Path.GetTempPath(), "JobSyncTests_Log.txt"));
            Directory.Delete(_replicaPath, true);
        }
    }
}
