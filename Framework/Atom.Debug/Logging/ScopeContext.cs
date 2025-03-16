using System.Text;
using Atom.Buffers;

namespace Atom.Debug.Logging;

/// <summary>
/// Представляет контекст логирования.
/// </summary>
public sealed class ScopeContext : IDisposable
{
    /// <summary>
    /// Данные о состоянии контекста.
    /// </summary>
    public object? State { get; set; }

    internal int ThreadId { get; set; }

    internal int? TaskId { get; set; }

    /// <summary>
    /// Родительский контекст.
    /// </summary>
    public ScopeContext? Parent { get; set; }

    /// <summary>
    /// Происходит в момент высвобождения контекста.
    /// </summary>
    public event EventHandler<ScopeContextEventArgs>? Disposed;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ScopeContext"/>.
    /// </summary>
    /// <param name="state">Данные о состоянии контекста.</param>
    public ScopeContext(object state) => State = state;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ScopeContext"/>.
    /// </summary>
    public ScopeContext() { }

    /// <summary>
    /// Высвобождает контекст.
    /// </summary>
    public void Dispose()
    {
        Disposed?.Invoke(this, new ScopeContextEventArgs(this));
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Преобразует экземпляр в строковое представление.
    /// </summary>
    public override string? ToString() => ToString(this);

    private static string ToString(ScopeContext ctx)
    {
        var sb = ObjectPool<StringBuilder>.Shared.Rent();
        var current = ctx;

        while (current is not null)
        {
            if (current.State is not null)
            {
                var state = current.State.ToString();

                if (!string.IsNullOrEmpty(state))
                {
                    sb.Insert(0, state.Trim());
                    if (current.Parent is not null) sb.Insert(0, ' ');
                }
            }

            current = current.Parent;
        }

        var result = sb.ToString();
        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());

        return result;
    }
}