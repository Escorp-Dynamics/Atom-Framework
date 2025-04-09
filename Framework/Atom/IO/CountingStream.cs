#pragma warning disable CA1835

using System.Runtime.CompilerServices;

namespace Atom.IO;

/// <summary>
/// Представляет поток с поддержкой подсчёта байт.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="CountingStream"/>.
/// </remarks>
/// <param name="baseStream">Ссылка на базовый поток.</param>
/// <param name="writeCallback">Происходит при записи в поток.</param>
/// <param name="readCallback">Происходит при чтении из потока.</param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public class CountingStream(Stream baseStream, Action<int>? writeCallback, Action<int>? readCallback) : Stream
{
    private ulong totalWritten;
    private ulong totalRead;

    private readonly Action<int>? onWrite = writeCallback;
    private readonly Action<int>? onRead = readCallback;

    /// <summary>
    /// Ссылка на базовый поток.
    /// </summary>
    public Stream BaseStream { get; protected set; } = baseStream;

    /// <summary>
    /// Суммарное количество записанных байт.
    /// </summary>
    public ulong TotalWritten => Interlocked.Read(ref totalWritten);

    /// <summary>
    /// Суммарное количество прочитанных байт.
    /// </summary>
    public ulong TotalRead => Interlocked.Read(ref totalRead);

    /// <summary>
    /// Суммарное количество записанных и прочитанных байт.
    /// </summary>
    public ulong Total => TotalWritten + TotalRead;

    /// <inheritdoc/>
    public override bool CanRead => BaseStream.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => BaseStream.CanSeek;

    /// <inheritdoc/>
    public override bool CanWrite => BaseStream.CanWrite;

    /// <inheritdoc/>
    public override long Length => BaseStream.Length;

    /// <inheritdoc/>
    public override long Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BaseStream.Position;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => BaseStream.Position = value;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="CountingStream"/>.
    /// </summary>
    /// <param name="baseStream">Ссылка на базовый поток.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CountingStream(Stream baseStream) : this(baseStream, default, default) { }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int Read(byte[] buffer, int offset, int count)
    {
        var length = Update(ref totalRead, BaseStream.Read(buffer, offset, count));
        onRead?.Invoke(length);
        return length;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(byte[] buffer, int offset, int count)
    {
        BaseStream.Write(buffer, offset, count);
        Update(ref totalWritten, count);
        onWrite?.Invoke(count);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Flush() => BaseStream.Flush();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override long Seek(long offset, SeekOrigin origin) => BaseStream.Seek(offset, origin);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SetLength(long value) => BaseStream.SetLength(value);

    /// <summary>
    /// Сбрасывает данные счётчиков.
    /// </summary>
    /// <param name="totalRead">Указывает, требуется ли сбросить количество прочитанных байт.</param>
    /// <param name="totalWritten">Указывает, требуется ли сбросить количество записанных байт.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset(bool totalRead, bool totalWritten)
    {
        if (totalRead) Interlocked.Exchange(ref this.totalRead, default);
        if (totalWritten) Interlocked.Exchange(ref this.totalWritten, default);
    }

    /// <summary>
    /// Сбрасывает данные счётчиков.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset() => Reset(true, true);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Update(ref ulong location, int value)
    {
        if (value > 0) Interlocked.Add(ref location, (ulong)value);
        return value;
    }
}