namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Bgr24 ↔ Hsv.
/// Lossy конвертация: RGB → HSV с потерей точности.
/// </summary>
[TestFixture]
public sealed class Bgr24HsvTests : ColorSpaceTestBase<Bgr24, Hsv>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Bgr24 ↔ Hsv";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 2;

    /// <inheritdoc/>
    protected override (Bgr24 Source, Hsv Target, int Tolerance)[] ReferenceValues =>
    [
        // Bgr24(B, G, R) → Hsv(H, S, V)
        (new Bgr24(0, 0, 0), new Hsv(0, 0, 0), 0),               // Чёрный
        (new Bgr24(255, 255, 255), new Hsv(0, 0, 255), 0),       // Белый
        (new Bgr24(0, 0, 255), new Hsv(0, 255, 255), 1),         // Красный (H=0°)
        (new Bgr24(0, 255, 0), new Hsv(21845, 255, 255), 1),     // Зелёный (H=120°)
        (new Bgr24(255, 0, 0), new Hsv(43690, 255, 255), 1),     // Синий (H=240°)
        (new Bgr24(128, 128, 128), new Hsv(0, 0, 128), 0),       // Серый 50%
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(Hsv a, Hsv b, int tolerance)
        => Math.Abs(a.H - b.H) <= tolerance * 256 &&
           ComponentEquals(a.S, b.S, tolerance) &&
           ComponentEquals(a.V, b.V, tolerance);

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
