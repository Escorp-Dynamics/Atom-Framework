using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Atom.Threading;

namespace Atom.Media;

internal static unsafe class WhiteNoiseGenerator
{
    [SuppressMessage("Meziantou.Analyzer", "MA0051:Method is too long", Justification = "FFmpeg frame generation requires a fixed sequence of native API calls.")]
    public static void WriteVideoFrame(CodecContext* context, FormatContext* outputContext, int streamIndex, ref long videoPts, Locker? locker)
    {
        var frame = FFmpeg.Util.FrameAlloc();

        if (frame is null)
        {
            locker?.Release();
            throw new VideoStreamException("Не удалось выделить память для фрейма");
        }

        frame->Format = (int)context->PixelFormat;
        frame->width = context->width;
        frame->height = context->height;

        FFmpeg.Util.FrameGetBuffer(frame, 32).ThrowIfErrors("Ошибка выделения буфера для фрейма", locker);
        FFmpeg.Util.FrameMakeWritable(frame).ThrowIfErrors("Не удалось разрешить запись в фрейм", locker);

        var packet = FFmpeg.Codec.PacketAlloc();

        if (packet is null)
        {
            locker?.Release();
            throw new VideoStreamException("Не удалось выделить память для пакета");
        }

        GenerateWhiteNoiseVideo(context, frame, locker);

        if ((PixelFormat)frame->Format != context->PixelFormat)
        {
            var swsContext = FFmpeg.SwScale.GetContext(
                context->width, context->height, PixelFormat.YUV420P,
                context->width, context->height, context->PixelFormat,
                2, default, default, default
            );

            if (swsContext is null)
            {
                locker?.Release();
                throw new VideoStreamException("Не удалось создать контекст масштабирования фрейма");
            }

            FFmpeg.SwScale.Scale(swsContext, frame->data.Source, frame->linesize, 0, context->height, frame->data.Source, frame->linesize)
                .ThrowIfErrors("Не удалось отмасштабировать фрейм", locker);

            FFmpeg.SwScale.FreeContext(swsContext);
        }

        frame->pts = videoPts;
        frame->duration = 1;
        frame->pict_type = (videoPts is 0) ? 1 : 2;
        frame->flags = 0;
        frame->time_base = context->time_base;

        FFmpeg.Codec.SendFrame(context, frame).ThrowIfErrors("Не удалось отправить фрейм в кодек", locker);

        var result = FFmpeg.Codec.ReceivePacket(context, packet);
        const string error = "Не удалось освободить пакет";

        if (result is -11 or -541_478_725)
        {
            videoPts += FFmpeg.Util.ReScaleQ(1, context->time_base, context->time_base);

            FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors(error, locker);
            FFmpeg.Codec.PacketFree(&packet);
            FFmpeg.Util.FrameFree(&frame);

            WriteVideoFrame(context, outputContext, streamIndex, ref videoPts, locker);
            return;
        }

        result.ThrowIfErrors("Ошибка при получении пакета из кодека", locker);

        packet->pts = FFmpeg.Util.ReScaleQ(videoPts, context->time_base, outputContext->streams[streamIndex]->time_base);
        packet->dts = packet->pts;
        packet->duration = FFmpeg.Util.ReScaleQ(1, context->time_base, outputContext->streams[streamIndex]->time_base);
        packet->stream_index = outputContext->streams[streamIndex]->index;

        FFmpeg.Format.InterleavedWriteFrame(outputContext, packet).ThrowIfErrors("Не удалось записать фрейм", locker);
        videoPts += FFmpeg.Util.ReScaleQ(1, context->time_base, context->time_base);

        FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors(error, locker);
        FFmpeg.Codec.PacketFree(&packet);
        FFmpeg.Util.FrameFree(&frame);
    }

