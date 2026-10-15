using DeskBox.Models;

namespace DeskBox.ViewModels;

public partial class WidgetViewModel
{
    /// <summary>
    /// Merges persisted open-counts into freshly loaded items and drops
    /// counts for paths that are no longer part of the widget.
    /// </summary>
    private void ApplyPersistedUsageCounts(IReadOnlyList<WidgetItem> items)
    {
        EnsureUsageDictionaryComparer();
        bool changed = false;
        var currentPaths = items
            .Select(item => item.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            item.UsageCount = Config.UsageCountByPath.TryGetValue(item.Path, out int count)
                ? count
                : 0;
        }

        foreach (string stalePath in Config.UsageCountByPath.Keys
                     .Where(path => !currentPaths.Contains(path))
                     .ToArray())
        {
            Config.UsageCountByPath.Remove(stalePath);
            changed = true;
        }

        if (changed)
        {
            PersistUsageTracking();
        }
    }

    private void RecordItemUsage(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        EnsureUsageDictionaryComparer();
        Config.UsageCountByPath.TryGetValue(path, out int count);
        Config.UsageCountByPath[path] = count + 1;
        PersistUsageTrackingIfNeeded();
    }

    private void TransferItemUsage(string oldPath, string newPath)
    {
        EnsureUsageDictionaryComparer();
        int count = Config.UsageCountByPath.TryGetValue(oldPath, out int existing)
            ? existing
            : 0;
        Config.UsageCountByPath.Remove(oldPath);
        if (count > 0)
        {
            Config.UsageCountByPath[newPath] = count;
        }
        PersistUsageTrackingIfNeeded();
    }

    private void RemoveItemUsage(string path)
    {
        EnsureUsageDictionaryComparer();
        if (Config.UsageCountByPath.Remove(path))
        {
            PersistUsageTrackingIfNeeded();
        }
    }

    private void ResetUsageTracking()
    {
        Config.UsageCountByPath = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Same batch-scope deferral as AddedAt tracking: inside a bulk mutation
    /// scope the dictionary mutations are free and the persist happens once,
    /// at the scope finalization.
    /// </summary>
    private void PersistUsageTrackingIfNeeded()
    {
        if (_itemMutationBatchDepth > 0)
        {
            _usagePersistPending = true;
            MarkItemMutationBatchDirty();
            return;
        }

        PersistUsageTracking();
    }

    private void EnsureUsageDictionaryComparer()
    {
        if (Config.UsageCountByPath is null)
        {
            Config.UsageCountByPath = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
            return;
        }

        if (Config.UsageCountByPath.Comparer.Equals(StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        Config.UsageCountByPath = new Dictionary<string, int>(
            Config.UsageCountByPath,
            StringComparer.OrdinalIgnoreCase);
    }

    private void PersistUsageTracking()
    {
        _settingsService.UpdateWidget(Config, notifySubscribers: false);
    }
}
