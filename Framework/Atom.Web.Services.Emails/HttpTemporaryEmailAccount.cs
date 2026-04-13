using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Общая базовая реализация HTTP-аккаунта временной почты с ленивой Bearer-аутентификацией.
/// </summary>
public abstract class HttpTemporaryEmailAccount<TProvider> : TemporaryEmailAccount
    , IHttpTemporaryEmailMailOperations
    where TProvider : class, ITemporaryEmailProvider
{
    private readonly SemaphoreSlim authenticationGate = new(initialCount: 1, maxCount: 1);

    private string? accessToken;

    /// <summary>
    /// Инициализирует HTTP-аккаунт временной почты.
    /// </summary>
    protected HttpTemporaryEmailAccount(
        TProvider provider,
        Guid id,
        string address,
        string password,
        string? accessToken = null,
        ILogger? logger = null)
        : base(id, address, password, logger)
    {
        ArgumentNullException.ThrowIfNull(provider);

        HttpProvider = provider;
        Provider = provider;
        this.accessToken = accessToken;
    }

    /// <summary>
    /// Concrete HTTP-провайдер, создавший аккаунт.
    /// </summary>
    protected TProvider HttpProvider { get; }

    /// <summary>
    /// Текущий Bearer token, если он уже был получен.
    /// </summary>
    protected string? AccessToken
    {
        get => accessToken;
        set => accessToken = value;
    }

    /// <inheritdoc/>
    public override async ValueTask ConnectAsync(CancellationToken cancellationToken)
        => _ = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public override ValueTask DisconnectAsync(CancellationToken cancellationToken)
    {
        accessToken = null;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Обеспечивает наличие актуального access token.
    /// </summary>
    protected async ValueTask<string> EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            return accessToken;
        }

        await authenticationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                return accessToken;
            }

            accessToken = await AuthenticateAsync(cancellationToken).ConfigureAwait(false);
            return accessToken;
        }
        finally
        {
            authenticationGate.Release();
        }
    }

    /// <summary>
    /// Запрашивает новый access token у конкретного upstream-провайдера.
    /// </summary>
    protected abstract ValueTask<string> AuthenticateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Помечает upstream-письмо как прочитанное.
    /// </summary>
    public abstract ValueTask MarkUpstreamMailAsReadAsync(string upstreamMessageId, CancellationToken cancellationToken);

    /// <summary>
    /// Удаляет upstream-письмо.
    /// </summary>
    public abstract ValueTask DeleteUpstreamMailAsync(string upstreamMessageId, CancellationToken cancellationToken);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            authenticationGate.Dispose();
            accessToken = null;
        }

        base.Dispose(disposing);
    }
}