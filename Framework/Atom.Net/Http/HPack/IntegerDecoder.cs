using System.Numerics;

namespace Atom.Net.Http.HPack;

internal struct IntegerDecoder
{
    private int _i;
    private int _m;

    /// <summary>
    /// Decodes the first byte of the integer.
    /// </summary>
    /// <param name="b">
    /// The first byte of the variable-length encoded integer.
    /// </param>
    /// <param name="prefixLength">
    /// The number of lower bits in this prefix byte that the
    /// integer has been encoded into. Must be between 1 and 8.
    /// Upper bits must be zero.
    /// </param>
    /// <param name="result">
    /// If decoded successfully, contains the decoded integer.
    /// </param>
    /// <returns>
    /// If the integer has been fully decoded, true.
    /// Otherwise, false -- <see cref="TryDecode(byte, out int)"/> must be called on subsequent bytes.
    /// </returns>
    /// <remarks>
    /// The term "prefix" can be confusing. From the HPACK spec:
    /// An integer is represented in two parts: a prefix that fills the current octet and an
    /// optional list of octets that are used if the integer value does not fit within the prefix.
    /// </remarks>
    public bool BeginTryDecode(byte b, int prefixLength, out int result)
    {
        if (b < ((1 << prefixLength) - 1))
        {
            result = b;
            return true;
        }

        _i = b;
        _m = 0;
        result = 0;
        return default;
    }

    /// <summary>
    /// Decodes subsequent bytes of an integer.
    /// </summary>
    /// <param name="b">The next byte.</param>
    /// <param name="result">
    /// If decoded successfully, contains the decoded integer.
    /// </param>
    /// <returns>If the integer has been fully decoded, true. Otherwise, false -- <see cref="TryDecode(byte, out int)"/> must be called on subsequent bytes.</returns>
    public bool TryDecode(byte b, out int result)
    {
        if (BitOperations.LeadingZeroCount(b) <= _m) throw new HPackDecodingException("Не удалось декодировать HPACK");

        _i += (b & 0x7f) << _m;
        if (_i < 0) throw new HPackDecodingException("Не удалось декодировать HPACK");

        _m += 7;

        if ((b & 128) is 0)
        {
            if (b is 0 && _m / 7 > 1) throw new HPackDecodingException("Не удалось декодировать HPACK");

            result = _i;
            return true;
        }

        result = default;
        return default;
    }
}