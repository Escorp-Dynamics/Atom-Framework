namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Rgba32 ↔ Cmyk.
/// Lossy конвертация: RGB → CMYK.
/// </summary>
[TestFixture]
public sealed class Rgba32CmykTests : ColorSpaceTestBase<Rgba32, Cmyk>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Rgba32 ↔ Cmyk";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 1;

    /// <inheritdoc/>
    protected override (Rgba32 Source, Cmyk Target, int Tolerance)[] ReferenceValues =>
    [
        // Rgba32(R, G, B, A) → Cmyk(C, M, Y, K) — альфа игнорируется
        (new Rgba32(0, 0, 0, 255), new Cmyk(0, 0, 0, 255), 0),         // Чёрный → K=255
        (new Rgba32(255, 255, 255, 255), new Cmyk(0, 0, 0, 0), 0),     // Белый → K=0
        (new Rgba32(255, 0, 0, 255), new Cmyk(0, 255, 255, 0), 1),     // Красный
        (new Rgba32(0, 255, 0, 255), new Cmyk(255, 0, 255, 0), 1),     // Зелёный
        (new Rgba32(0, 0, 255, 255), new Cmyk(255, 255, 0, 0), 1),     // Синий
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
    protected override bool EqualsSource(Rgba32 a, Rgba32 b, int tolerance)
        => ComponentEquals(a.R, b.R, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.B, b.B, tolerance);

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Rgba32 a, Rgba32 b)
        => Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Abs(a.B - b.B));

    #endregion
}
