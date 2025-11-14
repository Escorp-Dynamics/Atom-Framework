using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Atom.Buffers;

namespace Atom;

/// <summary>
/// Представляет базовые аргументы событий.
/// </summary>
public class MutableEventArgs : EventArgs, IPooled
{
    private readonly Stopwatch timer = new();

    /// <summary>
    /// Номер текущей итерации.
    /// </summary>
    public uint Iteration { get; set; }

    /// <summary>
    /// Определяет, было ли событие отменено другим обработчиком.
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// Длительность события.
    /// </summary>
    public TimeSpan Duration => timer.Elapsed;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="MutableEventArgs"/>.
    /// </summary>
    public MutableEventArgs()
    {
        Iteration = 1;
        timer.Start();
    }

    /// <summary>
    /// Происходит в момент возврата экземпляра в пул.
    /// </summary>
    public virtual void Reset()
    {
        timer.Reset();
        Iteration = 1;
        IsCancelled = default;
    }

    /// <summary>
    /// Сбрасывает таймер длительности события.
    /// </summary>
    /// <param name="isIncrementIteration">Следует ли увеличить счётчик итераций.</param>
    public void Restart(bool isIncrementIteration)
    {
        if (isIncrementIteration) ++Iteration;
        timer.Restart();
    }

    /// <summary>
    /// Сбрасывает таймер длительности события и увеличивает счётчик итераций.
    /// </summary>
    public void Restart() => Restart(isIncrementIteration: true);

    /// <summary>
    /// Приостанавливает таймер длительности события.
    /// </summary>
    public void Pause() => timer.Stop();

    /// <summary>
    /// Возобновляет таймер длительности события.
    /// </summary>
    public void Resume() => timer.Start();

    /// <inheritdoc/>
    public static T Rent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>() where T : IPooled => ObjectPool<T>.Shared.Rent();

    /// <summary>
    /// Арендует экземпляр аргументов события в пуле объектов.
    /// </summary>
    public static MutableEventArgs Rent() => Rent<MutableEventArgs>();

    /// <inheritdoc/>
    public static void Return<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(T value) where T : IPooled => ObjectPool<T>.Shared.Return(value, x => x.Reset());
}