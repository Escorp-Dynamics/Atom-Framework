using Atom.Architect.Components;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Представляет базовую реализацию провайдера временной почты.
/// </summary>
[Component]
public abstract partial class TemporaryEmailProvider : ITemporaryEmailProvider
{
    private int disposeState;

    /// <summary>
    /// Инициализирует провайдер временной почты.
    /// </summary>
    protected TemporaryEmailProvider(ILogger? logger = null)
    {
        Logger = logger;
    }

    /// <inheritdoc/>
    public ILogger? Logger { get; set; }

    /// <inheritdoc/>
    public virtual IEnumerable<string> AvailableDomains => [];

    /// <inheritdoc/>
    public virtual bool CanCreate(TemporaryEmailAccountCreateSettings request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Domain))
        {
            return true;
        }

        var declaredDomains = AvailableDomains as string[] ?? [.. AvailableDomains];
        return declaredDomains.Length == 0 || declaredDomains.Contains(request.Domain, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public ValueTask<ITemporaryEmailAccount> CreateAccountAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        return CreateAccountCoreAsync(request, cancellationToken);
    }

    /// <summary>
    /// Создаёт новый временный почтовый аккаунт в конкретном upstream-провайдере.
    /// </summary>
    protected abstract ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(
        TemporaryEmailAccountCreateSettings request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Высвобождает управляемые ресурсы провайдера.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref disposeState, value: 1) != 0)
        {
            return;
        }
    }

    /// <summary>
    /// Высвобождает ресурсы провайдера.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Проверяет, что провайдер не был освобождён.
    /// </summary>
    protected void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposeState) != 0, GetType().Name);
}