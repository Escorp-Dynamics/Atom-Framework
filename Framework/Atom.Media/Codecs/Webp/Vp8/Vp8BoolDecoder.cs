using System.Runtime.CompilerServices;

namespace Atom.Media.Codecs.Webp.Vp8;

/// <summary>
/// VP8 Boolean Arithmetic Decoder (range coder) per RFC 6386 Section 7.3.
/// </summary>
/// <remarks>
/// Exact translation of the reference decoder from RFC 6386 §20.2 (bool_decoder.h).
/// State: range (identical to encoder's range), value (at least 8 significant bits),
/// bit_count (# of bits shifted out of value, max 7).
/// Uses byte[] instead of ReadOnlySpan to allow storage in arrays (multiple token partitions).
/// </remarks>
internal struct Vp8BoolDecoder
{
    private readonly byte[] _data;

    public int Pos { get; private set; }

    private uint _range;    // 128 <= range <= 255 (after init)
    private uint _value;    // contains at least 8 significant bits
    private int _bitCount;  // # of bits shifted out of value, at most 7

    /// <summary>
    /// Initializes the boolean decoder from a data span.
    /// Per RFC 6386 §7.3: value = first 2 input bytes (big-endian), range = 255, bit_count = 0.
    /// </summary>
    public Vp8BoolDecoder(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
        Pos = 0;
        _range = 255;
        _bitCount = 0;

        // Value = first 2 input bytes, big-endian
        _value = 0;

        if (Pos < _data.Length)
        {
            _value = (uint)_data[Pos++] << 8;
        }

        if (Pos < _data.Length)
        {
            _value |= _data[Pos++];
        }
    }

    /// <summary>
    /// Decodes a single bit with the given probability (1–255).
    /// prob represents P(bit=0) = prob/256.
    /// Per RFC 6386 §7.3: bool_get / read_bool.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeBit(int prob)
    {
        // Split is identical to encoder's split
        var split = 1u + (((_range - 1) * (uint)prob) >> 8);
        var bigsplit = split << 8; // SPLIT in reference code: split << 8

        int bit;

        if (_value >= bigsplit) // encoded a one
        {
            bit = 1;
            _range -= split;
            _value -= bigsplit;
        }
        else // encoded a zero
        {
            bit = 0;
            _range = split;
        }

        // Shift out irrelevant value bits until range >= 128
        while (_range < 128)
        {
            _value <<= 1;
            _range <<= 1;

            if (++_bitCount == 8) // shift in new bits 8 at a time
            {
                _bitCount = 0;

                if (Pos < _data.Length)
                {
                    _value |= _data[Pos++];
                }
            }
        }

        return bit;
    }

    /// <summary>
    /// Decodes an unsigned n-bit literal (MSB first), each bit at probability 128 (uniform).
    /// Per RFC 6386 §7.3: read_literal.
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
    /// Decodes a signed n-bit value.
    /// Per RFC 6386 §7.3: read_signed_literal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeSigned(int n)
    {
        var value = (int)DecodeLiteral(n);
        return DecodeBit(128) != 0 ? -value : value;
    }

    /// <summary>
    /// Decodes a value from a binary tree using per-node probabilities.
    /// Per RFC 6386 §8.1: treed_read / bool_read_tree.
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
                return -i; // Leaf: negated value is the symbol
        }
    }

    /// <summary>
    /// Returns true if the decoder has consumed all input data.
    /// </summary>
    public readonly bool IsAtEnd => Pos >= _data.Length && _bitCount == 0;

    /// <summary>
    /// Number of bytes consumed from the input so far.
    /// </summary>
    public readonly int BytesRead => Pos;
}
