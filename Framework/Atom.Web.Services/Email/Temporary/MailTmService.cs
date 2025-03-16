/*using System.Runtime.CompilerServices;
using Atom.Net.Http;

namespace Atom.Web.Services.Email.Temporary;

/// <summary>
/// Представляет сервис временных почт <c>mail.tm</c>.
/// </summary>
public class MailTmService : ITemporaryEmailService
{
    private readonly HttpClientHandler handler;
    private readonly SafetyHttpClient client;
    private bool isDisposed;

    private static readonly Lazy<ITemporaryEmailFactory> factory = new(CreateFactory, true);

    /// <inheritdoc/>
    public IEnumerable<string> Domains { get; protected set; } = [];

    /// <summary>
    /// Фабрика временных почт.
    /// </summary>
    public static ITemporaryEmailFactory Factory => factory.Value;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="MailTmService"/>.
    /// </summary>
    public MailTmService()
    {
        handler = new HttpClientHandler();
        client = new SafetyHttpClient(handler) { BaseAddress = new Uri("https://api.mail.tm/") };
    }

    private static TemporaryEmailFactory CreateFactory()
    {
        var factory = new TemporaryEmailFactory();
        factory.Use<MailTmService>();
        return factory;
    }

    /// <summary>
    /// Освобождает неуправляемые ресурсы и выполняет другие задачи очистки.
    /// </summary>
    /// <param name="disposing">Указывает, вызывается ли метод из метода Dispose или из финализатора.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;
        client.Dispose();
        handler.Dispose();
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> GetDomainsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>
        {
            { "accept", "application/json" },
        };

        var items = await client.GetAsync(new Uri(client.BaseAddress!, "domains"), headers, JsonHttpContext.Default.JsonElement, cancellationToken).ConfigureAwait(false);

        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("isActive", out var isActiveProperty) && !isActiveProperty.GetBoolean()) continue;
            if (!item.TryGetProperty("domain", out var domainProperty)) continue;

            var domain = domainProperty.GetString();

            if (!string.IsNullOrEmpty(domain))
            {
                Domains = Domains.Concat([domain]).Distinct();
                yield return domain;
            }
        }

        yield break;
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<string> GetDomainsAsync() => GetDomainsAsync(CancellationToken.None);

    /// <inheritdoc/>
    public ValueTask<TemporaryEmailAccount?> CreateAccountAsync(string login, string password, CancellationToken cancellationToken) => throw new NotImplementedException();

    /// <inheritdoc/>
    public ValueTask<TemporaryEmailAccount?> CreateAccountAsync(string login, string password) => CreateAccountAsync(login, password, CancellationToken.None);

    /// <summary>
    /// Выполняет освобождение неуправляемых ресурсов и других задач очистки.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}*/