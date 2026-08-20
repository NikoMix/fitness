namespace Forge.Core.Abstractions.Media;

/// <summary>Pure storage-cap policy shared by cache implementations and tests.</summary>
public sealed class MediaCachePolicy
{
    public MediaCachePolicy(long storageCapBytes)
    {
        if (storageCapBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(storageCapBytes), "The media cache storage cap must be positive.");
        }

        StorageCapBytes = storageCapBytes;
    }

    public long StorageCapBytes { get; }

    public bool CanEverFit(long incomingBytes) => incomingBytes >= 0 && incomingBytes <= StorageCapBytes;

    public IReadOnlyList<MediaCacheEntry> SelectEvictionCandidates(
        IEnumerable<MediaCacheEntry> entries,
        long incomingBytes,
        string? protectedAssetKey = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (!CanEverFit(incomingBytes))
        {
            return [];
        }

        var candidates = entries
            .Where(entry => !string.Equals(entry.AssetKey, protectedAssetKey, StringComparison.Ordinal))
            .OrderBy(entry => entry.LastAccessedAt)
            .ThenBy(entry => entry.AssetKey, StringComparer.Ordinal)
            .ToList();

        var usedBytes = entries.Sum(entry => Math.Max(0, entry.SizeBytes));
        var requiredBytes = usedBytes + incomingBytes - StorageCapBytes;
        if (requiredBytes <= 0)
        {
            return [];
        }

        var selected = new List<MediaCacheEntry>();
        long freedBytes = 0;
        foreach (var candidate in candidates)
        {
            selected.Add(candidate);
            freedBytes += Math.Max(0, candidate.SizeBytes);
            if (freedBytes >= requiredBytes)
            {
                break;
            }
        }

        return freedBytes >= requiredBytes ? selected : [];
    }
}
