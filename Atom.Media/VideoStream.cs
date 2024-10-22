using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Atom.Media;

/// <summary>
/// Представляет контекст медиа формата.
/// </summary>
public class VideoStream : Stream
{
    private readonly int index;
    private Size resolution;
    private PixelFormat pixelFormat;

    private nint formatContext;
    private nint codecContext;
    private nint frame;
    private nint packet;
    private bool isDisposed;

    /// <summary>
    /// Указывает, был ли закрыт медиа вход.
    /// </summary>
    public bool IsClosed { get; protected set; }

    /// <inheritdoc/>
    public override bool CanRead { get; } = true;

    /// <inheritdoc/>
    public override bool CanSeek { get; } = true;

    /// <inheritdoc/>
    public override bool CanWrite { get; }

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// Получает или задает формат пикселей для потока.
    /// </summary>
    public PixelFormat PixelFormat
    {
        get => pixelFormat;

        set
        {
            if (frame != nint.Zero) FFmpeg.SetFrameFormat(frame, (int)value);
            pixelFormat = value;
        }
    }

    /// <summary>
    /// Получает или задает разрешение видео для потока.
    /// </summary>
    public Size Resolution
    {
        get => resolution;

        set
        {
            if (frame != nint.Zero)
            {
                FFmpeg.SetFrameWidth(frame, value.Width);
                FFmpeg.SetFrameHeight(frame, value.Height);
                _ = FFmpeg.GetFrameBuffer(frame, 1);
            }

            resolution = value;
        }
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStream"/>.
    /// </summary>
    /// <param name="path">Путь к файлу видео.</param>
    /// <param name="resolution">Разрешение видео.</param>
    /// <param name="pixelFormat">Формат пикселей видео.</param>
    public VideoStream(string path, Size resolution, PixelFormat pixelFormat)
    {
        var result = FFmpeg.OpenInput(out formatContext, path, nint.Zero, nint.Zero);
        if (result < 0) throw new IOException("Не удалось открыть входной поток");

        result = FFmpeg.FindStreamInfo(formatContext, nint.Zero);
        if (result < 0) throw new IOException("Не удалось найти информацию о потоке");

        index = FFmpeg.FindBestStream(formatContext, 0, -1, -1, nint.Zero, 0);
        if (index < 0) throw new IOException("Медиапоток не найден");

        var stream = Marshal.ReadIntPtr(formatContext + 64 + (index * IntPtr.Size));
        codecContext = Marshal.ReadIntPtr(stream + 32);

        result = FFmpeg.Open2(formatContext, IntPtr.Zero, IntPtr.Zero);
        if (result < 0) throw new IOException("Не удалось открыть кодек");

        frame = FFmpeg.AllocFrame();
        packet = FFmpeg.AllocPacket();

        this.resolution = resolution;
        this.pixelFormat = pixelFormat;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStream"/>.
    /// </summary>
    /// <param name="url">URI-адрес видеофайла.</param>
    /// <param name="resolution">Разрешение видео.</param>
    /// <param name="pixelFormat">Формат пикселей видео.</param>
    public VideoStream([NotNull] Uri url, Size resolution, PixelFormat pixelFormat) : this(url.AbsoluteUri, resolution, pixelFormat) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStream"/>.
    /// </summary>
    /// <param name="resolution">Разрешение видео.</param>
    /// <param name="pixelFormat">Формат пикселей видео.</param>
    public VideoStream(Size resolution, PixelFormat pixelFormat) : this(string.Empty, resolution, pixelFormat) { }

    //public bool ReadFrame() => FFmpeg.ReadFrame(formatContext, packet) >= 0;

    /// <summary>
    /// Закрывает медиа вход.
    /// </summary>
    public override void Close()
    {
        if (IsClosed) return;
        IsClosed = true;

        if (formatContext != nint.Zero) FFmpeg.CloseInput(ref formatContext);
        if (codecContext != nint.Zero) FFmpeg.FreeContext(ref codecContext);
        if (frame != nint.Zero) FFmpeg.FreeFrame(ref frame);
        if (packet != nint.Zero) FFmpeg.FreePacket(ref packet);
    }

    /// <summary>
    /// Освобождает неуправляемые ресурсы, используемые объектом.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (isDisposed) return;
        isDisposed = true;

        Close();
        base.Dispose(disposing);
    }

    /// <summary>
    /// Деструктор класса MediaStream, вызывающий метод Dispose(false).
    /// </summary>
    ~VideoStream() => Dispose(disposing: false);

    /// <inheritdoc/>
    public override void Flush() { }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        var result = FFmpeg.ReadFrame(formatContext, packet);

        if (result < 0)
        {
            _ = FFmpeg.SeekFile(formatContext, -1, 0, 0, 0, 0);
            return 0;
        }

        result = FFmpeg.SendPacket(codecContext, packet);
        if (result < 0) throw new IOException("Ошибка отправки пакета в кодек");

        result = FFmpeg.ReceiveFrame(codecContext, frame);
        if (result < 0) throw new IOException("Ошибка получения фрейма из кодека");

        var bufferSize = FFmpeg.GetImageBufferSize((int)pixelFormat, resolution.Width, resolution.Height, 1);
        if (bufferSize > count) throw new ArgumentException("Буфер слишком мал для данных фрейма");

        var dstData = Marshal.ReadIntPtr(frame + 40);
        Marshal.Copy(dstData, buffer, offset, bufferSize);

        FFmpeg.UnRefPacket(packet);
        return bufferSize;
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        var result = FFmpeg.SeekFrame(formatContext, index, offset, (int)origin);
        return result < 0 ? throw new IOException("Не удалось выполнить поиск в потоке") : offset;
    }

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}