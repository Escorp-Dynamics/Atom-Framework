using System.Buffers.Binary;

namespace Atom.Media.Audio.Backends.PipeWire;

/// <summary>
/// Конструктор SPA Pod структур для описания формата аудиопотока PipeWire.
/// Строит бинарное представление напрямую в <see cref="Span{T}"/>.
/// </summary>
internal static class SpaAudioPodBuilder
{
    // SPA типы
    private const uint SPA_TYPE_Id = 3;
    private const uint SPA_TYPE_Int = 4;
    private const uint SPA_TYPE_Object = 15;

    // SPA Object
    private const uint SPA_TYPE_OBJECT_Format = 0x40003;
    private const uint SPA_PARAM_EnumFormat = 3;

    // SPA Format ключи
    private const uint SPA_FORMAT_mediaType = 1;
    private const uint SPA_FORMAT_mediaSubtype = 2;
    private const uint SPA_FORMAT_AUDIO_format = 0x10001;
    private const uint SPA_FORMAT_AUDIO_rate = 0x10003;
    private const uint SPA_FORMAT_AUDIO_channels = 0x10004;

    // SPA Media — значения из SpaConstants (сгенерированы из системных заголовков)
    private const uint SPA_MEDIA_TYPE_audio = SpaConstants.SPA_MEDIA_TYPE_audio;
    private const uint SPA_MEDIA_SUBTYPE_raw = SpaConstants.SPA_MEDIA_SUBTYPE_raw;

    /// <summary>
    /// Строит SPA Pod для описания формата аудиопотока.
    /// </summary>
    /// <param name="buffer">Целевой буфер (рекомендуется >= 256 байт).</param>
    /// <param name="sampleRate">Частота дискретизации.</param>
    /// <param name="channels">Количество каналов.</param>
    /// <param name="sampleFormat">Формат семплов.</param>
    /// <returns>Размер построенного pod в байтах.</returns>
    internal static int BuildAudioFormatPod(
        Span<byte> buffer, int sampleRate, int channels, AudioSampleFormat sampleFormat)
    {
        var offset = 0;

        // 5 свойств: mediaType, mediaSubtype, AUDIO_format (Id), AUDIO_rate (Int), AUDIO_channels (Int)
        const int propSize = 24;
        var objectBodySize = 8 + (propSize * 5);

        WritePodHeader(buffer, ref offset, objectBodySize, SPA_TYPE_Object);

        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], SPA_TYPE_OBJECT_Format);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], SPA_PARAM_EnumFormat);
        offset += 4;

        // Property: mediaType = audio
        WritePropId(buffer, ref offset, SPA_FORMAT_mediaType, SPA_MEDIA_TYPE_audio);

        // Property: mediaSubtype = raw
        WritePropId(buffer, ref offset, SPA_FORMAT_mediaSubtype, SPA_MEDIA_SUBTYPE_raw);

        // Property: AUDIO_format
        WritePropId(buffer, ref offset, SPA_FORMAT_AUDIO_format, ToSpaAudioFormat(sampleFormat));

        // Property: AUDIO_rate
        WritePropInt(buffer, ref offset, SPA_FORMAT_AUDIO_rate, sampleRate);

        // Property: AUDIO_channels
        WritePropInt(buffer, ref offset, SPA_FORMAT_AUDIO_channels, channels);

        return offset;
    }

    private static void WritePodHeader(Span<byte> buf, ref int offset, int bodySize, uint type)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], (uint)bodySize);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], type);
        offset += 4;
    }

    private static void WritePropId(Span<byte> buf, ref int offset, uint key, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], key);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], 0);
        offset += 4;

        WritePodHeader(buf, ref offset, bodySize: 4, SPA_TYPE_Id);

        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], value);
        offset += 4;

        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], 0);
        offset += 4;
    }

    private static void WritePropInt(Span<byte> buf, ref int offset, uint key, int value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], key);
        offset += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], 0);
        offset += 4;

        WritePodHeader(buf, ref offset, bodySize: 4, SPA_TYPE_Int);

        BinaryPrimitives.WriteInt32LittleEndian(buf[offset..], value);
        offset += 4;

        BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], 0);
        offset += 4;
    }

    internal static uint ToSpaAudioFormat(AudioSampleFormat format) => format switch
    {
        AudioSampleFormat.U8 => SpaConstants.SPA_AUDIO_FORMAT_U8,
        AudioSampleFormat.S16 => SpaConstants.SPA_AUDIO_FORMAT_S16_LE,
        AudioSampleFormat.S32 => SpaConstants.SPA_AUDIO_FORMAT_S32_LE,
        AudioSampleFormat.F32 => SpaConstants.SPA_AUDIO_FORMAT_F32_LE,
        AudioSampleFormat.F64 => SpaConstants.SPA_AUDIO_FORMAT_F64_LE,
        AudioSampleFormat.U8Planar => SpaConstants.SPA_AUDIO_FORMAT_U8P,
        AudioSampleFormat.S16Planar => SpaConstants.SPA_AUDIO_FORMAT_S16P,
        AudioSampleFormat.S32Planar => SpaConstants.SPA_AUDIO_FORMAT_S32P,
        AudioSampleFormat.F32Planar => SpaConstants.SPA_AUDIO_FORMAT_F32P,
        AudioSampleFormat.F64Planar => SpaConstants.SPA_AUDIO_FORMAT_F64P,
        _ => throw new VirtualMicrophoneException(
            $"Формат семплов {format} не поддерживается PipeWire."),
    };
}
