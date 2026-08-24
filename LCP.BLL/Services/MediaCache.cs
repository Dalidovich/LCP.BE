namespace LCP.BLL.Services;

internal sealed class MediaCache<T>
{
    private sealed class Entry
    {
        public required T Value { get; init; }
        public required int SizeInBytes { get; init; }
        public required LinkedListNode<string> RecencyNode { get; init; }
    }

    private readonly Dictionary<string, Entry> _entries = new();
    private readonly LinkedList<string> _recency = new();
    private readonly long _maxBytes;
    private readonly object _gate = new();
    private long _totalBytes;

    public MediaCache(long maxBytes)
    {
        _maxBytes = maxBytes > 0 ? maxBytes : 1;
    }

    public bool TryGet(string key, out T value)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                value = default!;
                return false;
            }

            _recency.Remove(entry.RecencyNode);
            _recency.AddFirst(entry.RecencyNode);
            value = entry.Value;
            return true;
        }
    }

    public void Set(string key, T value, int sizeInBytes)
    {
        lock (_gate)
        {
            RemoveCore(key);

            var node = _recency.AddFirst(key);
            _entries[key] = new Entry { Value = value, SizeInBytes = sizeInBytes, RecencyNode = node };
            _totalBytes += sizeInBytes;

            while (_totalBytes > _maxBytes && _entries.Count > 1)
            {
                var leastRecent = _recency.Last;
                if (leastRecent is null) break;
                RemoveCore(leastRecent.Value);
            }
        }
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            RemoveCore(key);
        }
    }

    public void RemoveWhere(Func<string, bool> predicate)
    {
        lock (_gate)
        {
            var matches = _entries.Keys.Where(predicate).ToArray();
            foreach (var key in matches)
                RemoveCore(key);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _recency.Clear();
            _totalBytes = 0;
        }
    }

    private void RemoveCore(string key)
    {
        if (!_entries.Remove(key, out var entry)) return;

        _recency.Remove(entry.RecencyNode);
        _totalBytes -= entry.SizeInBytes;
    }
}
