namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Gray16 ↔ Rgba32.
/// Lossy конвертация: 16-bit grayscale → 8-bit RGB.
/// </summary>
[TestFixture]
public sealed class Gray16Rgba32Tests : ColorSpaceTestBase<Gray16, Rgba32>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Gray16 ↔ Rgba32";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 257;

    /// <inheritdoc/>
    protected override (Gray16 Source, Rgba32 Target, int Tolerance)[] ReferenceValues =>
    [
        // Gray16(Value) → Rgba32(R, G, B, 255)
        (new Gray16(0), new Rgba32(0, 0, 0, 255), 0),              // Чёрный
        (new Gray16(65535), new Rgba32(255, 255, 255, 255), 0),    // Белый
        (new Gray16(32768), new Rgba32(128, 128, 128, 255), 1),    // Серый 50%
        (new Gray16(16384), new Rgba32(64, 64, 64, 255), 1),       // Серый 25%
        (new Gray16(49152), new Rgba32(192, 192, 192, 255), 1),    // Серый 75%
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(Rgba32 a, Rgba32 b, int tolerance)
        => ComponentEquals(a.R, b.R, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.B, b.B, tolerance);

    /// <inheritdoc/>
    protected override bool EqualsSource(Gray16 a, Gray16 b, int tolerance)
        => Math.Abs(a.Value - b.Value) <= tolerance;

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Gray16 a, Gray16 b)
        => Math.Abs(a.Value - b.Value);

    #endregion
}
