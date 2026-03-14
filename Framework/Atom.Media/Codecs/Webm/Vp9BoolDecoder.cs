#pragma warning disable S109

using System.Runtime.CompilerServices;

namespace Atom.Media.Codecs.Webm;

/// <summary>
/// VP9 Boolean Arithmetic Decoder (range coder) per RFC 7741 / VP9 Bitstream Specification §9.2.
/// </summary>
/// <remarks>
/// Identical algorithm to VP8 boolean decoder (RFC 6386 §7.3), adapted for VP9.
/// State: range (128–255), value (at least 8 significant bits), bit_count (0–7).
/// Uses byte[] to allow storage as a field (VP9 uses single-partition bitstream).
/// </remarks>
internal struct Vp9BoolDecoder
{
    private readonly byte[] _data;

    public int Pos { get; private set; }

    private uint _range;
    private uint _value;
    private int _bitCount;

    /// <summary>
    /// Initializes the boolean decoder from a data span.
    /// value = first 2 input bytes (big-endian), range = 255, bit_count = 0.
    /// </summary>
    public Vp9BoolDecoder(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
        Pos = 0;
        _range = 255;
        _bitCount = 0;
        _value = 0;

        if (Pos < _data.Length)
            _value = (uint)_data[Pos++] << 8;

        if (Pos < _data.Length)
            _value |= _data[Pos++];
    }

    /// <summary>
    /// Decodes a single bit with the given probability (1–255).
    /// prob represents P(bit = 0) = prob / 256.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeBit(int prob)
    {
        var split = 1u + (((_range - 1) * (uint)prob) >> 8);
        var bigsplit = split << 8;

        int bit;

        if (_value >= bigsplit)
        {
            bit = 1;
            _range -= split;
            _value -= bigsplit;
        }
        else
        {
            bit = 0;
            _range = split;
        }

        while (_range < 128)
        {
            _value <<= 1;
            _range <<= 1;

            if (++_bitCount == 8)
            {
                _bitCount = 0;

                if (Pos < _data.Length)
                    _value |= _data[Pos++];
            }
        }

        return bit;
    }

    /// <summary>
    /// Decodes an unsigned n-bit literal (MSB first), each bit at probability 128 (uniform).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint DecodeLiteral(int n)
    {
        var v = 0u;

        for (var i = n - 1; i >= 0; i--)
            v |= (uint)DecodeBit(128) << i;

        return v;
    }

    /// <summary>
    /// Decodes a signed n-bit value: magnitude then sign.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeSigned(int n)
    {
        var value = (int)DecodeLiteral(n);
        return DecodeBit(128) != 0 ? -value : value;
    }

    /// <summary>
    /// Decodes a value from a binary tree using per-node probabilities.
    /// tree[i] and tree[i+1] are children; negative values are leaves (negated symbol).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeTree(ReadOnlySpan<sbyte> tree, ReadOnlySpan<byte> probs)
    {
        var i = 0;

        while (true)
        {
            var bit = DecodeBit(probs[i >> 1]);
            i = tree[i + bit];

            if (i <= 0)
                return -i;
        }
    }

    /// <summary>
    /// Decodes a uniform-probability symbol in range [0, n).
    /// VP9 spec §9.2: non-power-of-2 uniform decode.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeUniform(int n)
    {
        if (n <= 1)
            return 0;

        var bits = 0;
        var v = n - 1;
        while (v > 0) { v >>= 1; bits++; }

        var m = (1 << bits) - n;
        var v2 = (int)DecodeLiteral(bits - 1);

        if (v2 < m)
            return v2;

        return (v2 << 1) - m + DecodeBit(128);
    }

    /// <summary>Returns true if the decoder has consumed all input data.</summary>
    public readonly bool IsAtEnd => Pos >= _data.Length && _bitCount == 0;

    /// <summary>Number of bytes consumed from the input.</summary>
    public readonly int BytesRead => Pos;
}
