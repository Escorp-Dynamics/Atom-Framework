namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Bgr24 ↔ Cmyk.
/// Lossy конвертация: RGB → CMYK.
/// </summary>
[TestFixture]
public sealed class Bgr24CmykTests : ColorSpaceTestBase<Bgr24, Cmyk>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Bgr24 ↔ Cmyk";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 1;

    /// <inheritdoc/>
    protected override (Bgr24 Source, Cmyk Target, int Tolerance)[] ReferenceValues =>
    [
        // Bgr24(B, G, R) → Cmyk(C, M, Y, K)
        (new Bgr24(0, 0, 0), new Cmyk(0, 0, 0, 255), 0),         // Чёрный → K=255
        (new Bgr24(255, 255, 255), new Cmyk(0, 0, 0, 0), 0),     // Белый → K=0
        (new Bgr24(0, 0, 255), new Cmyk(255, 255, 0, 0), 1),     // Красный → C=255, M=255
        (new Bgr24(0, 255, 0), new Cmyk(255, 0, 255, 0), 1),     // Зелёный → C=255, Y=255
        (new Bgr24(255, 0, 0), new Cmyk(0, 255, 255, 0), 1),     // Синий → M=255, Y=255
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(Cmyk a, Cmyk b, int tolerance)
        => ComponentEquals(a.C, b.C, tolerance) &&
           ComponentEquals(a.M, b.M, tolerance) &&
           ComponentEquals(a.Y, b.Y, tolerance) &&
           ComponentEquals(a.K, b.K, tolerance);

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
