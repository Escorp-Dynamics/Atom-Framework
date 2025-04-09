using System.Buffers;
using System.Text;

namespace Atom.Net.Http.HPack;

internal static class HPackEncoder
{
    public static bool EncodeIndexedHeaderField(int index, Span<byte> destination, out int bytesWritten)
    {
        if (!destination.IsEmpty)
        {
            destination[0] = 0x80;
            return IntegerEncoder.Encode(index, 7, destination, out bytesWritten);
        }

        bytesWritten = default;
        return default;
    }

    public static bool EncodeStatusHeader(int statusCode, Span<byte> destination, out int bytesWritten)
    {
        if (StaticTable.TryGetStatusIndex(statusCode, out var index)) return EncodeIndexedHeaderField(index, destination, out bytesWritten);

        if (!EncodeLiteralHeaderFieldWithoutIndexing(StaticTable.Status200, destination, out var nameLength))
        {
            bytesWritten = default;
            return default;
        }

        var statusBytes = StatusCodes.ToStatusBytes(statusCode);

        if (!EncodeStringLiteral(statusBytes, destination[nameLength..], out var valueLength))
        {
            bytesWritten = default;
            return default;
        }

        bytesWritten = nameLength + valueLength;
        return true;
    }

    public static bool EncodeLiteralHeaderFieldNeverIndexing(int index, string value, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten)
    {
        if ((uint)destination.Length >= 2)
        {
            destination[0] = 0x10;

            if (IntegerEncoder.Encode(index, 4, destination, out var indexLength) && EncodeStringLiteral(value, valueEncoding, destination[indexLength..], out var nameLength))
            {
                bytesWritten = indexLength + nameLength;
                return true;
            }
        }

        bytesWritten = default;
        return default;
    }

    public static bool EncodeLiteralHeaderFieldIndexing(int index, string value, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten)
    {
        if ((uint)destination.Length >= 2)
        {
            destination[0] = 0x40;

            if (IntegerEncoder.Encode(index, 6, destination, out var indexLength) && EncodeStringLiteral(value, valueEncoding, destination[indexLength..], out var nameLength))
            {
                bytesWritten = indexLength + nameLength;
                return true;
            }
        }

        bytesWritten = default;
        return default;
    }

    public static bool EncodeLiteralHeaderFieldWithoutIndexing(int index, string value, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten)
    {
        if ((uint)destination.Length >= 2)
        {
            destination[0] = default;

            if (IntegerEncoder.Encode(index, 4, destination, out var indexLength) && EncodeStringLiteral(value, valueEncoding, destination[indexLength..], out var nameLength))
            {
                bytesWritten = indexLength + nameLength;
                return true;
            }
        }

        bytesWritten = default;
        return default;
    }

    public static bool EncodeLiteralHeaderFieldWithoutIndexing(int index, Span<byte> destination, out int bytesWritten)
    {
        if (!destination.IsEmpty)
        {
            destination[0] = default;

            if (IntegerEncoder.Encode(index, 4, destination, out var indexLength))
            {
                bytesWritten = indexLength;
                return true;
            }
        }

        bytesWritten = default;
        return default;
    }

    public static bool EncodeLiteralHeaderFieldIndexingNewName(string name, string value, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten) => EncodeLiteralHeaderNewNameCore(0x40, name, value, valueEncoding, destination, out bytesWritten);

    /// <summary>Encodes a "Literal Header Field never Indexing - New Name".</summary>
    public static bool EncodeLiteralHeaderFieldNeverIndexingNewName(string name, string value, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten) => EncodeLiteralHeaderNewNameCore(0x10, name, value, valueEncoding, destination, out bytesWritten);

    private static bool EncodeLiteralHeaderNewNameCore(byte mask, string name, string value, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten)
    {
        if ((uint)destination.Length >= 3)
        {
            destination[0] = mask;

            if (EncodeLiteralHeaderName(name, destination[1..], out var nameLength) && EncodeStringLiteral(value, valueEncoding, destination[(1 + nameLength)..], out var valueLength))
            {
                bytesWritten = 1 + nameLength + valueLength;
                return true;
            }
        }

        bytesWritten = default;
        return default;
    }

    public static bool EncodeLiteralHeaderFieldWithoutIndexingNewName(string name, string value, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten) => EncodeLiteralHeaderNewNameCore(0, name, value, valueEncoding, destination, out bytesWritten);

    public static bool EncodeLiteralHeaderFieldWithoutIndexingNewName(string name, ReadOnlySpan<string> values, byte[] separator, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten)
    {
        if ((uint)destination.Length >= 3)
        {
            destination[0] = default;

            if (EncodeLiteralHeaderName(name, destination[1..], out var nameLength) && EncodeStringLiterals(values, separator, valueEncoding, destination[(1 + nameLength)..], out var valueLength))
            {
                bytesWritten = 1 + nameLength + valueLength;
                return true;
            }
        }

        bytesWritten = default;
        return default;
    }

    public static bool EncodeLiteralHeaderFieldWithoutIndexingNewName(string name, Span<byte> destination, out int bytesWritten)
    {
        if ((uint)destination.Length >= 2)
        {
            destination[0] = default;

            if (EncodeLiteralHeaderName(name, destination[1..], out var nameLength))
            {
                bytesWritten = 1 + nameLength;
                return true;
            }
        }

        bytesWritten = default;
        return default;
    }

