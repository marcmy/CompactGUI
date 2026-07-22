namespace CompactGUI.Core;


public sealed class AnalysedFileDetails
{
    public required string FileName { get; set; }
    public long UncompressedSize { get; set; }
    public long CompressedSize { get; set; }
    public WOFCompressionAlgorithm CompressionMode { get; set; }
    public FileInfo? FileInfo { get; set; }
}


public sealed class ExtensionResult
{
    public required string Extension { get; set; }
    public long UncompressedBytes { get; set; }
    public long CompressedBytes { get; set; }
    public int TotalFiles { get; set; }
    public double CRatio => CompressedBytes == 0 ? 0 : Math.Round((double)CompressedBytes / UncompressedBytes, 2);

}




public struct CompressionProgress
{
    public int ProgressPercent;
    public string FileName;
    public long FileSize;
    public long? CompressedSize;
    public long ProcessedBytes;
    public long TotalBytes;
    public int ProcessedFiles;
    public int TotalFiles;
    public int FailedFiles;
    public CompressionFileState FileState;
    public string? FailureReason;
    public IReadOnlyList<CompressionWorkItem>? WorkItems;

    public CompressionProgress(int progressPercent, string fileName)
    {
        ProgressPercent = progressPercent;
        FileName = fileName;
        FileSize = 0;
        CompressedSize = null;
        ProcessedBytes = 0;
        TotalBytes = 0;
        ProcessedFiles = 0;
        TotalFiles = 0;
        FailedFiles = 0;
        FileState = CompressionFileState.None;
        FailureReason = null;
        WorkItems = null;
    }

    public static CompressionProgress CreateWorkList(
        IReadOnlyList<CompressionWorkItem> workItems,
        long totalBytes)
    {
        return new CompressionProgress(0, string.Empty)
        {
            TotalBytes = totalBytes,
            TotalFiles = workItems.Count,
            WorkItems = workItems
        };
    }

    public static CompressionProgress CreateFileUpdate(
        int progressPercent,
        CompressionWorkItem workItem,
        CompressionFileState fileState,
        long processedBytes,
        int processedFiles,
        int totalFiles,
        int failedFiles,
        long totalBytes,
        long? compressedSize = null,
        string? failureReason = null)
    {
        return new CompressionProgress(progressPercent, workItem.FileName)
        {
            FileSize = workItem.UncompressedSize,
            CompressedSize = compressedSize,
            ProcessedBytes = processedBytes,
            TotalBytes = totalBytes,
            ProcessedFiles = processedFiles,
            TotalFiles = totalFiles,
            FailedFiles = failedFiles,
            FileState = fileState,
            FailureReason = failureReason
        };
    }
}

public readonly record struct CompressionWorkItem(string FileName, long UncompressedSize);

internal readonly record struct FileOperationResult(bool Succeeded, string? FailureReason = null);

public enum CompressionFileState : int
{
    Processing = 0,
    Failed = 1,
    Queued = 2,
    Completed = 3,
    None = 4
}


public enum CompressionMode: int
{
    XPRESS4K,
    XPRESS8K,
    XPRESS16K,
    LZX,
    None
}


public enum WOFCompressionAlgorithm: int
{
    NO_COMPRESSION = -2,
    LZNT1 = -1,
    XPRESS4K = 0,
    LZX = 1,
    XPRESS8K = 2,
    XPRESS16K = 3
}
