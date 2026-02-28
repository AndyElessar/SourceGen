namespace SourceGen.Ioc.SourceGenerator;

/// <summary>
/// Represents a single type argument mapping entry.
/// </summary>
/// <param name="Key">The type parameter name (e.g., "T", "TValue").</param>
/// <param name="Value">The actual type argument (e.g., "string", "int").</param>
internal readonly record struct TypeArgEntry(string Key, string Value);

/// <summary>
/// A lightweight structure for mapping type parameter names to their actual type arguments.
/// Entries are stored sorted by key length descending to ensure correct matching priority
/// (e.g., "TValue" is matched before "T").
/// </summary>
/// <remarks>
/// This is a <see langword="ref struct"/> to prevent boxing and ensure efficient stack-based usage patterns.
/// Entries are sorted lazily (only when <see cref="AsSpan"/> is called) to avoid O(n²) insertion overhead.
/// </remarks>
internal ref struct TypeArgMap
{
    private TypeArgEntry[] _entries;
    private int _count;
    private bool _isSorted;

    /// <summary>
    /// Creates an empty TypeArgMap with the specified initial capacity.
    /// </summary>
    public TypeArgMap(int capacity)
    {
        _entries = capacity > 0 ? new TypeArgEntry[capacity] : [];
        _count = 0;
        _isSorted = true;
    }

    /// <summary>
    /// Gets whether the map is uninitialized or contains no elements.
    /// </summary>
    public readonly bool IsDefaultOrEmpty => _entries is null || _count == 0;

    /// <summary>
    /// Gets whether the map is empty.
    /// </summary>
    public readonly bool IsEmpty => _count == 0;

    /// <summary>
    /// Gets the number of entries in the map.
    /// </summary>
    public readonly int Count => _count;

    /// <summary>
    /// Adds a type parameter mapping without maintaining sorted order.
    /// Sorting is deferred until <see cref="AsSpan"/> is called.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(string typeParam, string actualArg)
    {
        // Ensure capacity
        if(_entries is null || _entries.Length == 0)
        {
            _entries = new TypeArgEntry[4];
        }
        else if(_count == _entries.Length)
        {
            Array.Resize(ref _entries, _entries.Length * 2);
        }

        _entries[_count++] = new TypeArgEntry(typeParam, actualArg);
        _isSorted = false;
    }

    /// <summary>
    /// Indexer for setting values.
    /// </summary>
    public string this[string typeParam]
    {
        set => Add(typeParam, value);
    }

    /// <summary>
    /// Tries to get a value by key.
    /// </summary>
    public readonly bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
    {
        for(int i = 0; i < _count; i++)
        {
            if(_entries[i].Key == key)
            {
                value = _entries[i].Value;
                return true;
            }
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Ensures entries are sorted by key length descending.
    /// Must be called before <see cref="AsSpan"/> if entries were added after creation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureSorted()
    {
        if(!_isSorted && _count > 1)
        {
            SortByKeyLengthDescending(_entries.AsSpan(0, _count));
            _isSorted = true;
        }
    }

    /// <summary>
    /// Sorts entries by key length descending using insertion sort (efficient for small arrays).
    /// </summary>
    private static void SortByKeyLengthDescending(Span<TypeArgEntry> entries)
    {
        // Insertion sort is efficient for small arrays (typically 1-4 type parameters)
        for(int i = 1; i < entries.Length; i++)
        {
            var current = entries[i];
            int j = i - 1;

            // Move elements that have shorter keys than current to one position ahead
            while(j >= 0 && entries[j].Key.Length < current.Key.Length)
            {
                entries[j + 1] = entries[j];
                j--;
            }
            entries[j + 1] = current;
        }
    }

    /// <summary>
    /// Gets a span of the entries, sorted by key length descending.
    /// </summary>
    public ReadOnlySpan<TypeArgEntry> AsSpan()
    {
        if(_entries is null || _count == 0)
        {
            return [];
        }
        EnsureSorted();
        return _entries.AsSpan(0, _count);
    }

    /// <summary>
    /// Returns an enumerator for the entries.
    /// </summary>
    public readonly Enumerator GetEnumerator() => new(_entries, _count);

    public ref struct Enumerator(TypeArgEntry[]? entries, int count)
    {
        private readonly TypeArgEntry[]? _entries = entries;
        private readonly int _count = entries is null ? 0 : count;
        private int _index = -1;

        public readonly ref readonly TypeArgEntry Current => ref _entries![_index];
        public bool MoveNext() => ++_index < _count;
    }
}
