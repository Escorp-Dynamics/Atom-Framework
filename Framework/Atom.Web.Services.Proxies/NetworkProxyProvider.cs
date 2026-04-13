using Microsoft.Extensions.Logging;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет базовую реализацию сетевых провайдеров прокси.
/// </summary>
public abstract class NetworkProxyProvider : ProxyProvider
{
	private readonly Queue<DateTime> requestStarts = new();
	private readonly SemaphoreSlim requestRateGate = new(initialCount: 1, maxCount: 1);

	/// <summary>
	/// Инициализирует сетевой provider surface.
	/// </summary>
	protected NetworkProxyProvider(ILogger? logger = null)
		: base(logger)
	{
	}

	/// <summary>
	/// Максимальное количество сетевых запросов, стартующих за одну секунду в рамках одного provider instance.
	/// </summary>
	public int RequestsPerSecondLimit { get; set; } = 1;

	/// <inheritdoc/>
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			requestRateGate.Dispose();
		}

		base.Dispose(disposing);
	}

	/// <summary>
	/// Выполняет сетевую операцию с provider-level ограничением по числу стартов запросов в секунду.
	/// </summary>
	protected async ValueTask<TResult> RunRateLimitedAsync<TResult>(Func<CancellationToken, ValueTask<TResult>> callback, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(callback);

		while (true)
		{
			var delay = TimeSpan.Zero;

			await requestRateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				var now = DateTime.UtcNow;
				TrimExpiredRequests(now);

				var limit = Math.Max(1, RequestsPerSecondLimit);
				if (requestStarts.Count < limit)
				{
					requestStarts.Enqueue(now);
					break;
				}

				delay = requestStarts.Peek().AddSeconds(1) - now;
				if (delay < TimeSpan.Zero)
				{
					delay = TimeSpan.Zero;
				}
			}
			finally
			{
				requestRateGate.Release();
			}

			if (delay > TimeSpan.Zero)
			{
				await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
			}
		}

		return await callback(cancellationToken).ConfigureAwait(false);
	}

	private void TrimExpiredRequests(DateTime now)
	{
		while (requestStarts.Count > 0 && now - requestStarts.Peek() >= TimeSpan.FromSeconds(1))
		{
			requestStarts.Dequeue();
		}
	}
}