// Copyright (c) 2026 Jeremy Ellis and contributors
//
// This file is part of CodeBrix.LilyPort, which is licensed under the
// GNU General Public License version 3 only.  See the LICENSE file in the
// repository root for the full text.

using System.Collections;
using System.Collections.Generic;

namespace CodeBrix.LilyPort.Importers;

/// <summary>A dictionary that answers in the order its keys were first added.</summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
/// <remarks>
/// ⚠ python's <c>dict</c> has ITERATED IN INSERTION ORDER since 3.7, and this converter
/// depends on that in several places — the header fields a document's credits produce
/// come out in the order the credits were read, and the group bookkeeping in
/// <c>extract_score_structure</c> takes "the first key" of a start map. .NET's
/// <c>Dictionary</c> makes no such promise, so the order is carried explicitly here
/// rather than relied on. Re-assigning an existing key keeps its POSITION and replaces
/// its VALUE, which is python's rule too.
/// </remarks>
internal sealed class PythonDictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
{
    private readonly Dictionary<TKey, TValue> _values = new Dictionary<TKey, TValue>();
    private readonly List<TKey> _order = new List<TKey>();

    /// <summary>How many keys there are.</summary>
    internal int Count => _order.Count;

    /// <summary>The keys, in the order they were first added.</summary>
    internal IReadOnlyList<TKey> Keys => _order;

    /// <summary>Reads or writes one key's value.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The value.</returns>
    internal TValue this[TKey key]
    {
        get => _values[key];
        set
        {
            if (!_values.ContainsKey(key))
            {
                _order.Add(key);
            }

            _values[key] = value;
        }
    }

    /// <summary>Adds one key and value, for a collection initialiser.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    internal void Add(TKey key, TValue value) => this[key] = value;

    /// <summary>Whether a key is present.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Whether it is there.</returns>
    internal bool ContainsKey(TKey key) => _values.ContainsKey(key);

    /// <summary>python's <c>dict.get</c>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">What to answer when the key is absent.</param>
    /// <returns>The value, or the default.</returns>
    internal TValue GetOrDefault(TKey key, TValue defaultValue = default)
        => key != null && _values.TryGetValue(key, out TValue value) ? value : defaultValue;

    /// <summary>Tries to read one key's value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value, when the key is present.</param>
    /// <returns>Whether it was there.</returns>
    internal bool TryGetValue(TKey key, out TValue value) => _values.TryGetValue(key, out value);

    /// <summary>python's <c>del</c>.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Whether anything was removed.</returns>
    internal bool Remove(TKey key)
    {
        if (!_values.Remove(key))
        {
            return false;
        }

        _order.Remove(key);
        return true;
    }

    /// <summary>Forgets every key.</summary>
    internal void Clear()
    {
        _values.Clear();
        _order.Clear();
    }

    /// <summary>Every key and value, in the order the keys were first added.</summary>
    /// <returns>The pairs.</returns>
    internal IEnumerable<(TKey Key, TValue Value)> Items()
    {
        foreach (TKey key in _order)
        {
            yield return (key, _values[key]);
        }
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        foreach (TKey key in _order)
        {
            yield return new KeyValuePair<TKey, TValue>(key, _values[key]);
        }
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>python's <c>dict(list(a.items()) + list(b.items()))</c>.</summary>
    /// <param name="first">The dictionary whose order wins.</param>
    /// <param name="second">The dictionary whose values win.</param>
    /// <returns>The merged dictionary.</returns>
    internal static PythonDictionary<TKey, TValue> Merge(
        PythonDictionary<TKey, TValue> first, PythonDictionary<TKey, TValue> second)
    {
        PythonDictionary<TKey, TValue> merged = new PythonDictionary<TKey, TValue>();
        foreach ((TKey key, TValue value) in first.Items())
        {
            merged[key] = value;
        }

        foreach ((TKey key, TValue value) in second.Items())
        {
            merged[key] = value;
        }

        return merged;
    }
}
