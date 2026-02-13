namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Rgba32 ↔ YCbCr.
/// Это правильное направление для round-trip тестирования:
/// Rgba32 → YCbCr → Rgba32 имеет максимальную ошибку 1 (теоретический минимум).
///
/// YCbCr → Rgba32 → YCbCr имеет огромные ошибки из-за clipping
/// (многие значения YCbCr выходят за пределы RGB gamut).
/// </summary>
[TestFixture]
public sealed class Rgba32YCbCrTests : ColorSpaceTestBase<Rgba32, YCbCr>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Rgba32 ↔ YCbCr";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 1; // Теоретический минимум для BT.601 Q16

    /// <inheritdoc/>
    protected override (Rgba32 Source, YCbCr Target, int Tolerance)[] ReferenceValues =>
    [
        // BT.601 Full Range:
        // Y  = 0.299*R + 0.587*G + 0.114*B
        // Cb = -0.169*R - 0.331*G + 0.5*B + 128
        // Cr = 0.5*R - 0.419*G - 0.081*B + 128
        (new Rgba32(0, 0, 0, 255), new YCbCr(0, 128, 128), 0),         // Чёрный
        (new Rgba32(255, 255, 255, 255), new YCbCr(255, 128, 128), 0), // Белый
        (new Rgba32(255, 0, 0, 255), new YCbCr(76, 85, 255), 1),       // Красный
        (new Rgba32(0, 255, 0, 255), new YCbCr(150, 44, 21), 1),       // Зелёный
        (new Rgba32(0, 0, 255, 255), new YCbCr(29, 255, 107), 1),      // Синий
        (new Rgba32(128, 128, 128, 255), new YCbCr(128, 128, 128), 0), // Серый 50%
        (new Rgba32(255, 255, 0, 255), new YCbCr(226, 1, 149), 1),     // Жёлтый
        (new Rgba32(0, 255, 255, 255), new YCbCr(179, 171, 1), 1),     // Cyan
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(YCbCr a, YCbCr b, int tolerance)
        => ComponentEquals(a.Y, b.Y, tolerance) &&
           ComponentEquals(a.Cb, b.Cb, tolerance) &&
           ComponentEquals(a.Cr, b.Cr, tolerance);

    /// <inheritdoc/>
    protected override bool EqualsSource(Rgba32 a, Rgba32 b, int tolerance)
        => ComponentEquals(a.R, b.R, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.B, b.B, tolerance) &&
           a.A == b.A; // Альфа должна быть точной

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Rgba32 a, Rgba32 b)
        => Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Abs(a.B - b.B));

    #endregion
}
