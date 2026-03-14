using System.Buffers.Binary;
using System.Globalization;

namespace Atom.Media.Audio;

/// <summary>
/// Минимальный парсер WAV-файлов (RIFF/PCM).
/// </summary>
internal static class WavReader
{
    internal readonly struct WavInfo
    {
        public required int SampleRate { get; init; }
        public required int Channels { get; init; }
        public required int BitsPerSample { get; init; }
        public required AudioSampleFormat SampleFormat { get; init; }
        public required byte[] Data { get; init; }
    }

    internal static WavInfo Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Parse(bytes);
    }

    internal static WavInfo Read(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Parse(ms.GetBuffer().AsSpan(0, (int)ms.Length));
    }

    internal static WavInfo Parse(ReadOnlySpan<byte> data)
    {
        ValidateRiffHeader(data);

        var pos = 12;
        FmtChunk? fmt = null;

        while (pos + 8 <= data.Length)
        {
            var chunkId = data.Slice(pos, 4);
            var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos + 4, 4));

            if (chunkId is [(byte)'f', (byte)'m', (byte)'t', (byte)' '])
            {
                fmt = ParseFmtChunk(data, pos, chunkSize);
            }
            else if (chunkId is [(byte)'d', (byte)'a', (byte)'t', (byte)'a'])
            {
                return BuildWavInfo(data, pos, chunkSize, fmt);
            }

            pos += 8 + chunkSize;

            // RIFF chunks are word-aligned
            if (chunkSize % 2 != 0)
            {
                pos++;
            }
        }

        throw new InvalidDataException("WAV: data-чанк не найден.");
    }

    private static void ValidateRiffHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 44)
        {
            throw new InvalidDataException("WAV-файл слишком мал.");
        }

        if (data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F')
        {
            throw new InvalidDataException("Файл не является RIFF-контейнером.");
        }

        if (data[8] != 'W' || data[9] != 'A' || data[10] != 'V' || data[11] != 'E')
        {
            throw new InvalidDataException("Файл не является WAV.");
        }
    }

    private static FmtChunk ParseFmtChunk(ReadOnlySpan<byte> data, int pos, int chunkSize)
    {
        if (chunkSize < 16)
        {
            throw new InvalidDataException("WAV fmt-чанк слишком мал.");
        }

        return new FmtChunk
        {
            AudioFormat = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(pos + 8, 2)),
            Channels = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(pos + 10, 2)),
            SampleRate = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos + 12, 4)),
            BitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(pos + 22, 2)),
        };
    }

    private static WavInfo BuildWavInfo(ReadOnlySpan<byte> data, int pos, int chunkSize, FmtChunk? fmt)
    {
        if (fmt is not { } f)
        {
            throw new InvalidDataException("WAV: data-чанк обнаружен до fmt-чанка.");
        }

        var pcmData = data.Slice(pos + 8, Math.Min(chunkSize, data.Length - pos - 8));

        return new WavInfo
        {
            SampleRate = f.SampleRate,
            Channels = f.Channels,
            BitsPerSample = f.BitsPerSample,
            SampleFormat = MapFormat(f.AudioFormat, f.BitsPerSample),
            Data = pcmData.ToArray(),
        };
    }

    private static AudioSampleFormat MapFormat(int audioFormat, int bitsPerSample)
    {
        return audioFormat switch
        {
            // PCM
            1 => bitsPerSample switch
            {
                8 => AudioSampleFormat.U8,
                16 => AudioSampleFormat.S16,
                32 => AudioSampleFormat.S32,
                _ => throw new NotSupportedException(
                    "Неподдерживаемая глубина PCM: "
                    + bitsPerSample.ToString(CultureInfo.InvariantCulture) + " бит."),
            },
            // IEEE Float
            3 => bitsPerSample switch
            {
                32 => AudioSampleFormat.F32,
                64 => AudioSampleFormat.F64,
                _ => throw new NotSupportedException(
                    "Неподдерживаемая глубина IEEE float: "
                    + bitsPerSample.ToString(CultureInfo.InvariantCulture) + " бит."),
            },
            _ => throw new NotSupportedException(
                "Неподдерживаемый формат WAV: "
                + audioFormat.ToString(CultureInfo.InvariantCulture) + "."),
        };
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private struct FmtChunk
    {
        public int AudioFormat;
        public int Channels;
        public int SampleRate;
        public int BitsPerSample;
    }
}
