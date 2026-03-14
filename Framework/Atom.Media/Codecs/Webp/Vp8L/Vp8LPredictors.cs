#pragma warning disable S109, MA0051

using System.Runtime.CompilerServices;

namespace Atom.Media;

/// <summary>
/// 14 пространственных предикторов VP8L.
/// </summary>
/// <remarks>
/// Каждый предиктор работает покомпонентно с ARGB (перенос по 8 бит, с маскированием 0xFF).
/// Порядок каналов в uint32: alpha[31:24], red[23:16], green[15:8], blue[7:0].
/// </remarks>
internal static class Vp8LPredictors
{
    /// <summary>
    /// Применяет предиктор по индексу и возвращает предсказанное значение ARGB.
    /// </summary>
    /// <param name="mode">Режим предсказания (0-13).</param>
    /// <param name="left">Левый пиксель (L).</param>
    /// <param name="top">Верхний пиксель (T).</param>
    /// <param name="topLeft">Верхний левый пиксель (TL).</param>
    /// <param name="topRight">Верхний правый пиксель (TR).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint Predict(int mode, uint left, uint top, uint topLeft, uint topRight) => mode switch
    {
        0 => 0xFF000000,                                       // Чёрный с alpha=255
        1 => left,                                              // L
        2 => top,                                               // T
        3 => topRight,                                          // TR
        4 => topLeft,                                           // TL
        5 => Average2(Average2(left, topRight), top),           // Avg(Avg(L,TR), T)
        6 => Average2(left, topLeft),                           // Avg(L, TL)
        7 => Average2(left, top),                               // Avg(L, T)
        8 => Average2(topLeft, top),                             // Avg(TL, T)
        9 => Average2(top, topRight),                           // Avg(T, TR)
        10 => Average2(Average2(left, topLeft), Average2(top, topRight)),
        11 => Select(left, top, topLeft),                       // Select (gradient)
        12 => ClampAddSubtractFull(left, top, topLeft),         // ClampAddSubFull
        13 => ClampAddSubtractHalf(Average2(left, top), topLeft), // ClampAddSubHalf
        _ => 0xFF000000,
    };

    /// <summary>
    /// Покомпонентное среднее двух ARGB пикселей: (a + b) / 2 для каждого канала.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint Average2(uint a, uint b)
    {
        // Среднее без переполнения через bit tricks:
        // avg = (a & b) + ((a ^ b) >> 1)  — для каждого 8-bit канала
        // Маскируем биты, чтобы сдвиг не портил соседние каналы
        const uint mask = 0xFEFEFEFE;
        return (a & b) + (((a ^ b) & mask) >> 1);
    }

    /// <summary>
    /// Select предиктор: выбирает L или T в зависимости от того,
    /// кто ближе к линейному предсказанию L+T-TL (по Manhattan distance).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint Select(uint left, uint top, uint topLeft)
    {
        // Вычисляем предсказание: L + T - TL для каждого компонента
        var pA = A(left) + A(top) - A(topLeft);
        var pR = R(left) + R(top) - R(topLeft);
        var pG = G(left) + G(top) - G(topLeft);
        var pB = B(left) + B(top) - B(topLeft);

        // Manhattan distance к L
        var distL = Abs(pA - A(left)) + Abs(pR - R(left)) +
                    Abs(pG - G(left)) + Abs(pB - B(left));

        // Manhattan distance к T
        var distT = Abs(pA - A(top)) + Abs(pR - R(top)) +
                    Abs(pG - G(top)) + Abs(pB - B(top));

        return distL < distT ? left : top;
    }

    /// <summary>
    /// ClampAddSubtractFull: Clamp(a + b - c) для каждого канала.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ClampAddSubtractFull(uint a, uint b, uint c)
    {
        var alpha = Clamp(A(a) + A(b) - A(c));
        var red = Clamp(R(a) + R(b) - R(c));
        var green = Clamp(G(a) + G(b) - G(c));
        var blue = Clamp(B(a) + B(b) - B(c));
        return Pack(alpha, red, green, blue);
    }

    /// <summary>
    /// ClampAddSubtractHalf: Clamp(a + (a - b) / 2) для каждого канала.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ClampAddSubtractHalf(uint a, uint b)
    {
        var alpha = Clamp(A(a) + ((A(a) - A(b)) / 2));
        var red = Clamp(R(a) + ((R(a) - R(b)) / 2));
        var green = Clamp(G(a) + ((G(a) - G(b)) / 2));
        var blue = Clamp(B(a) + ((B(a) - B(b)) / 2));
        return Pack(alpha, red, green, blue);
    }

    #region Channel Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int A(uint argb) => (int)((argb >> 24) & 0xFF);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int R(uint argb) => (int)((argb >> 16) & 0xFF);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int G(uint argb) => (int)((argb >> 8) & 0xFF);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int B(uint argb) => (int)(argb & 0xFF);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Clamp(int value) => Math.Clamp(value, 0, 255);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Abs(int value) => Math.Abs(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Pack(int a, int r, int g, int b) =>
        ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;

    #endregion
}
