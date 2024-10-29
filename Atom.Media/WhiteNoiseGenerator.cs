using System.Security.Cryptography;

namespace Atom.Media;

internal sealed unsafe class WhiteNoiseGenerator
{
    private long pts;
    private long audioPts;

    public void WriteVideoFrame(CodecContext* context, FormatContext* outputContext)
    {
        var frame = FFmpeg.Util.FrameAlloc();
        if (frame is null) throw new VideoStreamException("Не удалось выделить память для фрейма");

        frame->Format = context->Format;
        frame->width = context->width;
        frame->height = context->height;

        FFmpeg.Util.FrameGetBuffer(frame, 32).ThrowIfErrors("Ошибка выделения буфера для фрейма");
        FFmpeg.Util.FrameMakeWritable(frame).ThrowIfErrors("Не удалось разрешить запись в фрейм");

        var packet = FFmpeg.Codec.PacketAlloc();
        if (packet is null) throw new VideoStreamException("Не удалось выделить память для пакета");

        var data = GenerateWhiteNoiseVideo(context);

        fixed (byte* ptr = data)
        {
            Buffer.MemoryCopy(ptr, frame->data[0], frame->linesize[0], frame->linesize[0]);
            Buffer.MemoryCopy(ptr + frame->linesize[0], frame->data[1], frame->linesize[1], frame->linesize[1]);
            Buffer.MemoryCopy(ptr + frame->linesize[0] + frame->linesize[1], frame->data[2], frame->linesize[2], frame->linesize[2]);

            //frame->extended_data = &ptr;
        }

        frame->pts = FFmpeg.Util.ReScaleQ(pts, context->time_base, outputContext->streams[0]->time_base);
        frame->pkt_dts = frame->pts;
        frame->duration = FFmpeg.Util.ReScaleQ(1, context->time_base, outputContext->streams[0]->time_base);
        //frame->key_frame = (frame->pict_type is 1) ? 1 : 0;
        frame->pict_type = (pts is 0) ? 1 : 2;
        frame->flags = 0;
        frame->time_base = context->time_base;

        FFmpeg.Codec.SendFrame(context, frame).ThrowIfErrors("Не удалось отправить фрейм в кодек");
        FFmpeg.Codec.ReceivePacket(context, packet).ThrowIfErrors("Ошибка при получении пакета из кодека");

        packet->pts = FFmpeg.Util.ReScaleQ(pts, context->time_base, outputContext->streams[0]->time_base);
        packet->dts = packet->pts;
        packet->duration = FFmpeg.Util.ReScaleQ(1, context->time_base, outputContext->streams[0]->time_base);
        packet->stream_index = outputContext->streams[0]->index;

        FFmpeg.Format.InterleavedWriteFrame(outputContext, packet).ThrowIfErrors("Не удалось записать фрейм");

        pts += FFmpeg.Util.ReScaleQ(1, context->time_base, context->time_base);

        FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors("Не удалось освободить пакет");
        FFmpeg.Codec.PacketFree(&packet);
        FFmpeg.Util.FrameFree(&frame);
    }

    public void WriteAudioFrame(CodecContext* context, FormatContext* outputContext)
    {
        var packet = FFmpeg.Codec.PacketAlloc();
        if (packet is null) throw new VideoStreamException("Не удалось выделить память для пакета");

        var data = GenerateWhiteNoiseAudio(context);
        var frameSize = context->frame_size > 0 ? context->frame_size : context->sample_rate / context->framerate.num * 100;

        if (data.Length > frameSize * sizeof(short) * context->ChannelLayout.nb_channels)
            throw new VideoStreamException("Сгенерированный аудиофрейм превышает допустимый размер буфера.");

        fixed (byte* ptr = data) packet->data = ptr;
        packet->size = data.Length;
        packet->pts = FFmpeg.Util.ReScaleQ(audioPts, context->time_base, outputContext->streams[1]->time_base);
        packet->dts = packet->pts;
        packet->duration = FFmpeg.Util.ReScaleQ(frameSize, context->time_base, outputContext->streams[1]->time_base);
        packet->stream_index = outputContext->streams[1]->index;

        FFmpeg.Format.InterleavedWriteFrame(outputContext, packet).ThrowIfErrors("Не удалось записать фрейм");
        audioPts += frameSize;

        FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors("Не удалось освободить пакет");
        FFmpeg.Codec.PacketFree(&packet);
    }

    private static ReadOnlySpan<byte> GenerateWhiteNoiseVideo(CodecContext* context)
    {
        using var rng = RandomNumberGenerator.Create();

        if (context->Format is PixelFormat.YUV420P)
        {
            var frame = new byte[context->width * context->height + 2 * (context->width / 2 * context->height / 2)];

            var ySize = context->width * context->height;
            var uvSize = context->width / 2 * (context->height / 2);

            rng.GetBytes(frame, 0, ySize);
            rng.GetBytes(frame, ySize, uvSize);
            rng.GetBytes(frame, ySize + uvSize, uvSize);

            return frame;
        }
        else if (context->Format is PixelFormat.RGB24)
        {
            var frame = new byte[context->width * context->height * 3];
            rng.GetBytes(frame);
            return frame.AsSpan();
        }

        throw new NotSupportedException($"Формат пикселей {context->Format} не поддерживается");
    }

    private static ReadOnlySpan<byte> GenerateWhiteNoiseAudio(CodecContext* context)
    {
        using var rng = RandomNumberGenerator.Create();
        var audio = new byte[context->sample_rate * context->ChannelLayout.nb_channels * 2].AsSpan();

        rng.GetBytes(audio);
        return audio;
    }
}