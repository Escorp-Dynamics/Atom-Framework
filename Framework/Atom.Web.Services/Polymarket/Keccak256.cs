using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Реализация хеш-функции Keccak-256 (pre-NIST версия, используемая в Ethereum).
/// </summary>
/// <remarks>
/// Ethereum использует Keccak-256 (pre-standardization), который отличается от
/// NIST SHA3-256 (FIPS 202) только padding'ом: Keccak использует 0x01, SHA3 — 0x06.
/// Параметры sponge: rate=1088, capacity=512, output=256 бит.
/// Совместим с NativeAOT. Не использует рефлексию.
/// </remarks>
public static class Keccak256
{
    // Параметры Keccak-256
    private const int Rate = 136; // 1088 бит / 8 = 136 байт
    private const int HashSize = 32; // 256 бит / 8 = 32 байта
    private const int StateSize = 25; // 5x5 матрица 64-битных слов
    private const int Rounds = 24;

    // Круговые константы (round constants) для iota-шага
    private static ReadOnlySpan<ulong> RoundConstants =>
    [
        0x0000000000000001UL, 0x0000000000008082UL, 0x800000000000808AUL, 0x8000000080008000UL,
        0x000000000000808BUL, 0x0000000080000001UL, 0x8000000080008081UL, 0x8000000000008009UL,
        0x000000000000008AUL, 0x0000000000000088UL, 0x0000000080008009UL, 0x000000008000000AUL,
        0x000000008000808BUL, 0x800000000000008BUL, 0x8000000000008089UL, 0x8000000000008003UL,
        0x8000000000008002UL, 0x8000000000000080UL, 0x000000000000800AUL, 0x800000008000000AUL,
        0x8000000080008081UL, 0x8000000000008080UL, 0x0000000080000001UL, 0x8000000080008008UL
    ];

    // Таблица сдвигов для rho-шага
    private static ReadOnlySpan<int> RotationOffsets =>
    [
         0,  1, 62, 28, 27,
        36, 44,  6, 55, 20,
         3, 10, 43, 25, 39,
        41, 45, 15, 21,  8,
        18,  2, 61, 56, 14
    ];

    // Таблица перестановок для pi-шага
    private static ReadOnlySpan<int> PiLane =>
    [
         0, 10,  7, 11, 17,
        18,  3,  5, 16,  8,
        21, 24,  4, 15, 23,
         9, 13, 12,  2, 20,
        14, 22,  1,  6, 19
    ];

    /// <summary>
    /// Вычисляет Keccak-256 хеш данных.
    /// </summary>
    /// <param name="data">Входные данные.</param>
    /// <returns>32-байтный хеш.</returns>
    public static byte[] Hash(ReadOnlySpan<byte> data)
    {
        var result = new byte[HashSize];
        Hash(data, result);
        return result;
    }

    /// <summary>
    /// Вычисляет Keccak-256 хеш данных в предоставленный буфер.
    /// </summary>
    /// <param name="data">Входные данные.</param>
    /// <param name="destination">Буфер назначения (минимум 32 байта).</param>
    public static void Hash(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, HashSize);

        Span<ulong> state = stackalloc ulong[StateSize];
        state.Clear();

        // Absorb (фаза впитывания)
        var offset = 0;
        while (offset + Rate <= data.Length)
        {
            XorBlock(state, data[offset..]);
            KeccakF1600(state);
            offset += Rate;
        }

        // Последний блок с padding (Keccak padding: 0x01...0x80)
        Span<byte> lastBlock = stackalloc byte[Rate];
        lastBlock.Clear();
        var remaining = data.Length - offset;
        data[offset..].CopyTo(lastBlock);

        // Keccak padding (НЕ SHA3): pad10*1
        // Первый бит padding = 0x01 (Keccak) vs 0x06 (SHA3)
        lastBlock[remaining] = 0x01;
        lastBlock[Rate - 1] |= 0x80;

        XorBlock(state, lastBlock);
        KeccakF1600(state);

        // Squeeze (фаза выжимания) — для 256 бит достаточно одного блока
        for (var i = 0; i < HashSize / 8; i++)
            BinaryPrimitives.WriteUInt64LittleEndian(destination[(i * 8)..], state[i]);
    }

    /// <summary>
    /// XOR блока данных с состоянием sponge.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void XorBlock(Span<ulong> state, ReadOnlySpan<byte> block)
    {
        for (var i = 0; i < Rate / 8; i++)
            state[i] ^= BinaryPrimitives.ReadUInt64LittleEndian(block[(i * 8)..]);
    }

    /// <summary>
    /// Перестановка Keccak-f[1600] — 24 раунда.
    /// </summary>
    private static void KeccakF1600(Span<ulong> state)
    {
        Span<ulong> c = stackalloc ulong[5];
        Span<ulong> d = stackalloc ulong[5];
        Span<ulong> b = stackalloc ulong[StateSize];

        for (var round = 0; round < Rounds; round++)
        {
            // θ (theta) шаг
            for (var x = 0; x < 5; x++)
                c[x] = state[x] ^ state[x + 5] ^ state[x + 10] ^ state[x + 15] ^ state[x + 20];

            for (var x = 0; x < 5; x++)
            {
                d[x] = c[(x + 4) % 5] ^ RotateLeft(c[(x + 1) % 5], 1);
                for (var y = 0; y < 5; y++)
                    state[x + 5 * y] ^= d[x];
            }

            // ρ (rho) и π (pi) шаги — объединены
            for (var i = 0; i < StateSize; i++)
                b[PiLane[i]] = RotateLeft(state[i], RotationOffsets[i]);

            // χ (chi) шаг
            for (var y = 0; y < 5; y++)
            {
                var y5 = 5 * y;
                for (var x = 0; x < 5; x++)
                    state[y5 + x] = b[y5 + x] ^ (~b[y5 + (x + 1) % 5] & b[y5 + (x + 2) % 5]);
            }

            // ι (iota) шаг
            state[0] ^= RoundConstants[round];
        }
    }

    /// <summary>
    /// Циклический сдвиг влево для 64-битного значения.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong RotateLeft(ulong value, int offset) =>
        (value << offset) | (value >> (64 - offset));
}
