using System.Runtime.CompilerServices;

namespace Atom.Collections;

/// <summary>
/// Представляет расширения для коллекций.
/// </summary>
public static class CollectionsExtensions
{
    /// <summary>
    /// Добавляет несколько элементов в словарь.
    /// </summary>
    /// <typeparam name="TKey">Тип ключа.</typeparam>
    /// <typeparam name="TValue">Тип значения.</typeparam>
    /// <param name="dictionary">Исходный словарь.</param>
    /// <param name="items">Добавляемые элементы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IDictionary<TKey, TValue>? AddRange<TKey, TValue>(this IDictionary<TKey, TValue>? dictionary, IEnumerable<KeyValuePair<TKey, TValue>>? items)
    {
        if (dictionary is null || items is null) return dictionary;
        foreach (var item in items) dictionary.Add(item.Key, item.Value);
        return dictionary;
    }

    /// <summary>
    /// Добавляет несколько элементов в словарь.
    /// </summary>
    /// <typeparam name="TKey">Тип ключа.</typeparam>
    /// <typeparam name="TValue">Тип значения.</typeparam>
    /// <param name="dictionary">Исходный словарь.</param>
    /// <param name="items">Добавляемые элементы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IDictionary<TKey, TValue>? AddRange<TKey, TValue>(this IDictionary<TKey, TValue>? dictionary, params KeyValuePair<TKey, TValue>[]? items)
        => dictionary.AddRange(items as IEnumerable<KeyValuePair<TKey, TValue>>);

    /// <summary>
    /// Преобразует диапазон в разреженный массив.
    /// </summary>
    /// <typeparam name="T">Тип элементов.</typeparam>
    /// <param name="span">Исходный диапазон.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SparseSpan<T> AsSparse<T>(this Span<T> span) where T : unmanaged => span;

    /// <summary>
    /// Преобразует массив в разреженный массив.
    /// </summary>
    /// <typeparam name="T">Тип элементов.</typeparam>
    /// <param name="array">Исходный массив.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SparseArray<T>? AsSparse<T>(this T[]? array) => array is null ? default : (SparseArray<T>)array;
}