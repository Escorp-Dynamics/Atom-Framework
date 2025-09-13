namespace Atom.IO.Compression.Zstd;

internal readonly struct ZstdEncoderSettings
{
    public bool IsContentChecksumEnabled { get; init; }

    public bool IsSingleSegment { get; init; }

    public int WindowSize { get; init; }

    public uint? DictionaryId { get; init; }

    public ulong? FrameContentSize { get; init; }

    public int BlockCap { get; init; }

    public int CompressionLevel { get; init; }
}