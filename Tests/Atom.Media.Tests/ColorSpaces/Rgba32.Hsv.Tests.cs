namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Rgba32 ↔ Hsv.
/// Lossy конвертация: RGB → HSV с потерей точности.
/// </summary>
[TestFixture]
public sealed class Rgba32HsvTests : ColorSpaceTestBase<Rgba32, Hsv>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Rgba32 ↔ Hsv";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 2;

    /// <inheritdoc/>
    protected override (Rgba32 Source, Hsv Target, int Tolerance)[] ReferenceValues =>
    [
        // Rgba32(R, G, B, A) → Hsv(H, S, V) — альфа игнорируется
        (new Rgba32(0, 0, 0, 255), new Hsv(0, 0, 0), 0),               // Чёрный
        (new Rgba32(255, 255, 255, 255), new Hsv(0, 0, 255), 0),       // Белый
        (new Rgba32(255, 0, 0, 255), new Hsv(0, 255, 255), 1),         // Красный (H=0°)
        (new Rgba32(0, 255, 0, 255), new Hsv(21845, 255, 255), 1),     // Зелёный (H=120°)
        (new Rgba32(0, 0, 255, 255), new Hsv(43690, 255, 255), 1),     // Синий (H=240°)
        (new Rgba32(128, 128, 128, 255), new Hsv(0, 0, 128), 0),       // Серый 50%
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(Hsv a, Hsv b, int tolerance)
        => Math.Abs(a.H - b.H) <= tolerance * 256 &&
           ComponentEquals(a.S, b.S, tolerance) &&
           ComponentEquals(a.V, b.V, tolerance);

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