    private static bool EncodeLiteralHeaderName(string value, Span<byte> destination, out int bytesWritten)
    {
        if (!destination.IsEmpty)
        {
            destination[0] = default; // TODO: Use Huffman encoding

            if (IntegerEncoder.Encode(value.Length, 7, destination, out var integerLength))
            {
                destination = destination[integerLength..];

                if (value.Length <= destination.Length)
                {
                    bytesWritten = integerLength + value.Length;
                    return true;
                }
            }
        }

        bytesWritten = default;
        return default;
    }

    private static void EncodeValueStringPart(string value, Span<byte> destination)
    {
        var status = Ascii.FromUtf16(value, destination, out _);
        if (status is OperationStatus.InvalidData) throw new HttpRequestException("Неверная кодировка символа");
    }

    public static bool EncodeStringLiteral(ReadOnlySpan<byte> value, Span<byte> destination, out int bytesWritten)
    {
        if (!destination.IsEmpty)
        {
            destination[0] = default; // TODO: Use Huffman encoding

            if (IntegerEncoder.Encode(value.Length, 7, destination, out var integerLength))
            {
                destination = destination[integerLength..];

                if (value.Length <= destination.Length)
                {
                    value.CopyTo(destination);
                    bytesWritten = integerLength + value.Length;
                    return true;
                }
            }
        }

        bytesWritten = default;
        return default;
    }

    public static bool EncodeStringLiteral(string value, Span<byte> destination, out int bytesWritten) => EncodeStringLiteral(value, valueEncoding: null, destination, out bytesWritten);

    public static bool EncodeStringLiteral(string value, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten)
    {
        if (!destination.IsEmpty)
        {
            destination[0] = 0; // TODO: Use Huffman encoding

            var encodedStringLength = valueEncoding is null || ReferenceEquals(valueEncoding, Encoding.Latin1)
                ? value.Length
                : valueEncoding.GetByteCount(value);

            if (IntegerEncoder.Encode(encodedStringLength, 7, destination, out var integerLength))
            {
                destination = destination[integerLength..];

                if (encodedStringLength <= destination.Length)
                {
                    if (valueEncoding is null) EncodeValueStringPart(value, destination);

                    bytesWritten = integerLength + encodedStringLength;
                    return true;
                }
            }
        }

        bytesWritten = default;
        return default;
    }

    public static bool EncodeDynamicTableSizeUpdate(int value, Span<byte> destination, out int bytesWritten)
    {
        if (!destination.IsEmpty)
        {
            destination[0] = 0x20;
            return IntegerEncoder.Encode(value, 5, destination, out bytesWritten);
        }

        bytesWritten = default;
        return default;
    }

    public static bool EncodeStringLiterals(ReadOnlySpan<string> values, byte[] separator, Encoding? valueEncoding, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = default;

        if (values.IsEmpty) return EncodeStringLiteral(string.Empty, valueEncoding: null, destination, out bytesWritten);
        if (values.Length is 1) return EncodeStringLiteral(values[0], valueEncoding, destination, out bytesWritten);

        if (!destination.IsEmpty)
        {
            var valueLength = checked((values.Length - 1) * separator.Length);

            if (valueEncoding is null || ReferenceEquals(valueEncoding, Encoding.Latin1))
            {
                foreach (var part in values) valueLength = checked(valueLength + part.Length);
            }
            else
            {
                foreach (var part in values) valueLength = checked(valueLength + valueEncoding.GetByteCount(part));
            }

            destination[0] = default;

            if (IntegerEncoder.Encode(valueLength, 7, destination, out var integerLength))
            {
                destination = destination[integerLength..];

                if (destination.Length >= valueLength)
                {
                    if (valueEncoding is null)
                    {
                        var value = values[0];
                        EncodeValueStringPart(value, destination);
                        destination = destination[value.Length..];

                        for (var i = 1; i < values.Length; ++i)
                        {
                            separator.CopyTo(destination);
                            destination = destination[separator.Length..];

                            value = values[i];
                            EncodeValueStringPart(value, destination);
                            destination = destination[value.Length..];
                        }
                    }
                    else
                    {
                        var written = valueEncoding.GetBytes(values[0], destination);
                        destination = destination[written..];

                        for (var i = 1; i < values.Length; ++i)
                        {
                            separator.CopyTo(destination);
                            destination = destination[separator.Length..];

                            written = valueEncoding.GetBytes(values[i], destination);
                            destination = destination[written..];
                        }
                    }

                    bytesWritten = integerLength + valueLength;
                    return true;
                }
            }
        }

        return default;
    }

    public static byte[] EncodeLiteralHeaderFieldWithoutIndexingNewNameToAllocatedArray(string name)
    {
        Span<byte> span = stackalloc byte[256];
        EncodeLiteralHeaderFieldWithoutIndexingNewName(name, span, out var length);
        return span[..length].ToArray();
    }

    public static byte[] EncodeLiteralHeaderFieldWithoutIndexingToAllocatedArray(int index)
    {
        Span<byte> span = stackalloc byte[256];
        EncodeLiteralHeaderFieldWithoutIndexing(index, span, out var length);
        return span[..length].ToArray();
    }

    public static byte[] EncodeLiteralHeaderFieldWithoutIndexingToAllocatedArray(int index, string value)
    {
        Span<byte> span =
#if DEBUG
        stackalloc byte[4];
#else
        stackalloc byte[512];
#endif
        while (true)
        {
            if (EncodeLiteralHeaderFieldWithoutIndexing(index, value, valueEncoding: default, span, out var length))
                return span[..length].ToArray();

            span = new byte[span.Length * 2];
        }
    }
}