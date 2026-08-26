using System.Diagnostics;
using System.Text.Json;

namespace CompactGUI.Core;

/// <summary>
/// Remembers unchanged files for which Windows reported that WOF compression
/// would not save disk space. Entries are algorithm-specific and become stale
/// automatically when the file size or last-write timestamp changes.
/// </summary>
public sealed class CompressionNotBeneficialCache
{
    private const string CacheFileName = "compression-not-beneficial.json";

    private readonly object syncRoot = new();
    private readonly FileInfo cacheFile;
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
    private readonly Dictionary<string, CompressionNotBeneficialCacheEntry> entries;

    public static CompressionNotBeneficialCache Shared { get; } = new();

    private CompressionNotBeneficialCache()
    {
        DirectoryInfo dataFolder = new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IridiumIO",
            "CompactGUI"));

        cacheFile = new FileInfo(Path.Combine(dataFolder.FullName, CacheFileName));
        entries = LoadEntries();
    }

    public bool ShouldSkip(string filePath, WOFCompressionAlgorithm algorithm)
    {
        string normalizedPath = NormalizePath(filePath);
        string key = BuildKey(normalizedPath, algorithm);

        lock (syncRoot)
        {
            if (!entries.TryGetValue(key, out CompressionNotBeneficialCacheEntry? entry))
                return false;

            try
            {
                FileInfo file = new(normalizedPath);
                if (file.Exists
                    && file.Length == entry.Length
                    && file.LastWriteTimeUtc.Ticks == entry.LastWriteTimeUtcTicks)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Unable to validate compression cache entry for '{normalizedPath}': {ex.Message}");
            }

            // The file disappeared or changed. Forget the old result so a future
            // file at this path is allowed to try compression again.
            if (entries.Remove(key))
                WriteEntries();

            return false;
        }
    }

    public void Record(string filePath, WOFCompressionAlgorithm algorithm)
    {
        string normalizedPath = NormalizePath(filePath);

        try
        {
            FileInfo file = new(normalizedPath);
            if (!file.Exists) return;

            CompressionNotBeneficialCacheEntry entry = new()
            {
                FilePath = normalizedPath,
                Algorithm = algorithm,
                Length = file.Length,
                LastWriteTimeUtcTicks = file.LastWriteTimeUtc.Ticks
            };

            lock (syncRoot)
            {
                string key = BuildKey(normalizedPath, algorithm);
                if (entries.TryGetValue(key, out CompressionNotBeneficialCacheEntry? existing)
                    && existing.Length == entry.Length
                    && existing.LastWriteTimeUtcTicks == entry.LastWriteTimeUtcTicks)
                {
                    return;
                }

                entries[key] = entry;
                WriteEntries();
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Unable to record non-beneficial compression result for '{normalizedPath}': {ex.Message}");
        }
    }

    private Dictionary<string, CompressionNotBeneficialCacheEntry> LoadEntries()
    {
        Dictionary<string, CompressionNotBeneficialCacheEntry> loaded = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!cacheFile.Directory!.Exists)
                cacheFile.Directory.Create();

            if (!cacheFile.Exists || cacheFile.Length == 0)
                return loaded;

            string json = File.ReadAllText(cacheFile.FullName);
            List<CompressionNotBeneficialCacheEntry>? savedEntries =
                JsonSerializer.Deserialize<List<CompressionNotBeneficialCacheEntry>>(json, jsonOptions);

            if (savedEntries is null) return loaded;

            foreach (CompressionNotBeneficialCacheEntry entry in savedEntries)
            {
                if (string.IsNullOrWhiteSpace(entry.FilePath)) continue;

                string normalizedPath = NormalizePath(entry.FilePath);
                entry.FilePath = normalizedPath;
                loaded[BuildKey(normalizedPath, entry.Algorithm)] = entry;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Unable to load compression-not-beneficial cache: {ex.Message}");
        }

        return loaded;
    }

    private void WriteEntries()
    {
        try
        {
            if (!cacheFile.Directory!.Exists)
                cacheFile.Directory.Create();

            string json = JsonSerializer.Serialize(
                entries.Values
                    .OrderBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Algorithm)
                    .ToList(),
                jsonOptions);

            string tempPath = cacheFile.FullName + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, cacheFile.FullName, true);
            cacheFile.Refresh();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Unable to save compression-not-beneficial cache: {ex.Message}");
        }
    }

    private static string BuildKey(string normalizedPath, WOFCompressionAlgorithm algorithm)
        => $"{normalizedPath}|{(int)algorithm}";

    private static string NormalizePath(string filePath)
    {
        try
        {
            return Path.GetFullPath(filePath);
        }
        catch
        {
            return filePath;
        }
    }
}

public sealed class CompressionNotBeneficialCacheEntry
{
    public string FilePath { get; set; } = string.Empty;
    public WOFCompressionAlgorithm Algorithm { get; set; }
    public long Length { get; set; }
    public long LastWriteTimeUtcTicks { get; set; }
}
