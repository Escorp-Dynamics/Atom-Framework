using System.Runtime.CompilerServices;

namespace Atom.IO.Compression.Tests;

/// <summary>
/// Фабрика доступных кодеков. Здесь регистрируем те адаптеры, что реально подключены NuGet‑пакетами.
/// </summary>
internal static class CodecFactory
{
    public static IEnumerable<ICodec> Codecs
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            yield return new AtomZstdCodec();
            yield return new ZstdSharpCodec();
            yield return new ZstdNetCodec();
        }
    }

    /// <summary>
    /// Набор уровней сжатия (без фанатизма, чтобы не тормозить CI).
    /// </summary>
    public static IEnumerable<int> Levels
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            yield return 1;
            yield return 3;
            yield return 6;
            yield return 9;
        }
    }

    /// <summary>
    /// IO‑чанки для стриминговых проверок.
    /// </summary>
    public static IEnumerable<int> IoChunks
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            yield return 1;       // экстремально дробно
            yield return 257;     // «некруглый» размер
            yield return 4096;    // типичный буфер
            yield return 65536;   // большой блок
        }
    }

    /// <summary>Генерирует тестовые буферы с разными шаблонами.</summary>
    public static IEnumerable<(string Name, byte[] Data)> DataSets
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            // Наборы размеров (в т.ч. граничные относительно 64KiB/128KiB):
            int[] sizes = [
                0,
                1,
                17,
                4096,
                65535,
                65536,
                65537,
                131072,
                131073,
                262147,
                1048576,
            ];
            // 1) Нулевые буферы.
            foreach (var n in sizes) yield return ("нули", new byte[n]);

            // 2) Повторяющийся байт.
            foreach (var n in sizes) yield return ("повторяющийся 0xAB", GeneratePattern(n));

            // 3) Псевдо‑рандом (фиксированный seed для воспроизводимости).
            foreach (var n in sizes) yield return ("рандом", GenerateRandom(n));

            // 4) «Текстоподобные» (ограниченный алфавит).
            foreach (var n in sizes) yield return ("текст", GenerateText(n));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] GeneratePattern(int length, byte seed = 0xAB)
    {
        var b = new byte[length];
        for (var i = 0; i < length; i++) b[i] = seed;
        return b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] GenerateRandom(int length, int seed = 0x5EED)
    {
        var b = new byte[length];
        var rng = new Random(length ^ seed);
        rng.NextBytes(b);

        return b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] GenerateText(int length, int seed = 0x13579B)
    {
        var b = new byte[length];
        var rng = new Random(length ^ seed);
        for (var i = 0; i < length; i++) b[i] = (byte)('a' + rng.Next(0, 26));

        return b;
    }
}