namespace Atom.Media.Codecs.Tests;

/// <summary>
/// Тесты WebP round-trip (lossless ARAW Store mode).
/// Примечание: WebpCodec поддерживает только ARAW формат (Store mode),
/// реальные VP8/VP8L файлы не поддерживаются.
/// </summary>
[TestFixture]
public sealed class WebpWebpCodecTests : ImageCodecTestBase<WebpCodec, WebpCodec>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "WebP";

    /// <inheritdoc/>
    protected override string EncoderExtension => ".webp";

    /// <inheritdoc/>
    protected override string DecoderExtension => ".webp";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 0; // ARAW lossless

    // Не используем test.webp — он в VP8L формате, а не ARAW
    /// <inheritdoc/>
    protected override string? TestAssetPath => null;

    /// <inheritdoc/>
    protected override HardwareAcceleration ImplementedAccelerations =>
        HardwareAcceleration.None |
        HardwareAcceleration.Sse2 |
        HardwareAcceleration.Avx2;

    #endregion
}
