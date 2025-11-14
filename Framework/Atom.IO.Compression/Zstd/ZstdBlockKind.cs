namespace Atom.IO.Compression.Zstd;

internal enum ZstdBlockKind : byte
{
    None = 0,
    Raw = 1,
    Rle = 2,
}
