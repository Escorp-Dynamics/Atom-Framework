namespace Atom.Media.Codecs.Tests;

/// <summary>
/// Тесты MP4 round-trip (lossless AFRM).
/// </summary>
[TestFixture]
public sealed class Mp4Mp4CodecTests : VideoCodecTestBase<Mp4Codec, Mp4Codec>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "MP4";

    /// <inheritdoc/>
    protected override string EncoderExtension => ".mp4";

    /// <inheritdoc/>
    protected override string DecoderExtension => ".mp4";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 0; // AFRM Store = lossless

    #endregion
}
