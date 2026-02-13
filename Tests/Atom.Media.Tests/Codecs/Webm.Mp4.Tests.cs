namespace Atom.Media.Codecs.Tests;

/// <summary>
/// Тесты кросс-декодирования: кодируем WebM, декодируем через MP4.
/// Оба используют AFRM Store — проверяем совместимость.
/// </summary>
[TestFixture]
public sealed class WebmMp4CodecTests : VideoCodecTestBase<WebmCodec, Mp4Codec>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "WebM (decoded by MP4)";

    /// <inheritdoc/>
    protected override string EncoderExtension => ".webm";

    /// <inheritdoc/>
    protected override string DecoderExtension => ".mp4";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 0; // Both AFRM Store = lossless

    #endregion
}
