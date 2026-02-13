namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Gray16 ↔ Bgr24.
/// Lossy конвертация: 16-bit grayscale → 8-bit RGB.
/// </summary>
[TestFixture]
public sealed class Gray16Bgr24Tests : ColorSpaceTestBase<Gray16, Bgr24>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Gray16 ↔ Bgr24";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 257;

    /// <inheritdoc/>
    protected override (Gray16 Source, Bgr24 Target, int Tolerance)[] ReferenceValues =>
    [
        // Gray16(Value) → Bgr24(B, G, R)
        (new Gray16(0), new Bgr24(0, 0, 0), 0),              // Чёрный
        (new Gray16(65535), new Bgr24(255, 255, 255), 0),    // Белый
        (new Gray16(32768), new Bgr24(128, 128, 128), 1),    // Серый 50%
        (new Gray16(16384), new Bgr24(64, 64, 64), 1),       // Серый 25%
        (new Gray16(49152), new Bgr24(192, 192, 192), 1),    // Серый 75%
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(Bgr24 a, Bgr24 b, int tolerance)
        => ComponentEquals(a.B, b.B, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.R, b.R, tolerance);

    /// <inheritdoc/>
    protected override bool EqualsSource(Gray16 a, Gray16 b, int tolerance)
        => Math.Abs(a.Value - b.Value) <= tolerance;

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Gray16 a, Gray16 b)
        => Math.Abs(a.Value - b.Value);

    #endregion
}
