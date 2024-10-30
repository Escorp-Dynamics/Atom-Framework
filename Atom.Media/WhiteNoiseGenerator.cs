using System.Security.Cryptography;

namespace Atom.Media;

internal sealed unsafe class WhiteNoiseGenerator
{
    private long videoPts;
    private long audioPts;

    public void WriteVideoFrame(CodecContext* context, FormatContext* outputContext, int streamIndex)
    {
        var frame = FFmpeg.Util.FrameAlloc();
        if (frame is null) throw new VideoStreamException("Не удалось выделить память для фрейма");

        frame->Format = (int)context->Format;
        frame->width = context->width;
        frame->height = context->height;

        FFmpeg.Util.FrameGetBuffer(frame, 32).ThrowIfErrors("Ошибка выделения буфера для фрейма");
        FFmpeg.Util.FrameMakeWritable(frame).ThrowIfErrors("Не удалось разрешить запись в фрейм");

        var packet = FFmpeg.Codec.PacketAlloc();
        if (packet is null) throw new VideoStreamException("Не удалось выделить память для пакета");

        GenerateWhiteNoiseVideo(context, frame);

        frame->pts = videoPts;
        frame->duration = 1;
        frame->pict_type = (videoPts is 0) ? 1 : 2;
        frame->flags = 0;
        frame->time_base = context->time_base;

        FFmpeg.Codec.SendFrame(context, frame).ThrowIfErrors("Не удалось отправить фрейм в кодек");

        var result = FFmpeg.Codec.ReceivePacket(context, packet);
        const string error = "Не удалось освободить пакет";

        if (result is -11 or -541_478_725)
        {
            videoPts += FFmpeg.Util.ReScaleQ(1, context->time_base, context->time_base);

            FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors(error);
            FFmpeg.Codec.PacketFree(&packet);
            FFmpeg.Util.FrameFree(&frame);

            WriteVideoFrame(context, outputContext, streamIndex);
            return;
        }

        result.ThrowIfErrors("Ошибка при получении пакета из кодека");

        packet->pts = FFmpeg.Util.ReScaleQ(videoPts, context->time_base, outputContext->streams[streamIndex]->time_base);
        packet->dts = packet->pts;
        packet->duration = FFmpeg.Util.ReScaleQ(1, context->time_base, outputContext->streams[streamIndex]->time_base);
        packet->stream_index = outputContext->streams[streamIndex]->index;

        FFmpeg.Format.InterleavedWriteFrame(outputContext, packet).ThrowIfErrors("Не удалось записать фрейм");

        videoPts += FFmpeg.Util.ReScaleQ(1, context->time_base, context->time_base);

        FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors(error);
        FFmpeg.Codec.PacketFree(&packet);
        FFmpeg.Util.FrameFree(&frame);
    }

    public void WriteAudioFrame(CodecContext* context, FormatContext* outputContext, int streamIndex)
    {
        var frame = FFmpeg.Util.FrameAlloc();
        if (frame is null) throw new VideoStreamException("Не удалось выделить память для фрейма");

        frame->nb_samples = context->frame_size;
        frame->Format = (int)context->SampleFormat;
        frame->ChannelLayout = context->ChannelLayout;

        FFmpeg.Util.FrameGetBuffer(frame, 0).ThrowIfErrors("Ошибка выделения буфера для фрейма");
        FFmpeg.Util.FrameMakeWritable(frame).ThrowIfErrors("Не удалось разрешить запись в фрейм");

        var packet = FFmpeg.Codec.PacketAlloc();
        if (packet is null) throw new VideoStreamException("Не удалось выделить память для пакета");

        GenerateWhiteNoiseAudio(context, frame);

        frame->pts = audioPts;
        frame->duration = context->frame_size;

        FFmpeg.Codec.SendFrame(context, frame).ThrowIfErrors("Не удалось отправить фрейм в кодек");

        var result = FFmpeg.Codec.ReceivePacket(context, packet);

        if (result is -11 or -541_478_725)
        {
            audioPts += FFmpeg.Util.ReScaleQ(frame->nb_samples, context->time_base, context->time_base);

            FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors("Не удалось освободить пакет");
            FFmpeg.Codec.PacketFree(&packet);
            FFmpeg.Util.FrameFree(&frame);
            WriteAudioFrame(context, outputContext, streamIndex);
            return;
        }

        result.ThrowIfErrors("Ошибка при получении пакета из кодека");

        packet->pts = FFmpeg.Util.ReScaleQ(audioPts, context->time_base, outputContext->streams[streamIndex]->time_base);
        packet->dts = packet->pts;
        packet->duration = FFmpeg.Util.ReScaleQ(frame->nb_samples, context->time_base, outputContext->streams[streamIndex]->time_base);
        packet->stream_index = outputContext->streams[streamIndex]->index;

        FFmpeg.Format.InterleavedWriteFrame(outputContext, packet).ThrowIfErrors("Не удалось записать фрейм");

        audioPts += FFmpeg.Util.ReScaleQ(frame->nb_samples, context->time_base, context->time_base);

        FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors("Не удалось освободить пакет");
        FFmpeg.Codec.PacketFree(&packet);
        FFmpeg.Util.FrameFree(&frame);
    }

    private static void GenerateWhiteNoiseVideo(CodecContext* context, MediaFrame* frame)
    {
        using var rng = RandomNumberGenerator.Create();

        if (context->Format is PixelFormat.YUV420P)
        {
            var ySize = context->width * context->height;
            var uvSize = context->width / 2 * (context->height / 2);

            rng.GetBytes(new Span<byte>(frame->data[0], ySize));
            rng.GetBytes(new Span<byte>(frame->data[1], uvSize));
            rng.GetBytes(new Span<byte>(frame->data[2], uvSize));

            return;
        }

        throw new NotSupportedException($"Формат пикселей {context->Format} не поддерживается");
    }

    private static void GenerateWhiteNoiseAudio(CodecContext* context, MediaFrame* frame)
    {
        using var rng = RandomNumberGenerator.Create();
        var totalSamples = frame->nb_samples * context->ChannelLayout.nb_channels;
        var audioData = new Span<short>((short*)frame->data[0], totalSamples);

        var randomBytes = new byte[totalSamples * sizeof(short)];
        rng.GetBytes(randomBytes);

        for (var i = 0; i < totalSamples; ++i) audioData[i] = BitConverter.ToInt16(randomBytes, i * sizeof(short));
    }
}