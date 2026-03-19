using System.Runtime.CompilerServices;

namespace Atom.Media;

/// <summary>
/// Базовый runtime-поток мультимедийных данных поверх <see cref="Atom.IO.Stream"/>.
/// </summary>
public abstract class MediaStream(MediaStreamType streamType) : Atom.IO.Stream
{
    private bool isDisposed;

    /// <summary>
    /// Тип мультимедийного потока.
    /// </summary>
    public MediaStreamType StreamType { get; } = streamType;

    /// <summary>
    /// Указывает, должен ли поток зацикливаться после достижения конца данных.
    /// </summary>
    public bool IsLooped { get; set; }

    /// <summary>
    /// Указывает, активен ли поток в данный момент.
    /// </summary>
    public bool IsActive { get; protected set; } = true;

    /// <summary>
    /// Длительность потока в микросекундах. Для потоков без фиксированной длительности возвращает <c>-1</c>.
    /// </summary>
    public virtual long DurationUs
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => -1;
    }

    /// <summary>
    /// Длительность потока в виде <see cref="TimeSpan"/>.
    /// </summary>
    public TimeSpan Duration
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => DurationUs >= 0 ? TimeSpan.FromMicroseconds(DurationUs) : TimeSpan.Zero;
    }

    /// <summary>
    /// Возвращает <see langword="true"/>, если поток поддерживает чтение.
    /// </summary>
    public override bool CanRead
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => true;
    }

    /// <summary>
    /// Возвращает <see langword="false"/>, так как базовый media stream не поддерживает запись.
    /// </summary>
    public override bool CanWrite
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => false;
    }

    /// <summary>
    /// Указывает, достигнут ли конец доступных данных.
    /// </summary>
    public abstract bool EndOfStream { get; }

    /// <summary>
    /// Сбрасывает позицию чтения потока.
    /// </summary>
    public virtual void Reset() => throw new NotSupportedException();

    /// <summary>
    /// Асинхронно сбрасывает позицию чтения потока.
    /// </summary>
    public virtual ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException(new NotSupportedException());
    }

    /// <summary>
    /// Запись в базовый media stream не поддерживается.
    /// </summary>
    public sealed override void Write(ReadOnlySpan<byte> buffer)
        => throw new NotSupportedException("MediaStream не поддерживает запись в базовой реализации.");

    /// <summary>
    /// Асинхронная запись в базовый media stream не поддерживается.
    /// </summary>
    public sealed override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => ValueTask.FromException(new NotSupportedException("MediaStream не поддерживает запись в базовой реализации."));

    /// <summary>
    /// Проверяет, что поток ещё не освобождён.
    /// </summary>
    protected void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), GetType().Name);

    /// <summary>
    /// Освобождает ресурсы потока.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Volatile.Write(ref isDisposed, value: true);
            IsActive = false;
        }

        base.Dispose(disposing: disposing);
    }
}