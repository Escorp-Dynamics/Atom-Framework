namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Bgra32 ↔ Rgb24.
/// Shuffle + swap B↔R + drop alpha.
/// </summary>
[TestFixture]
public sealed class Bgra32Rgb24Tests : ColorSpaceTestBase<Bgra32, Rgb24>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Bgra32 ↔ Rgb24";

    /// <inheritdoc/>
    protected override (Bgra32 Source, Rgb24 Target, int Tolerance)[] ReferenceValues =>
    [
        (new Bgra32(0, 0, 0, 255), new Rgb24(0, 0, 0), 0),             // Чёрный
        (new Bgra32(255, 255, 255, 255), new Rgb24(255, 255, 255), 0), // Белый
        (new Bgra32(255, 0, 0, 255), new Rgb24(0, 0, 255), 0),         // BGRA B=255 → RGB R=0,G=0,B=255
        (new Bgra32(0, 255, 0, 255), new Rgb24(0, 255, 0), 0),         // Зелёный
        (new Bgra32(0, 0, 255, 255), new Rgb24(255, 0, 0), 0),         // BGRA R=255 → RGB R=255,G=0,B=0
        (new Bgra32(64, 128, 192, 200), new Rgb24(192, 128, 64), 0),   // Произвольный с alpha
        (new Bgra32(10, 20, 30, 128), new Rgb24(30, 20, 10), 0),       // Малые значения (B=10, R=30)
        (new Bgra32(240, 128, 80, 0), new Rgb24(80, 128, 240), 0),     // Alpha=0
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(Rgb24 a, Rgb24 b, int tolerance)
        => ComponentEquals(a.R, b.R, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.B, b.B, tolerance);

    /// <inheritdoc/>
    protected override bool EqualsSource(Bgra32 a, Bgra32 b, int tolerance)
        => ComponentEquals(a.B, b.B, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.R, b.R, tolerance);
    // Alpha игнорируется при round-trip

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Bgra32 a, Bgra32 b)
        => Math.Max(Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.G - b.G)), Math.Abs(a.R - b.R));

    #endregion
}
