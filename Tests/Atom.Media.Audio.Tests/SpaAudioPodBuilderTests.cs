using Atom.Media.Audio.Backends.PipeWire;

namespace Atom.Media.Audio.Tests;

[TestFixture]
public class SpaAudioPodBuilderTests(ILogger logger) : BenchmarkTests<SpaAudioPodBuilderTests>(logger)
{
    public SpaAudioPodBuilderTests() : this(ConsoleLogger.Unicode) { }

    // --- Pod Size ---

    [TestCase(TestName = "Pod: размер 136 байт для F32 mono")]
    public void PodSizeF32Mono()
    {
        Span<byte> buffer = stackalloc byte[256];
        var size = SpaAudioPodBuilder.BuildAudioFormatPod(buffer, 48000, 1, AudioSampleFormat.F32);
        Assert.That(size, Is.EqualTo(136));
    }

    [TestCase(TestName = "Pod: размер 136 байт для S16 stereo")]
    public void PodSizeS16Stereo()
    {
        Span<byte> buffer = stackalloc byte[256];
        var size = SpaAudioPodBuilder.BuildAudioFormatPod(buffer, 44100, 2, AudioSampleFormat.S16);
        Assert.That(size, Is.EqualTo(136));
    }

    [TestCase(TestName = "Pod: размер одинаков для всех форматов")]
    public void PodSizeSameForAllFormats()
    {
        Span<byte> buffer = stackalloc byte[256];
        var formats = new[]
        {
            AudioSampleFormat.U8, AudioSampleFormat.S16, AudioSampleFormat.S32,
            AudioSampleFormat.F32, AudioSampleFormat.F64,
            AudioSampleFormat.U8Planar, AudioSampleFormat.S16Planar,
            AudioSampleFormat.S32Planar, AudioSampleFormat.F32Planar, AudioSampleFormat.F64Planar,
        };

        foreach (var format in formats)
        {
            var size = SpaAudioPodBuilder.BuildAudioFormatPod(buffer, 48000, 1, format);
            Assert.That(size, Is.EqualTo(136), $"Format {format} produced unexpected pod size");
        }
    }

    // --- Format Mapping ---

    [TestCase(TestName = "ToSpaAudioFormat: U8 → 0x102")]
    public void FormatU8()
    {
        Assert.That(SpaAudioPodBuilder.ToSpaAudioFormat(AudioSampleFormat.U8), Is.EqualTo(0x102u));
    }

    [TestCase(TestName = "ToSpaAudioFormat: S16 → 0x103")]
    public void FormatS16()
    {
        Assert.That(SpaAudioPodBuilder.ToSpaAudioFormat(AudioSampleFormat.S16), Is.EqualTo(0x103u));
    }

    [TestCase(TestName = "ToSpaAudioFormat: S32 → 0x10b")]
    public void FormatS32()
    {
        Assert.That(SpaAudioPodBuilder.ToSpaAudioFormat(AudioSampleFormat.S32), Is.EqualTo(0x10bu));
    }

    [TestCase(TestName = "ToSpaAudioFormat: F32 → 0x11b")]
    public void FormatF32()
    {
        Assert.That(SpaAudioPodBuilder.ToSpaAudioFormat(AudioSampleFormat.F32), Is.EqualTo(0x11bu));
    }

    [TestCase(TestName = "ToSpaAudioFormat: F64 → 0x11d")]
    public void FormatF64()
    {
        Assert.That(SpaAudioPodBuilder.ToSpaAudioFormat(AudioSampleFormat.F64), Is.EqualTo(0x11du));
    }

    [TestCase(TestName = "ToSpaAudioFormat: U8Planar → 0x201")]
    public void FormatU8Planar()
    {
        Assert.That(SpaAudioPodBuilder.ToSpaAudioFormat(AudioSampleFormat.U8Planar), Is.EqualTo(0x201u));
    }

    [TestCase(TestName = "ToSpaAudioFormat: S16Planar → 0x202")]
    public void FormatS16Planar()
    {
        Assert.That(SpaAudioPodBuilder.ToSpaAudioFormat(AudioSampleFormat.S16Planar), Is.EqualTo(0x202u));
    }

    [TestCase(TestName = "ToSpaAudioFormat: S32Planar → 0x204")]
    public void FormatS32Planar()
    {
        Assert.That(SpaAudioPodBuilder.ToSpaAudioFormat(AudioSampleFormat.S32Planar), Is.EqualTo(0x204u));
    }

    [TestCase(TestName = "ToSpaAudioFormat: F32Planar → 0x206")]
    public void FormatF32Planar()
    {
        Assert.That(SpaAudioPodBuilder.ToSpaAudioFormat(AudioSampleFormat.F32Planar), Is.EqualTo(0x206u));
    }

    [TestCase(TestName = "ToSpaAudioFormat: F64Planar → 0x207")]
    public void FormatF64Planar()
    {
        Assert.That(SpaAudioPodBuilder.ToSpaAudioFormat(AudioSampleFormat.F64Planar), Is.EqualTo(0x207u));
    }

    [TestCase(TestName = "ToSpaAudioFormat: Unknown → VirtualMicrophoneException")]
    public void FormatUnknownThrows()
    {
        Assert.Throws<VirtualMicrophoneException>(() =>
            SpaAudioPodBuilder.ToSpaAudioFormat(AudioSampleFormat.Unknown));
    }

    // --- Pod Structure ---

    [TestCase(TestName = "Pod: media type = audio (1)")]
    public void PodMediaTypeAudio()
    {
        Span<byte> buffer = stackalloc byte[256];
        _ = SpaAudioPodBuilder.BuildAudioFormatPod(buffer, 48000, 1, AudioSampleFormat.F32);

        // After object header (8) + body type (4) + body id (4) = offset 16
        // First property: key (4) + flags (4) + pod header (8) + value (4) = mediaType value at offset 32
        var mediaType = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer[32..]);
        Assert.That(mediaType, Is.EqualTo(1u)); // SPA_MEDIA_TYPE_audio = 1
    }
}
