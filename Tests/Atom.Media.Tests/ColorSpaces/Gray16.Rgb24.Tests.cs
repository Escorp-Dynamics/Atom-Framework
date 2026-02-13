namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Gray16 ↔ Rgb24.
/// Lossy конвертация: 16-bit grayscale → 8-bit RGB.
/// </summary>
[TestFixture]
public sealed class Gray16Rgb24Tests : ColorSpaceTestBase<Gray16, Rgb24>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Gray16 ↔ Rgb24";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 257; // 16-bit → 8-bit → 16-bit теряет младшие биты

    /// <inheritdoc/>
    protected override (Gray16 Source, Rgb24 Target, int Tolerance)[] ReferenceValues =>
    [
        // Gray16(Value) → Rgb24(R, G, B)
        (new Gray16(0), new Rgb24(0, 0, 0), 0),              // Чёрный
        (new Gray16(65535), new Rgb24(255, 255, 255), 0),    // Белый
        (new Gray16(32768), new Rgb24(128, 128, 128), 1),    // Серый 50%
        (new Gray16(16384), new Rgb24(64, 64, 64), 1),       // Серый 25%
        (new Gray16(49152), new Rgb24(192, 192, 192), 1),    // Серый 75%
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(Rgb24 a, Rgb24 b, int tolerance)
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
