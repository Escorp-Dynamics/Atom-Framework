namespace Atom.Media.Codecs.Tests;

/// <summary>
/// Тесты PNG round-trip (lossless).
/// </summary>
[TestFixture]
public sealed class PngPngCodecTests : ImageCodecTestBase<PngCodec, PngCodec>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "PNG";

    /// <inheritdoc/>
    protected override string EncoderExtension => ".png";

    /// <inheritdoc/>
    protected override string DecoderExtension => ".png";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 0; // PNG = lossless

    /// <inheritdoc/>
    protected override string? TestAssetPath => "assets/test.png";

    /// <inheritdoc/>
    protected override HardwareAcceleration ImplementedAccelerations =>
        HardwareAcceleration.None |
        HardwareAcceleration.Sse2 |
        HardwareAcceleration.Avx2;

    #endregion
}
