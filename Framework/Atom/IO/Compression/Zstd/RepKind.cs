namespace Atom.IO.Compression.Zstd;

internal enum RepKind : byte
{
    None = 0,     // обычное смещение (не repeat)
    Rep1 = 1,     // Repeated_Offset1 (самое последнее)
    Rep2 = 2,     // Repeated_Offset2
    Rep3 = 3,     // Repeated_Offset3
    Rep1Minus1 = 4// Спец-случай при LL=0: Repeated_Offset1 - 1 byte
}