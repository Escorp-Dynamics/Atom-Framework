using System.Buffers.Binary;
using Atom.Media.Audio;

namespace Atom.Media.Audio.Tests;

[TestFixture]
public class WavReaderTests(ILogger logger) : BenchmarkTests<WavReaderTests>(logger)
{
    public WavReaderTests() : this(ConsoleLogger.Unicode) { }

    // --- Helpers ---

    private static byte[] CreateWav(
        int sampleRate, int channels, int bitsPerSample, int audioFormat, byte[]? pcmData = null)
    {
        pcmData ??= new byte[sampleRate * channels * (bitsPerSample / 8)];
        var fmtChunkSize = 16;
        var dataChunkSize = pcmData.Length;
        var totalSize = 4 + (8 + fmtChunkSize) + (8 + dataChunkSize);
        var wav = new byte[12 + (8 + fmtChunkSize) + (8 + dataChunkSize)];
        var span = wav.AsSpan();

        // RIFF header
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), totalSize);
        "WAVE"u8.CopyTo(span.Slice(8));

        // fmt chunk
        var pos = 12;
        "fmt "u8.CopyTo(span.Slice(pos));
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos + 4), fmtChunkSize);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(pos + 8), (short)audioFormat);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(pos + 10), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos + 12), sampleRate);
        var byteRate = sampleRate * channels * (bitsPerSample / 8);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos + 16), byteRate);
        var blockAlign = (short)(channels * (bitsPerSample / 8));
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(pos + 20), blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(pos + 22), (short)bitsPerSample);

        // data chunk
        pos += 8 + fmtChunkSize;
        "data"u8.CopyTo(span.Slice(pos));
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos + 4), dataChunkSize);
        pcmData.CopyTo(span.Slice(pos + 8));

        return wav;
    }

    // --- Валидные WAV-файлы ---

    [TestCase(TestName = "WavReader: PCM S16 mono 48000 Hz")]
    public void ParsePcmS16Mono48000()
    {
        var pcm = new byte[480 * 2]; // 10 мс при 48000 Hz, 1 канал, S16
        var wav = CreateWav(sampleRate: 48000, channels: 1, bitsPerSample: 16, audioFormat: 1, pcm);

        var info = WavReader.Parse(wav);

        Assert.Multiple(() =>
        {
            Assert.That(info.SampleRate, Is.EqualTo(48000));
            Assert.That(info.Channels, Is.EqualTo(1));
            Assert.That(info.BitsPerSample, Is.EqualTo(16));
            Assert.That(info.SampleFormat, Is.EqualTo(AudioSampleFormat.S16));
            Assert.That(info.Data, Has.Length.EqualTo(pcm.Length));
        });
    }

    [TestCase(TestName = "WavReader: PCM S32 stereo 44100 Hz")]
    public void ParsePcmS32Stereo44100()
    {
        var pcm = new byte[441 * 2 * 4]; // 10 мс при 44100 Hz, 2 канала, S32
        var wav = CreateWav(sampleRate: 44100, channels: 2, bitsPerSample: 32, audioFormat: 1, pcm);

        var info = WavReader.Parse(wav);

        Assert.Multiple(() =>
        {
            Assert.That(info.SampleRate, Is.EqualTo(44100));
            Assert.That(info.Channels, Is.EqualTo(2));
            Assert.That(info.BitsPerSample, Is.EqualTo(32));
            Assert.That(info.SampleFormat, Is.EqualTo(AudioSampleFormat.S32));
            Assert.That(info.Data, Has.Length.EqualTo(pcm.Length));
        });
    }

    [TestCase(TestName = "WavReader: IEEE Float F32 mono 48000 Hz")]
    public void ParseFloat32Mono48000()
    {
        var pcm = new byte[480 * 4]; // 10 мс при 48000 Hz, F32
        var wav = CreateWav(sampleRate: 48000, channels: 1, bitsPerSample: 32, audioFormat: 3, pcm);

        var info = WavReader.Parse(wav);

        Assert.Multiple(() =>
        {
            Assert.That(info.SampleRate, Is.EqualTo(48000));
            Assert.That(info.Channels, Is.EqualTo(1));
            Assert.That(info.SampleFormat, Is.EqualTo(AudioSampleFormat.F32));
            Assert.That(info.Data, Has.Length.EqualTo(pcm.Length));
        });
    }

    [TestCase(TestName = "WavReader: IEEE Float F64 stereo 96000 Hz")]
    public void ParseFloat64Stereo96000()
    {
        var pcm = new byte[960 * 2 * 8]; // 10 мс при 96000 Hz, 2 канала, F64
        var wav = CreateWav(sampleRate: 96000, channels: 2, bitsPerSample: 64, audioFormat: 3, pcm);

        var info = WavReader.Parse(wav);

        Assert.Multiple(() =>
        {
            Assert.That(info.SampleRate, Is.EqualTo(96000));
            Assert.That(info.Channels, Is.EqualTo(2));
            Assert.That(info.SampleFormat, Is.EqualTo(AudioSampleFormat.F64));
        });
    }

    [TestCase(TestName = "WavReader: PCM U8 mono 8000 Hz")]
    public void ParsePcmU8Mono8000()
    {
        var pcm = new byte[80]; // 10 мс при 8000 Hz, 1 канал, U8
        var wav = CreateWav(sampleRate: 8000, channels: 1, bitsPerSample: 8, audioFormat: 1, pcm);

        var info = WavReader.Parse(wav);

        Assert.Multiple(() =>
        {
            Assert.That(info.SampleRate, Is.EqualTo(8000));
            Assert.That(info.SampleFormat, Is.EqualTo(AudioSampleFormat.U8));
        });
    }

    [TestCase(TestName = "WavReader: данные PCM совпадают с оригиналом")]
    public void ParsePcmDataMatchesOriginal()
    {
        var pcm = new byte[100];
        Random.Shared.NextBytes(pcm);
        var wav = CreateWav(sampleRate: 48000, channels: 1, bitsPerSample: 16, audioFormat: 1, pcm);

        var info = WavReader.Parse(wav);

        Assert.That(info.Data, Is.EqualTo(pcm));
    }

    // --- Невалидные WAV-файлы ---

    [TestCase(TestName = "WavReader: файл слишком мал → InvalidDataException")]
    public void ParseTooSmallThrows()
    {
        var data = new byte[20];
        Assert.Throws<InvalidDataException>(() => WavReader.Parse(data));
    }

    [TestCase(TestName = "WavReader: не RIFF → InvalidDataException")]
    public void ParseNotRiffThrows()
    {
        var data = new byte[44];
        "XXXX"u8.CopyTo(data);
        Assert.Throws<InvalidDataException>(() => WavReader.Parse(data));
    }

    [TestCase(TestName = "WavReader: не WAVE → InvalidDataException")]
    public void ParseNotWaveThrows()
    {
        var data = new byte[44];
        "RIFF"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 36);
        "AVI "u8.CopyTo(data.AsSpan(8));
        Assert.Throws<InvalidDataException>(() => WavReader.Parse(data));
    }

    [TestCase(TestName = "WavReader: data до fmt → InvalidDataException")]
    public void ParseDataBeforeFmtThrows()
    {
        // RIFF header
        var data = new byte[56];
        "RIFF"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 48);
        "WAVE"u8.CopyTo(data.AsSpan(8));

        // data chunk first (before fmt)
        "data"u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 0);

        Assert.Throws<InvalidDataException>(() => WavReader.Parse(data));
    }

    [TestCase(TestName = "WavReader: нет data-чанка → InvalidDataException")]
    public void ParseMissingDataChunkThrows()
    {
        // WAV only with fmt, no data
        var data = new byte[36];
        "RIFF"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 28);
        "WAVE"u8.CopyTo(data.AsSpan(8));
        "fmt "u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 16);

        Assert.Throws<InvalidDataException>(() => WavReader.Parse(data));
    }

    [TestCase(TestName = "WavReader: fmt-чанк < 16 байт → InvalidDataException")]
    public void ParseFmtTooSmallThrows()
    {
        var data = new byte[44];
        "RIFF"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 36);
        "WAVE"u8.CopyTo(data.AsSpan(8));
        "fmt "u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 8); // too small

        Assert.Throws<InvalidDataException>(() => WavReader.Parse(data));
    }

    [TestCase(TestName = "WavReader: неподдерживаемый audioFormat → NotSupportedException")]
    public void ParseUnsupportedAudioFormatThrows()
    {
        var wav = CreateWav(sampleRate: 48000, channels: 1, bitsPerSample: 16, audioFormat: 99);
        Assert.Throws<NotSupportedException>(() => WavReader.Parse(wav));
    }

    [TestCase(TestName = "WavReader: неподдерживаемая глубина PCM 24 бит → NotSupportedException")]
    public void ParseUnsupportedBitDepthThrows()
    {
        var wav = CreateWav(sampleRate: 48000, channels: 1, bitsPerSample: 24, audioFormat: 1);
        Assert.Throws<NotSupportedException>(() => WavReader.Parse(wav));
    }

    // --- Чтение с диска ---

    [TestCase(TestName = "WavReader: Read из файла")]
    public void ReadFromFile()
    {
        var pcm = new byte[960];
        Random.Shared.NextBytes(pcm);
        var wav = CreateWav(sampleRate: 48000, channels: 1, bitsPerSample: 16, audioFormat: 1, pcm);

        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, wav);
            var info = WavReader.Read(path);

            Assert.Multiple(() =>
            {
                Assert.That(info.SampleRate, Is.EqualTo(48000));
                Assert.That(info.Data, Is.EqualTo(pcm));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestCase(TestName = "WavReader: Read из Stream")]
    public void ReadFromStream()
    {
        var pcm = new byte[960];
        Random.Shared.NextBytes(pcm);
        var wav = CreateWav(sampleRate: 48000, channels: 1, bitsPerSample: 16, audioFormat: 1, pcm);

        using var stream = new MemoryStream(wav);
        var info = WavReader.Read(stream);

        Assert.Multiple(() =>
        {
            Assert.That(info.SampleRate, Is.EqualTo(48000));
            Assert.That(info.Channels, Is.EqualTo(1));
            Assert.That(info.SampleFormat, Is.EqualTo(AudioSampleFormat.S16));
            Assert.That(info.Data, Is.EqualTo(pcm));
        });
    }
}
