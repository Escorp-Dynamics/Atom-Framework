#pragma warning disable S109

using System.Runtime.CompilerServices;

namespace Atom.Media;

/// <summary>
/// Цветовой кэш VP8L для быстрого доступа к недавно использованным цветам.
/// </summary>
/// <remarks>
/// Использует мультипликативное хеширование для индексации.
/// Hash = (0x1e35a7bd * color) >> (32 - cache_bits).
/// Размер кэша = 1 &lt;&lt; cache_bits (допустимые значения: 1-11).
/// </remarks>
internal sealed class Vp8LColorCache
{
    /// <summary>Хеш-множитель (magic number из спецификации VP8L).</summary>
    private const uint HashMultiplier = 0x1E35A7BD;

    /// <summary>Массив кэшированных ARGB цветов.</summary>
    private readonly uint[] colors;

    /// <summary>Сдвиг вправо для хеширования: 32 - cache_bits.</summary>
    private readonly int hashShift;

    /// <summary>Количество бит кэша.</summary>
    internal int CacheBits { get; }

    /// <summary>Размер кэша (количество записей).</summary>
    internal int Size { get; }

    /// <summary>
    /// Создаёт новый цветовой кэш.
    /// </summary>
    /// <param name="cacheBits">Биты размера кэша (1-11).</param>
    internal Vp8LColorCache(int cacheBits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cacheBits, Vp8LConstants.MinColorCacheBits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cacheBits, Vp8LConstants.MaxColorCacheBits);

        CacheBits = cacheBits;
        Size = 1 << cacheBits;
        hashShift = 32 - cacheBits;
        colors = new uint[Size];
    }

    /// <summary>
    /// Вставляет цвет в кэш.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Insert(uint argb)
    {
        var index = HashIndex(argb);
        colors[index] = argb;
    }

    /// <summary>
    /// Пакетная вставка блока пикселей в кэш.
    /// Сначала выполняется bulk copy, затем обновляется кэш по уже записанным данным.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal unsafe void InsertBatch(uint* src, int count)
    {
        var shift = hashShift;
        ref var colorsRef = ref colors[0];
        for (var i = 0; i < count; i++)
        {
            var argb = src[i];
            Unsafe.Add(ref colorsRef, (int)((HashMultiplier * argb) >> shift)) = argb;
        }
    }

    /// <summary>
    /// Получает цвет по индексу (из декодированного символа).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal uint Lookup(int index) => colors[index];

    /// <summary>
    /// Вычисляет хеш-индекс для цвета ARGB.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int HashIndex(uint argb) => (int)((HashMultiplier * argb) >> hashShift);

    /// <summary>
    /// Сбрасывает кэш (все записи в 0).
    /// </summary>
    internal void Reset() => Array.Clear(colors);
}
