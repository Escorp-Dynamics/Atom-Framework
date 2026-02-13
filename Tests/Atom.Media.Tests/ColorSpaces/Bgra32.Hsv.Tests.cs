namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Bgra32 ↔ Hsv.
/// Lossy конвертация: RGB → HSV с потерей точности.
/// </summary>
[TestFixture]
public sealed class Bgra32HsvTests : ColorSpaceTestBase<Bgra32, Hsv>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Bgra32 ↔ Hsv";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 2;

    /// <inheritdoc/>
    protected override (Bgra32 Source, Hsv Target, int Tolerance)[] ReferenceValues =>
    [
        // Bgra32(B, G, R, A) → Hsv(H, S, V)
        (new Bgra32(0, 0, 0, 255), new Hsv(0, 0, 0), 0),               // Чёрный
        (new Bgra32(255, 255, 255, 255), new Hsv(0, 0, 255), 0),       // Белый
        (new Bgra32(0, 0, 255, 255), new Hsv(0, 255, 255), 1),         // Красный (R=255)
        (new Bgra32(0, 255, 0, 255), new Hsv(21845, 255, 255), 1),     // Зелёный
        (new Bgra32(255, 0, 0, 255), new Hsv(43690, 255, 255), 1),     // Синий (B=255)
        (new Bgra32(128, 128, 128, 255), new Hsv(0, 0, 128), 0),       // Серый 50%
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(Hsv a, Hsv b, int tolerance)
        => Math.Abs(a.H - b.H) <= tolerance * 256 &&
           ComponentEquals(a.S, b.S, tolerance) &&
           ComponentEquals(a.V, b.V, tolerance);

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
