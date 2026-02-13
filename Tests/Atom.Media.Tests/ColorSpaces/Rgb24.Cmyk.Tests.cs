namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Rgb24 ↔ Cmyk.
/// Lossy конвертация: RGB → CMYK.
/// </summary>
[TestFixture]
public sealed class Rgb24CmykTests : ColorSpaceTestBase<Rgb24, Cmyk>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Rgb24 ↔ Cmyk";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 1;

    /// <inheritdoc/>
    protected override (Rgb24 Source, Cmyk Target, int Tolerance)[] ReferenceValues =>
    [
        // Rgb24(R, G, B) → Cmyk(C, M, Y, K)
        (new Rgb24(0, 0, 0), new Cmyk(0, 0, 0, 255), 0),         // Чёрный → K=255
        (new Rgb24(255, 255, 255), new Cmyk(0, 0, 0, 0), 0),     // Белый → K=0
        (new Rgb24(255, 0, 0), new Cmyk(0, 255, 255, 0), 1),     // Красный → M=255, Y=255
        (new Rgb24(0, 255, 0), new Cmyk(255, 0, 255, 0), 1),     // Зелёный → C=255, Y=255
        (new Rgb24(0, 0, 255), new Cmyk(255, 255, 0, 0), 1),     // Синий → C=255, M=255
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
    protected override bool EqualsSource(Rgb24 a, Rgb24 b, int tolerance)
        => ComponentEquals(a.R, b.R, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.B, b.B, tolerance);

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Rgb24 a, Rgb24 b)
        => Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Abs(a.B - b.B));

    #endregion
}
