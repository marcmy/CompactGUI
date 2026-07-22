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

        totalProcessedBytes = 0;
        processedFiles.Clear();

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

        var res = WOFCompressFile(file.FileName);
        if (res == 0)
        {
            processedFiles.TryAdd(file.FileName, file.OriginalCompressionMode);
        }
        Interlocked.Add(ref totalProcessedBytes, file.UncompressedSize);
        var progressPercent = totalFilesSize <= 0
            ? 100
            : (int)((double)totalProcessedBytes / totalFilesSize * 100.0);
        progressMonitor?.Report(new CompressionProgress(progressPercent, file.FileName));

    }

    private unsafe int? WOFCompressFile(string filePath)
    {
        try
        {
            using (SafeFileHandle fs = File.OpenHandle(filePath))
            {
                return PInvoke.WofSetFileDataLocation(fs, (uint)WOFHelper.WOF_PROVIDER_FILE, compressionInfoPtr.ToPointer(), compressionInfoSize);
            }
        }
        catch (Exception ex)
        {
            CompactorLog.FileCompressionFailed(_logger, filePath, ex.Message);
            return null;
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
