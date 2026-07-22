using CompactGUI.Logging.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using Windows.Win32;

namespace CompactGUI.Core;

public sealed class Compactor : ICompressor, IDisposable
{

    private readonly string workingDirectory;
    private readonly HashSet<string> excludedFileExtensions;
    private readonly WOFCompressionAlgorithm wofCompressionAlgorithm;


    private IntPtr compressionInfoPtr;
    private UInt32 compressionInfoSize;

    private long totalProcessedBytes = 0;
    private int processedFileCount = 0;
    private int failedFileCount = 0;
    private readonly SemaphoreSlim pauseSemaphore = new SemaphoreSlim(1, 2);
    private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    private readonly ConcurrentDictionary<string, WOFCompressionAlgorithm> processedFiles = new(StringComparer.OrdinalIgnoreCase);

    private ILogger<Compactor> _logger;

    private Analyser _analyser;

    public IReadOnlyDictionary<string, WOFCompressionAlgorithm> ProcessedFiles => processedFiles;
    public int WorkItemCount { get; private set; }

    public Compactor(string folderPath, WOFCompressionAlgorithm compressionLevel, string[] excludedFileTypes, Analyser analyser, ILogger<Compactor>? logger = null)
    {
        workingDirectory = folderPath;
        excludedFileExtensions = new HashSet<string>(excludedFileTypes);
        wofCompressionAlgorithm = compressionLevel;
        _logger = logger ?? NullLogger<Compactor>.Instance;
        _analyser = analyser;
        InitializeCompressionInfoPointer();
    }