    [SuppressMessage("Meziantou.Analyzer", "MA0051:Method is too long", Justification = "FFmpeg frame generation requires a fixed sequence of native API calls.")]
    public static void WriteAudioFrame(CodecContext* context, FormatContext* outputContext, int streamIndex, ref long audioPts, Locker? locker)
    {
        var frame = FFmpeg.Util.FrameAlloc();

        if (frame is null)
        {
            locker?.Release();
            throw new VideoStreamException("Не удалось выделить память для фрейма");
        }

        frame->nb_samples = context->frame_size;
        frame->Format = (int)context->SampleFormat;
        frame->ChannelLayout = context->ChannelLayout;

        FFmpeg.Util.FrameGetBuffer(frame, 0).ThrowIfErrors("Ошибка выделения буфера для фрейма", locker);
        FFmpeg.Util.FrameMakeWritable(frame).ThrowIfErrors("Не удалось разрешить запись в фрейм", locker);

        var packet = FFmpeg.Codec.PacketAlloc();

        if (packet is null)
        {
            locker?.Release();
            throw new VideoStreamException("Не удалось выделить память для пакета");
        }

        GenerateWhiteNoiseAudio(context, frame);

        frame->pts = audioPts;
        frame->duration = context->frame_size;

        FFmpeg.Codec.SendFrame(context, frame).ThrowIfErrors("Не удалось отправить фрейм в кодек", locker);

        var result = FFmpeg.Codec.ReceivePacket(context, packet);

        if (result is -11 or -541_478_725)
        {
            audioPts += FFmpeg.Util.ReScaleQ(frame->nb_samples, context->time_base, context->time_base);

            FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors("Не удалось освободить пакет", locker);
            FFmpeg.Codec.PacketFree(&packet);
            FFmpeg.Util.FrameFree(&frame);
            WriteAudioFrame(context, outputContext, streamIndex, ref audioPts, locker);
            return;
        }

        result.ThrowIfErrors("Ошибка при получении пакета из кодека", locker);

        packet->pts = FFmpeg.Util.ReScaleQ(audioPts, context->time_base, outputContext->streams[streamIndex]->time_base);
        packet->dts = packet->pts;
        packet->duration = FFmpeg.Util.ReScaleQ(frame->nb_samples, context->time_base, outputContext->streams[streamIndex]->time_base);
        packet->stream_index = outputContext->streams[streamIndex]->index;

        FFmpeg.Format.InterleavedWriteFrame(outputContext, packet).ThrowIfErrors("Не удалось записать фрейм", locker);
        audioPts += FFmpeg.Util.ReScaleQ(frame->nb_samples, context->time_base, context->time_base);

        FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors("Не удалось освободить пакет", locker);
        FFmpeg.Codec.PacketFree(&packet);
        FFmpeg.Util.FrameFree(&frame);
    }

    private static void GenerateWhiteNoiseVideo(CodecContext* context, MediaFrame* frame, Locker? locker)
    {
        using var rng = RandomNumberGenerator.Create();

        if (context->PixelFormat is PixelFormat.YUV420P or PixelFormat.YUVJ420P)
        {
            var ySize = context->width * context->height;
            var uvSize = context->width / 2 * (context->height / 2);

            rng.GetBytes(new Span<byte>(frame->data[0], ySize));
            rng.GetBytes(new Span<byte>(frame->data[1], uvSize));
            rng.GetBytes(new Span<byte>(frame->data[2], uvSize));

            return;
        }

        if (context->PixelFormat is PixelFormat.YUVA420P)
        {
            var ySize = context->width * context->height;
            var uvSize = context->width / 2 * (context->height / 2);
            var aSize = context->width * context->height;

            rng.GetBytes(new Span<byte>(frame->data[0], ySize));
            rng.GetBytes(new Span<byte>(frame->data[1], uvSize));
            rng.GetBytes(new Span<byte>(frame->data[2], uvSize));
            rng.GetBytes(new Span<byte>(frame->data[3], aSize));

            return;
        }

        if (context->PixelFormat is PixelFormat.BGRA)
        {
            rng.GetBytes(new Span<byte>(frame->data[0], context->width * context->height * 4));
            return;
        }

        locker?.Release();
        throw new NotSupportedException($"Формат пикселей {context->PixelFormat} не поддерживается");
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
