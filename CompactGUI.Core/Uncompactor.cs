
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Windows.Win32;
using CompactGUI.Logging.Core;
using System.Diagnostics;

namespace CompactGUI.Core;

public sealed class Uncompactor : ICompressor, IDisposable
{

    private SemaphoreSlim pauseSemaphore = new SemaphoreSlim(1, 2);
    private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    private ConcurrentDictionary<string, int> processedFileCount = new ConcurrentDictionary<string, int>();
    private long totalProcessedBytes;
    private int failedFileCount;

    private readonly ILogger<Uncompactor> _logger;

    public Uncompactor(ILogger<Uncompactor>? logger = null)
    {
        _logger = logger ?? NullLogger<Uncompactor>.Instance;
    }

    public async Task<bool> RunAsync(List<string>? filesList, IProgress<CompressionProgress>? progressMonitor = null, int maxParallelism = 1)
    {
        filesList ??= [];
        var workItems = filesList
            .Where(File.Exists)
            .Select(file => new CompressionWorkItem(file, new FileInfo(file).Length))
            .ToList();
        int totalFiles = workItems.Count;
        long totalBytes = workItems.Sum(file => file.UncompressedSize);
        if (maxParallelism <= 0) maxParallelism = Environment.ProcessorCount;
        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = maxParallelism,
            CancellationToken = cancellationTokenSource.Token
        };
        totalProcessedBytes = 0;
        failedFileCount = 0;
        processedFileCount.Clear();
        progressMonitor?.Report(CompressionProgress.CreateWorkList(workItems, totalBytes));

        if (totalFiles == 0)
        {
            progressMonitor?.Report(new CompressionProgress(100, string.Empty));
            return true;
        }

        UncompactorLog.StartingDecompression(_logger, totalFiles, maxParallelism);
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            await Parallel.ForEachAsync(workItems, parallelOptions,
                (file, ctx) =>
                {
                    ctx.ThrowIfCancellationRequested();
                    return new ValueTask(PauseAndProcessFile(
                        file,
                        totalFiles,
                        totalBytes,
                        progressMonitor,
                        ctx));
                });
        }
        catch (OperationCanceledException) {
            UncompactorLog.DecompressionCanceled(_logger);
            return false; 
        }
        finally { sw.Stop(); }

        UncompactorLog.DecompressionCompleted(_logger, Math.Round(sw.Elapsed.TotalSeconds, 3));
        return true;

    }

    private async Task PauseAndProcessFile(
        CompressionWorkItem file,
        int totalFiles,
        long totalBytes,
        IProgress<CompressionProgress>? progressMonitor,
        CancellationToken ctx)
    {
        UncompactorLog.ProcessingFile(_logger, file.FileName);
        try
        {
            await pauseSemaphore.WaitAsync(ctx).ConfigureAwait(false);
            pauseSemaphore.Release();
        }
        catch (OperationCanceledException) { throw; }
        ctx.ThrowIfCancellationRequested();

        long currentProcessedBytes = Interlocked.Read(ref totalProcessedBytes);
        progressMonitor?.Report(CompressionProgress.CreateFileUpdate(
            CalculateProgressPercent(currentProcessedBytes, totalBytes),
            file,
            CompressionFileState.Processing,
            currentProcessedBytes,
            processedFileCount.Count,
            totalFiles,
            Volatile.Read(ref failedFileCount),
            totalBytes));

        FileOperationResult operation = WOFDecompressFile(file.FileName);
        bool succeeded = operation.Succeeded;
        if (!succeeded) Interlocked.Increment(ref failedFileCount);
        long? sizeOnDisk = null;
        if (succeeded)
        {
            long currentSizeOnDisk = SharedMethods.GetFileSizeOnDisk(file.FileName);
            if (currentSizeOnDisk >= 0) sizeOnDisk = currentSizeOnDisk;
        }

        processedFileCount.TryAdd(file.FileName, 1);
        currentProcessedBytes = Interlocked.Add(ref totalProcessedBytes, file.UncompressedSize);
        progressMonitor?.Report(CompressionProgress.CreateFileUpdate(
            CalculateProgressPercent(currentProcessedBytes, totalBytes),
            file,
            succeeded ? CompressionFileState.Completed : CompressionFileState.Failed,
            currentProcessedBytes,
            processedFileCount.Count,
            totalFiles,
            Volatile.Read(ref failedFileCount),
            totalBytes,
            sizeOnDisk,
            operation.FailureReason));

    }

    private static int CalculateProgressPercent(long processedBytes, long totalBytes)
    {
        return totalBytes <= 0
            ? 100
            : Math.Clamp((int)((double)processedBytes / totalBytes * 100.0), 0, 100);
    }

    private unsafe FileOperationResult WOFDecompressFile(string file)
    {
        try
        {
            using (SafeFileHandle fs = File.OpenHandle(file))
            {
                bool succeeded = PInvoke.DeviceIoControl(
                    fs,
                    WOFHelper.FSCTL_DELETE_EXTERNAL_BACKING,
                    null,
                    0,
                    null,
                    0,
                    null,
                    null);

                if (succeeded) return new FileOperationResult(true);

                int errorCode = Marshal.GetLastWin32Error();
                string failureReason = errorCode == 0
                    ? "Windows rejected the decompression request."
                    : new Win32Exception(errorCode).Message;
                UncompactorLog.FileDecompressionFailed(_logger, file, failureReason);
                return new FileOperationResult(false, failureReason);
            }  
        }
        catch (Exception ex) { 
            UncompactorLog.FileDecompressionFailed(_logger, file, ex.Message);
            return new FileOperationResult(false, ex.Message);
        }
    }

    public void Pause()
    {
        UncompactorLog.DecompressionPaused(_logger);
        pauseSemaphore.Wait();
    }


    public void Resume()
    {
        if (pauseSemaphore.CurrentCount == 0) pauseSemaphore.Release();
        UncompactorLog.DecompressionResumed(_logger);
    }


    public void Cancel()
    {
        Resume();
        cancellationTokenSource.Cancel();
    }


    public void Dispose()
    {
        pauseSemaphore.Dispose();
        cancellationTokenSource.Dispose();
    }







}