    private void InitializeCompressionInfoPointer()
    {
        var _EFInfo = new WOFHelper.WOF_FILE_COMPRESSION_INFO_V1 { Algorithm = (UInt32)wofCompressionAlgorithm, Flags = 0 };
        compressionInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf(_EFInfo));
        compressionInfoSize = (UInt32)Marshal.SizeOf(_EFInfo);
        Marshal.StructureToPtr(_EFInfo, compressionInfoPtr, true);

    }


    public async Task<bool> RunAsync(List<string>? filesList, IProgress<CompressionProgress>? progressMonitor = null, int maxParallelism = 1)
    {
        if(cancellationTokenSource.IsCancellationRequested) { return false; }

        CompactorLog.BuildingWorkingFilesList(_logger, workingDirectory);
        var workingFiles = (await BuildWorkingFilesList(filesList).ConfigureAwait(false)).ToList();
        WorkItemCount = workingFiles.Count;
        long totalFilesSize = workingFiles.Sum((f) => f.UncompressedSize);
        var workItems = workingFiles
            .Select(file => new CompressionWorkItem(file.FileName, file.UncompressedSize))
            .ToList();

        totalProcessedBytes = 0;
        processedFileCount = 0;
        failedFileCount = 0;
        processedFiles.Clear();
        progressMonitor?.Report(CompressionProgress.CreateWorkList(workItems, totalFilesSize));

        if (workingFiles.Count == 0)
        {
            progressMonitor?.Report(new CompressionProgress(100, string.Empty));
            CompactorLog.CompressionCompleted(_logger, 0);
            return false;
        }

        var sw = Stopwatch.StartNew();

        if (maxParallelism <= 0) maxParallelism = Environment.ProcessorCount;
        ParallelOptions parallelOptions = new() { MaxDegreeOfParallelism = maxParallelism, CancellationToken = cancellationTokenSource.Token };

        CompactorLog.StartingCompression(_logger, workingDirectory, wofCompressionAlgorithm.ToString(), maxParallelism);
        try
        {
           await Parallel.ForEachAsync(workingFiles, parallelOptions,
                (file, ctx) =>
                {
                    ctx.ThrowIfCancellationRequested();

                    return new ValueTask(PauseAndProcessFile(file, totalFilesSize, cancellationTokenSource.Token, progressMonitor));
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException){
            CompactorLog.CompressionCanceled(_logger);
            return false; 
        }
        catch (Exception ex){ 
            CompactorLog.CompressionFailed(_logger, ex.Message);
            return false; 
        }
        finally { sw.Stop();}


        
        CompactorLog.CompressionCompleted(_logger, Math.Round(sw.Elapsed.TotalSeconds, 3));
        return true;
    }

    private async Task PauseAndProcessFile(FileDetails file, long totalFilesSize, CancellationToken token, IProgress<CompressionProgress>? progressMonitor)
    {
        CompactorLog.ProcessingFile(_logger, file.FileName, file.UncompressedSize);

        await pauseSemaphore.WaitAsync(token).ConfigureAwait(false);
        pauseSemaphore.Release();

        var workItem = new CompressionWorkItem(file.FileName, file.UncompressedSize);
        long currentProcessedBytes = Interlocked.Read(ref totalProcessedBytes);
        progressMonitor?.Report(CompressionProgress.CreateFileUpdate(
            CalculateProgressPercent(currentProcessedBytes, totalFilesSize),
            workItem,
            CompressionFileState.Processing,
            currentProcessedBytes,
            Volatile.Read(ref processedFileCount),
            WorkItemCount,
            Volatile.Read(ref failedFileCount),
            totalFilesSize));

        FileOperationResult operation = WOFCompressFile(file.FileName);
        bool succeeded = operation.Succeeded;
        long? compressedSize = null;
        if (succeeded)
        {
            processedFiles.TryAdd(file.FileName, file.OriginalCompressionMode);
            long fileSizeOnDisk = SharedMethods.GetFileSizeOnDisk(file.FileName);
            if (fileSizeOnDisk >= 0) compressedSize = fileSizeOnDisk;
        }
        else
        {
            Interlocked.Increment(ref failedFileCount);
        }

        long processedBytes = Interlocked.Add(ref totalProcessedBytes, file.UncompressedSize);
        int processedFilesCount = Interlocked.Increment(ref processedFileCount);
        progressMonitor?.Report(CompressionProgress.CreateFileUpdate(
            CalculateProgressPercent(processedBytes, totalFilesSize),
            workItem,
            succeeded ? CompressionFileState.Completed : CompressionFileState.Failed,
            processedBytes,
            processedFilesCount,
            WorkItemCount,
            Volatile.Read(ref failedFileCount),
            totalFilesSize,
            compressedSize,
            operation.FailureReason));

    }

    private static int CalculateProgressPercent(long processedBytes, long totalBytes)
    {
        return totalBytes <= 0
            ? 100
            : Math.Clamp((int)((double)processedBytes / totalBytes * 100.0), 0, 100);
    }

    private unsafe FileOperationResult WOFCompressFile(string filePath)
    {
        try
        {
            using (SafeFileHandle fs = File.OpenHandle(filePath))
            {
                int result = PInvoke.WofSetFileDataLocation(
                    fs,
                    (uint)WOFHelper.WOF_PROVIDER_FILE,
                    compressionInfoPtr.ToPointer(),
                    compressionInfoSize);

                if (result == 0) return new FileOperationResult(true);

                string failureReason = Marshal.GetExceptionForHR(result)?.Message
                    ?? $"Windows returned HRESULT 0x{result:X8}.";
                CompactorLog.FileCompressionFailed(_logger, filePath, failureReason);
                return new FileOperationResult(false, failureReason);
            }
        }
        catch (Exception ex)
        {
            CompactorLog.FileCompressionFailed(_logger, filePath, ex.Message);
            return new FileOperationResult(false, ex.Message);
        }
    }

    public async Task<IEnumerable<FileDetails>> BuildWorkingFilesList(IReadOnlyCollection<string>? filesList = null)
    {
        if (filesList is { Count: > 0 })
        {
            return filesList
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(File.Exists)
                .Select(file => new FileDetails(file, new FileInfo(file).Length, WOFCompressionAlgorithm.NO_COMPRESSION))
                .ToList();
        }

        uint clusterSize = SharedMethods.GetClusterSize(workingDirectory);

        var analysedFiles = await _analyser.GetAnalysedFilesAsync(cancellationTokenSource.Token) ?? [];

        return analysedFiles
            .Where(fl =>
                fl.CompressionMode != wofCompressionAlgorithm
                && fl.UncompressedSize > clusterSize
                && ((fl.FileInfo != null && !excludedFileExtensions.Contains(fl.FileInfo.Extension)) || excludedFileExtensions.Contains(fl.FileName))
            )
            .Select(fl => new FileDetails(fl.FileName, fl.UncompressedSize, fl.CompressionMode))
            .ToList();
    }




    public void Pause()
    {
        CompactorLog.CompressionPaused(_logger);
        pauseSemaphore.Wait(cancellationTokenSource.Token);
    }


    public void Resume()
    {
        if (pauseSemaphore.CurrentCount == 0) pauseSemaphore.Release();  
        CompactorLog.CompressionResumed(_logger);
    }


    public void Cancel()
    {
        Resume();
        cancellationTokenSource.Cancel();
    }


    public void Dispose()
    {
        cancellationTokenSource?.Dispose();
        pauseSemaphore?.Dispose();
        if (compressionInfoPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(compressionInfoPtr);
            compressionInfoPtr = IntPtr.Zero;
        }
    }


    public readonly record struct FileDetails(string FileName, long UncompressedSize, WOFCompressionAlgorithm OriginalCompressionMode);

}
