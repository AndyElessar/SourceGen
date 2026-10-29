using System.Buffers;

namespace SourceGen.Ioc;

/// <summary>
/// Collects items in a pooled buffer and copies them into an <see cref="ImmutableEquatableArray{T}"/>.
/// </summary>
/// <remarks>
/// Building a model array through a <see cref="List{T}"/> allocates the list and its growing buffers besides the final
/// array, on every transform run. This builder only allocates the final array. Dispose it, typically with
/// <see langword="using"/>, to return the buffer; it is a <see langword="ref struct"/> so it cannot be copied into a
/// field or closure and returned twice.
/// </remarks>
internal ref struct ValueListBuilder<T>
{
    private T[] buffer;
    private int count;

    public ValueListBuilder(int capacity)
    {
        buffer = ArrayPool<T>.Shared.Rent(Math.Max(capacity, 4));
        count = 0;
    }

    public readonly int Count => count;

    public void Add(T item)
    {
        if(count == buffer.Length)
        {
            Grow();
        }

        buffer[count++] = item;
    }

    public readonly ReadOnlySpan<T> AsSpan() => buffer.AsSpan(0, count);

    public readonly ImmutableEquatableArray<T> ToImmutableEquatableArray() => ImmutableEquatableArray.Create<T>(buffer.AsSpan(0, count));

    public void Dispose()
    {
        if(buffer is null)
        {
            return;
        }

        // Clear the references so the pool does not keep model objects alive
        buffer.AsSpan(0, count).Clear();
        ArrayPool<T>.Shared.Return(buffer);
        buffer = null!;
        count = 0;
    }

    private void Grow()
    {
        var larger = ArrayPool<T>.Shared.Rent(buffer.Length * 2);
        buffer.AsSpan(0, count).CopyTo(larger);
        buffer.AsSpan(0, count).Clear();
        ArrayPool<T>.Shared.Return(buffer);
        buffer = larger;
    }
}
