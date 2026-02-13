namespace Atom.Media.Codecs.Tests;

/// <summary>
/// Тесты кросс-декодирования: кодируем MP4, декодируем через WebM.
/// Оба используют AFRM Store — проверяем совместимость.
/// </summary>
[TestFixture]
public sealed class Mp4WebmCodecTests : VideoCodecTestBase<Mp4Codec, WebmCodec>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "MP4 (decoded by WebM)";

    /// <inheritdoc/>
    protected override string EncoderExtension => ".mp4";

    /// <inheritdoc/>
    protected override string DecoderExtension => ".webm";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 0; // Both AFRM Store = lossless

    #endregion
}
