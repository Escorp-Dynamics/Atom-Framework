namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Rgb24 ↔ YCbCr.
/// Lossy конвертация: цветовое пространство BT.601.
/// </summary>
[TestFixture]
public sealed class Rgb24YCbCrTests : ColorSpaceTestBase<Rgb24, YCbCr>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Rgb24 ↔ YCbCr";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 1; // BT.601 имеет погрешности округления

    /// <inheritdoc/>
    protected override (Rgb24 Source, YCbCr Target, int Tolerance)[] ReferenceValues =>
    [
        // Full range BT.601: Y = 0.299R + 0.587G + 0.114B, Cb = 128 - 0.169R - 0.331G + 0.500B, Cr = 128 + 0.500R - 0.419G - 0.081B
        (new Rgb24(0, 0, 0), new YCbCr(0, 128, 128), 1),             // Чёрный → Y=0
        (new Rgb24(255, 255, 255), new YCbCr(255, 128, 128), 1),     // Белый → Y=255
        (new Rgb24(255, 0, 0), new YCbCr(76, 85, 255), 2),           // Красный
        (new Rgb24(0, 255, 0), new YCbCr(150, 44, 21), 2),           // Зелёный
        (new Rgb24(0, 0, 255), new YCbCr(29, 255, 107), 2),          // Синий
        (new Rgb24(128, 128, 128), new YCbCr(128, 128, 128), 1),     // Серый 50%
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(YCbCr a, YCbCr b, int tolerance)
        => ComponentEquals(a.Y, b.Y, tolerance) &&
           ComponentEquals(a.Cb, b.Cb, tolerance) &&
           ComponentEquals(a.Cr, b.Cr, tolerance);

    /// <inheritdoc/>
    protected override bool EqualsSource(Rgb24 a, Rgb24 b, int tolerance)
        => ComponentEquals(a.R, b.R, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.B, b.B, tolerance);

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Rgb24 a, Rgb24 b)
        => Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Abs(a.B - b.B));

    #endregion
}
