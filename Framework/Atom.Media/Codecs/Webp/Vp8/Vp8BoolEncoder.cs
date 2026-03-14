#pragma warning disable MA0182

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media.Codecs.Webp.Vp8;

/// <summary>
/// VP8 Boolean Arithmetic Encoder (range coder) per RFC 6386 Section 7.3.
/// </summary>
/// <remarks>
/// Exact translation of the C encoder from RFC 6386.
/// State: range (128–255), bottom (accumulator), bit_count (24 → 0 per byte).
/// Carry propagation walks backward through already-written output.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal ref struct Vp8BoolEncoder
{
    private readonly Span<byte> _output;

    public int Pos { get; private set; }

    private uint _range;   // 128 <= range <= 255
    private uint _bottom;  // minimum value of remaining output
    private int _bitCount; // shifts before an output byte is available (starts at 24)

    /// <summary>
    /// Initializes the boolean encoder writing to the given output span.
    /// Per RFC 6386 §7.3: range=255, bottom=0, bit_count=24.
    /// </summary>
    public Vp8BoolEncoder(Span<byte> output)
    {
        _output = output;
        Pos = 0;
        _range = 255;
        _bottom = 0;
        _bitCount = 24;
    }

    /// <summary>
    /// Encodes a single bit with the given probability (1–255).
    /// prob represents P(bit=0) = prob/256.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EncodeBit(int bit, int prob)
    {
        var split = 1u + (((_range - 1) * (uint)prob) >> 8);

        if (bit != 0)
        {
            _bottom += split; // move up bottom of interval
            _range -= split;  // corresponding decrease in range
        }
        else
        {
            _range = split; // decrease range, leaving bottom alone
        }

        // Renormalize: double range until >= 128, shifting bits out of bottom
        while (_range < 128)
        {
            _range <<= 1;

            if ((_bottom & (1u << 31)) != 0) // detect carry
                AddOneToOutput();

            _bottom <<= 1; // shift bottom AFTER carry detection

            if (--_bitCount == 0) // write out high byte of bottom
            {
                _output[Pos++] = (byte)(_bottom >> 24);
                _bottom &= (1u << 24) - 1; // keep low 3 bytes
                _bitCount = 8;              // 8 shifts until next output
            }
        }
    }

    /// <summary>
    /// Encodes an unsigned n-bit literal (MSB first), each bit at probability 128 (uniform).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EncodeLiteral(uint value, int n)
    {
        for (var i = n - 1; i >= 0; i--)
            EncodeBit((int)((value >> i) & 1), 128);
    }

    /// <summary>
    /// Encodes a signed n-bit value: first the magnitude, then the sign bit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EncodeSigned(int value, int n)
    {
        EncodeLiteral((uint)Math.Abs(value), n);

        if (value != 0)
            EncodeBit(value < 0 ? 1 : 0, 128);
    }

    /// <summary>
    /// Flushes remaining bits to the output buffer.
    /// Must be called exactly once after encoding the last bool.
    /// Per RFC 6386 §7.3 flush_bool_encoder.
    /// </summary>
    public void Flush()
    {
        var c = _bitCount;
        var v = _bottom;

        if ((v & (1u << (32 - c))) != 0) // propagate (unlikely) carry
            AddOneToOutput();

        v <<= c & 7; // shift remaining output before byte alignment
        c >>= 3;     // to top of internal buffer

        while (--c >= 0)
            v <<= 8;

        c = 4;

        while (--c >= 0) // write remaining data, possibly padded
        {
            _output[Pos++] = (byte)(v >> 24);
            v <<= 8;
        }
    }

    /// <summary>
    /// Number of bytes written to the output so far.
    /// </summary>
    public readonly int BytesWritten => Pos;

    /// <summary>
    /// Propagates a carry backward through already-written output bytes.
    /// Per RFC 6386: "while (*--q == 255) *q = 0; ++*q;"
    /// The arithmetic guarantees propagation never goes beyond the beginning.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private readonly void AddOneToOutput()
    {
        var q = Pos;

        while (--q >= 0 && _output[q] == 255)
            _output[q] = 0;

        if (q >= 0)
            _output[q]++;
    }
}
