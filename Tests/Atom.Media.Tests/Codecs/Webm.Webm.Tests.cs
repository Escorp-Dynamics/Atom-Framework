namespace Atom.Media.Codecs.Tests;

/// <summary>
/// Тесты WebM round-trip (lossless AFRM).
/// </summary>
[TestFixture]
public sealed class WebmWebmCodecTests : VideoCodecTestBase<WebmCodec, WebmCodec>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "WebM";

    /// <inheritdoc/>
    protected override string EncoderExtension => ".webm";

    /// <inheritdoc/>
    protected override string DecoderExtension => ".webm";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 0; // AFRM Store = lossless

    #endregion
}
