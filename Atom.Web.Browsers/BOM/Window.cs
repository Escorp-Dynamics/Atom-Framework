using System.Drawing;

namespace Atom.Web.Browsers.BOM;

/// <summary>
/// Представляет окно браузера.
/// </summary>
public class Window : IWindow
{
    /// <inheritdoc/>
    public ulong Id { get; init; }

    /// <inheritdoc/>
    public Size Size { get; set; }

    /// <inheritdoc/>
    public Point Position { get; set; }

    /// <inheritdoc/>
    public ValueTask<IPage> OpenPageAsync(PageSettings settings, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public ValueTask<IPage> OpenPageAsync(PageSettings settings) => OpenPageAsync(settings, CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask; // TODO: реализовать.
    }

    /// <inheritdoc/>
    public ValueTask CloseAsync() => CloseAsync(CancellationToken.None);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}