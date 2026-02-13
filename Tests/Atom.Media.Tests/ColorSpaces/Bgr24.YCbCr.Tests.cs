namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Bgr24 ↔ YCbCr.
/// Lossy конвертация: RGB → YCbCr использует BT.601 коэффициенты.
/// </summary>
[TestFixture]
public sealed class Bgr24YCbCrTests : ColorSpaceTestBase<Bgr24, YCbCr>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Bgr24 ↔ YCbCr";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 1;

    /// <inheritdoc/>
    protected override (Bgr24 Source, YCbCr Target, int Tolerance)[] ReferenceValues =>
    [
        // BT.601 Full Range: Bgr24(B, G, R) → YCbCr(Y, Cb, Cr)
        (new Bgr24(0, 0, 0), new YCbCr(0, 128, 128), 0),         // Чёрный
        (new Bgr24(255, 255, 255), new YCbCr(255, 128, 128), 0), // Белый
        (new Bgr24(0, 0, 255), new YCbCr(76, 85, 255), 1),       // Красный (R=255)
        (new Bgr24(0, 255, 0), new YCbCr(150, 44, 21), 1),       // Зелёный
        (new Bgr24(255, 0, 0), new YCbCr(29, 255, 107), 1),      // Синий (B=255)
        (new Bgr24(128, 128, 128), new YCbCr(128, 128, 128), 0), // Серый 50%
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(YCbCr a, YCbCr b, int tolerance)
        => ComponentEquals(a.Y, b.Y, tolerance) &&
           ComponentEquals(a.Cb, b.Cb, tolerance) &&
           ComponentEquals(a.Cr, b.Cr, tolerance);

    /// <inheritdoc/>
    protected override bool EqualsSource(Bgr24 a, Bgr24 b, int tolerance)
        => ComponentEquals(a.B, b.B, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.R, b.R, tolerance);

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Bgr24 a, Bgr24 b)
        => Math.Max(Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.G - b.G)), Math.Abs(a.R - b.R));

    #endregion
}
