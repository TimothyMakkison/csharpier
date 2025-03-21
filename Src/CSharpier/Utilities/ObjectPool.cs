using System.Diagnostics;

namespace CSharpier.Utilities;

internal class ObjectPool<T>
    where T : class
{
    [DebuggerDisplay("{Value,nq}")]
    private struct Element
    {
        internal T? Value;
    }

    /// <remarks>
    /// Not using System.Func{T} because this file is linked into the (debugger) Formatter,
    /// which does not have that type (since it compiles against .NET 2.0).
    /// </remarks>
    internal delegate T Factory();

    // Storage for the pool objects. The first item is stored in a dedicated field because we
    // expect to be able to satisfy most requests from it.
    private T? _firstItem;
    private readonly Element[] _items;

    // factory is stored for the lifetime of the pool. We will call this only when pool needs to
    // expand. compared to "new T()", Func gives more flexibility to implementers and faster
    // than "new T()".
    private readonly Factory _factory;

    public readonly bool TrimOnFree;

    internal ObjectPool(Factory factory, bool trimOnFree = true)
        : this(factory, Environment.ProcessorCount * 2, trimOnFree) { }

    internal ObjectPool(Factory factory, int size, bool trimOnFree = true)
    {
        Debug.Assert(size >= 1);
        _factory = factory;
        _items = new Element[size - 1];
        TrimOnFree = trimOnFree;
    }

    internal ObjectPool(Func<ObjectPool<T>, T> factory, int size)
    {
        Debug.Assert(size >= 1);
        _factory = () => factory(this);
        _items = new Element[size - 1];
    }

    private T CreateInstance()
    {
        var inst = _factory();
        return inst;
    }

    /// <summary>
    /// Produces an instance.
    /// </summary>
    /// <remarks>
    /// Search strategy is a simple linear probing which is chosen for it cache-friendliness.
    /// Note that Free will try to store recycled objects close to the start thus statistically
    /// reducing how far we will typically search.
    /// </remarks>
    internal T Allocate()
    {
        // PERF: Examine the first element. If that fails, AllocateSlow will look at the remaining elements.
        // Note that the initial read is optimistically not synchronized. That is intentional.
        // We will interlock only when we have a candidate. in a worst case we may miss some
        // recently returned objects. Not a big deal.
        var inst = _firstItem;
        if (inst == null || inst != Interlocked.CompareExchange(ref _firstItem, null, inst))
        {
            inst = AllocateSlow();
        }

        return inst;
    }

    private T AllocateSlow()
    {
        var items = _items;

        for (var i = 0; i < items.Length; i++)
        {
            // Note that the initial read is optimistically not synchronized. That is intentional.
            // We will interlock only when we have a candidate. in a worst case we may miss some
            // recently returned objects. Not a big deal.
            var inst = items[i].Value;
            if (inst != null)
            {
                if (inst == Interlocked.CompareExchange(ref items[i].Value, null, inst))
                {
                    return inst;
                }
            }
        }

        return CreateInstance();
    }

    /// <summary>
    /// Returns objects to the pool.
    /// </summary>
    /// <remarks>
    /// Search strategy is a simple linear probing which is chosen for it cache-friendliness.
    /// Note that Free will try to store recycled objects close to the start thus statistically
    /// reducing how far we will typically search in Allocate.
    /// </remarks>
    internal void Free(T obj)
    {
        Validate(obj);
        ForgetTrackedObject(obj);

        if (_firstItem == null)
        {
            // Intentionally not using interlocked here.
            // In a worst case scenario two objects may be stored into same slot.
            // It is very unlikely to happen and will only mean that one of the objects will get collected.
            _firstItem = obj;
        }
        else
        {
            FreeSlow(obj);
        }
    }

    private void FreeSlow(T obj)
    {
        var items = _items;
        for (var i = 0; i < items.Length; i++)
        {
            if (items[i].Value == null)
            {
                // Intentionally not using interlocked here.
                // In a worst case scenario two objects may be stored into same slot.
                // It is very unlikely to happen and will only mean that one of the objects will get collected.
                items[i].Value = obj;
                break;
            }
        }
    }

    /// <summary>
    /// Removes an object from leak tracking.
    ///
    /// This is called when an object is returned to the pool.  It may also be explicitly
    /// called if an object allocated from the pool is intentionally not being returned
    /// to the pool.  This can be of use with pooled arrays if the consumer wants to
    /// return a larger array to the pool than was originally allocated.
    /// </summary>
    [Conditional("DEBUG")]
    internal void ForgetTrackedObject(T old, T? replacement = null) { }

    private void Validate(object obj)
    {
        Debug.Assert(obj != null, "freeing null?");

        Debug.Assert(_firstItem != obj, "freeing twice?");

        var items = _items;
        for (var i = 0; i < items.Length; i++)
        {
            var value = items[i].Value;
            if (value == null)
            {
                return;
            }

            Debug.Assert(value != obj, "freeing twice?");
        }
    }
}
