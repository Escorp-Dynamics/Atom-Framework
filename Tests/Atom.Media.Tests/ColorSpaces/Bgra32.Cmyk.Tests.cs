namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Bgra32 ↔ Cmyk.
/// Lossy конвертация: RGB → CMYK.
/// </summary>
[TestFixture]
public sealed class Bgra32CmykTests : ColorSpaceTestBase<Bgra32, Cmyk>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Bgra32 ↔ Cmyk";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 1;

    /// <inheritdoc/>
    protected override (Bgra32 Source, Cmyk Target, int Tolerance)[] ReferenceValues =>
    [
        // Bgra32(B, G, R, A) → Cmyk(C, M, Y, K)
        (new Bgra32(0, 0, 0, 255), new Cmyk(0, 0, 0, 255), 0),         // Чёрный → K=255
        (new Bgra32(255, 255, 255, 255), new Cmyk(0, 0, 0, 0), 0),     // Белый → K=0
        (new Bgra32(0, 0, 255, 255), new Cmyk(255, 255, 0, 0), 1),     // Красный (R=255)
        (new Bgra32(0, 255, 0, 255), new Cmyk(255, 0, 255, 0), 1),     // Зелёный
        (new Bgra32(255, 0, 0, 255), new Cmyk(0, 255, 255, 0), 1),     // Синий (B=255)
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
    protected override bool EqualsSource(Bgra32 a, Bgra32 b, int tolerance)
        => ComponentEquals(a.B, b.B, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.R, b.R, tolerance);

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Bgra32 a, Bgra32 b)
        => Math.Max(Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.G - b.G)), Math.Abs(a.R - b.R));

    #endregion
}
