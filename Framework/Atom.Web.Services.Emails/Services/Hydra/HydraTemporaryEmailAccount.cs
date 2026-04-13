using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Общая базовая реализация Hydra-compatible временного почтового аккаунта.
/// </summary>
public abstract class HydraTemporaryEmailAccount<TProvider> : HttpTemporaryEmailAccount<TProvider>
    where TProvider : class, ITemporaryEmailProvider, IHydraTemporaryEmailProviderOperations
{
    /// <summary>
    /// Инициализирует Hydra-compatible временный почтовый аккаунт.
    /// </summary>
    protected HydraTemporaryEmailAccount(
        TProvider provider,
        Guid id,
        string address,
        string password,
        string? accessToken = null,
        ILogger? logger = null)
        : base(provider, id, address, password, accessToken, logger) { }

    /// <summary>
    /// Отображаемое имя upstream-провайдера для пользовательских сообщений.
    /// </summary>
    protected abstract string ProviderDisplayName { get; }

    /// <inheritdoc/>
    public override ValueTask SendAsync(IMail mail, CancellationToken cancellationToken)
        => ValueTask.FromException(new NotSupportedException($"{ProviderDisplayName} не поддерживает исходящую отправку через текущий провайдер."));

    /// <inheritdoc/>
    public override async ValueTask MarkUpstreamMailAsReadAsync(string upstreamMessageId, CancellationToken cancellationToken)
    {
        var accessToken = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        await HttpProvider.MarkAsReadAsync(accessToken, upstreamMessageId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async ValueTask DeleteUpstreamMailAsync(string upstreamMessageId, CancellationToken cancellationToken)
    {
        var accessToken = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        await HttpProvider.DeleteMailAsync(accessToken, upstreamMessageId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
    {
        var accessToken = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        return await HttpProvider.LoadMessagesAsync(this, accessToken, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override ValueTask<string> AuthenticateAsync(CancellationToken cancellationToken)
        => HttpProvider.AuthenticateAsync(Address, Password, cancellationToken);
}