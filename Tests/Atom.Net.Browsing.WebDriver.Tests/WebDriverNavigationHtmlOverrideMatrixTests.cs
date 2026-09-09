using System.Diagnostics;
using System.Text;
using Atom.Debug.Logging;

namespace Atom.Net.Browsing.WebDriver.Tests;

/// <summary>
/// Проверяет, что <see cref="NavigationSettings.Html"/> действительно подменяет документ main_frame
/// в РЕАЛЬНОМ браузере — по живому DOM, а не по кэшу драйвера.
/// </summary>
/// <remarks>
/// ЗАЧЕМ ОТДЕЛЬНЫЙ КЛАСС. Существующие real-browser тесты сверяют <c lang="text">WebPage.CurrentTitle</c>, а это
/// значение из транспорта драйвера: оно проставляется из подготовленного HTML ещё до того, как
/// браузер что-либо загрузил. Если подмена по дороге потерялась и вкладка открыла настоящий сайт,
/// такой тест всё равно зелёный. Здесь всё утверждается через живой опрос страницы
/// (<c lang="text">EvaluateAsync</c>/<c lang="text">GetContentAsync</c>), который ходит в браузер по мосту.
/// </remarks>
[NonParallelizable]
public sealed class WebDriverNavigationHtmlOverrideMatrixTests
{
    /// <summary>
    /// Адрес-цель: внешний HTTPS, поэтому запрос обязан пройти через локальный навигационный прокси
    /// (CONNECT + MITM) — тот же путь, что у боевого солвера. Домен example.com выбран намеренно:
    /// он существует, поэтому провал подмены даёт не сетевую ошибку, а чужой документ, то есть ровно
    /// тот отказ, который надо ловить.
    /// </summary>
    private static readonly Uri OverrideTargetUrl = new("https://example.com/atom-html-override-probe");

    /// <summary>
    /// Маркер в подменённом документе. Ищется в живом DOM: его наличие означает, что fulfill-решение
    /// доехало до вкладки, отсутствие — что браузер получил настоящий ответ origin.
    /// </summary>
    private const string OverrideMarker = "atom-html-override-marker";

    private static readonly TimeSpan LiveProbeTimeout = TimeSpan.FromSeconds(20);

    private static string BuildOverrideHtml(string probeId)
        => $$"""
            <!DOCTYPE html>
            <html>
              <head><meta charset="utf-8"><title>{{OverrideMarker}}</title></head>
              <body>
                <div id="{{OverrideMarker}}" data-probe="{{probeId}}">override-ok</div>
                <script>window.__atomOverrideProbe = "{{probeId}}";</script>
              </body>
            </html>
            """;

    /// <summary>
    /// Матрица User-Agent: по одному представителю на связку «движок × ОС», включая мобильные
    /// iOS (iPhone/iPad Safari, CriOS, EdgiOS, FxiOS). Chromium-UA прогоняются на chromium-браузерах,
    /// Gecko-UA — на Firefox: подмена UA поперёк движка сама по себе ломает согласованность
    /// отпечатка и увела бы диагностику в сторону.
    /// </summary>
    private static readonly (string Name, string UserAgent)[] ChromiumUserAgents =
    [
        ("chromium-windows", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"),
        ("chromium-macos", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"),
        ("chromium-linux", "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"),
        ("chromium-android", "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36"),
        ("ios-iphone-safari", "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1"),
        ("ios-ipad-safari", "Mozilla/5.0 (iPad; CPU OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1"),
        ("ios-iphone-crios", "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/126.0.6478.54 Mobile/15E148 Safari/604.1"),
        ("ios-iphone-edgios", "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 EdgiOS/126.2536.62 Mobile/15E148 Safari/604.1"),
        ("ios-ipad-fxios", "Mozilla/5.0 (iPad; CPU OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) FxiOS/127.0 Mobile/15E148 Safari/605.1.15"),
    ];

    private static readonly (string Name, string UserAgent)[] GeckoUserAgents =
    [
        ("gecko-windows", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0"),
        ("gecko-macos", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:133.0) Gecko/20100101 Firefox/133.0"),
        ("gecko-linux", "Mozilla/5.0 (X11; Linux x86_64; rv:133.0) Gecko/20100101 Firefox/133.0"),
        ("gecko-android", "Mozilla/5.0 (Android 13; Mobile; rv:133.0) Gecko/133.0 Firefox/133.0"),
        ("ios-ipad-fxios-gecko", "Mozilla/5.0 (iPad; CPU OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) FxiOS/127.0 Mobile/15E148 Safari/605.1.15"),
    ];

    /// <summary>
    /// Базовый репродьюсер: одна вкладка, UA по умолчанию. Падает ровно на том, на чём падает солвер.
    /// </summary>
    [Test]
    public async Task NavigationHtmlOverrideReplacesLiveDocumentOnRealBrowser()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Требуется ATOM_TEST_WEBDRIVER_BROWSER.");

        await using var browser = await WebDriverTestEnvironment.LaunchAsync(new WebBrowserSettings
        {
            Logger = new ConsoleLogger(nameof(WebDriverNavigationHtmlOverrideMatrixTests)),
        });

        var page = (WebPage)browser.CurrentPage;
        var outcome = await ProbeOverrideAsync(page, "baseline").ConfigureAwait(false);

        Assert.That(outcome.Applied, Is.True, outcome.Describe("baseline"));
    }

    /// <summary>
    /// Матрица UA внутри одного запуска браузера: каждая вкладка получает свой User-Agent и свою
    /// пробу. Так вся матрица UA проходит за один старт браузера, а не за N стартов.
    /// </summary>
    [Test]
    public async Task NavigationHtmlOverrideHoldsAcrossUserAgentMatrixOnRealBrowser()
    {
        if (!WebDriverTestEnvironment.IsRealBrowserRunConfigured())
            Assert.Ignore("Требуется ATOM_TEST_WEBDRIVER_BROWSER.");

        var browserName = Environment.GetEnvironmentVariable("ATOM_TEST_WEBDRIVER_BROWSER") ?? string.Empty;
        var isFirefox = browserName.Contains("firefox", StringComparison.OrdinalIgnoreCase);
        var userAgents = isFirefox ? GeckoUserAgents : ChromiumUserAgents;

        await using var browser = await WebDriverTestEnvironment.LaunchAsync(new WebBrowserSettings
        {
            Logger = new ConsoleLogger(nameof(WebDriverNavigationHtmlOverrideMatrixTests)),
        });

        var window = (WebWindow)browser.CurrentWindow;
        var failures = new List<string>();

        foreach (var (name, userAgent) in userAgents)
        {
            // Служебная флейка Chrome: при быстром open+reconfigure мостовая вкладка иногда
            // отваливается («сеанс-отключён») до применения контекста. Одна повторная попытка
            // на новой вкладке отличает транзиент от реального провала подмены.
            OverrideOutcome outcome;
            try
            {
                outcome = await RunProbeWithRetryAsync(window, name, userAgent).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failures.Add($"[{name}] исключение: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (!outcome.Applied)
                failures.Add(outcome.Describe(name));
        }

        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Одна проба матрицы: открытие вкладки, применение UA, проверка подмены. Внутри — до двух
    /// повторных попыток при транзиентных сбоях моста: перезапуск service worker ломает текущую
    /// команду («сеанс-отключён»), вкладка может не успеть перерегистрироваться («вкладка …
    /// не зарегистрирована»), а гонка закрытия вкладки даёт ObjectDisposedException. Всё это —
    /// известные транзиенты окружения, а не отказ подмены.
    /// </summary>
    private static async Task<OverrideOutcome> RunProbeWithRetryAsync(WebWindow window, string name, string userAgent)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await RunProbeAsync(window, name, userAgent).ConfigureAwait(false);
            }
            catch (Exception ex)
                when (attempt < maxAttempts && IsTransientBridgeFailure(ex))
            {
            }
        }
    }

    private static bool IsTransientBridgeFailure(Exception ex)
        => ex is ObjectDisposedException
            || (ex is InvalidOperationException invalidOperation
                && (invalidOperation.Message.Contains("сеанс-отключён", StringComparison.Ordinal)
                    || invalidOperation.Message.Contains("не зарегистрирована", StringComparison.Ordinal)));

    private static async Task<OverrideOutcome> RunProbeAsync(WebWindow window, string name, string userAgent)
    {
        var page = (WebPage)await window.OpenPageAsync().ConfigureAwait(false);
        try
        {
            await page.ReconfigureAsync(new WebPageSettings
            {
                Device = UserAgentProfileAlignment.CreateDevice(userAgent),
            }).ConfigureAwait(false);

            return await ProbeOverrideAsync(page, name).ConfigureAwait(false);
        }
        finally
        {
            await page.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Выполняет одну пробу подмены и снимает живое состояние вкладки.
    /// </summary>
    /// <remarks>
    /// <c lang="text">NavigateAsync</c> для мостового пути возвращается по подтверждению команды, а не по
    /// готовности документа (в логе вкладка на этот момент ещё <c lang="text">loading</c>). Поэтому результат
    /// снимается поллингом живого DOM до появления маркера либо до таймаута — иначе проба меряла бы
    /// предыдущую страницу вкладки и давала ложный отказ.
    /// </remarks>
    private static async Task<OverrideOutcome> ProbeOverrideAsync(WebPage page, string probeId)
    {
        // Перехват обязателен: без него вкладка не получает маршрут навигационного прокси и подменять
        // ответ нечем (WebBrowser.ResolveBridgeNavigationInterceptionMode).
        await page.SetRequestInterceptionAsync(enabled: true, urlPatterns: ["https://**/*"]).ConfigureAwait(false);

        using var probeCancellation = new CancellationTokenSource(LiveProbeTimeout);
        var stopwatch = Stopwatch.StartNew();

        await page.NavigateAsync(
            OverrideTargetUrl,
            new NavigationSettings { Html = BuildOverrideHtml(probeId) },
            probeCancellation.Token).ConfigureAwait(false);

        string? liveProbeId = null;
        string? liveHtml = null;
        Uri? liveUrl = null;
        string? liveTitle = null;

        while (!probeCancellation.IsCancellationRequested)
        {
            liveUrl = await SafeAsync(() => page.GetUrlAsync(probeCancellation.Token)).ConfigureAwait(false);
            liveProbeId = await SafeAsync(() => page.EvaluateAsync<string>(
                "window.__atomOverrideProbe || ''", probeCancellation.Token)).ConfigureAwait(false);
            liveHtml = await SafeAsync(() => page.EvaluateAsync<string>(
                "document.documentElement ? document.documentElement.outerHTML : ''", probeCancellation.Token)).ConfigureAwait(false);

            var reachedTarget = liveUrl is not null
                && string.Equals(liveUrl.AbsoluteUri, OverrideTargetUrl.AbsoluteUri, StringComparison.Ordinal);
            var markerVisible = liveHtml?.Contains(OverrideMarker, StringComparison.Ordinal) == true;

            // Ранний выход — только когда картина уже устоялась: маркер найден либо вкладка доехала
            // до целевого адреса с чужим непустым документом (это и есть отказ, ждать больше нечего).
            if (markerVisible || (reachedTarget && !string.IsNullOrEmpty(liveHtml)))
                break;

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), probeCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        liveTitle = await SafeAsync(() => page.GetTitleAsync(CancellationToken.None)).ConfigureAwait(false);
        stopwatch.Stop();

        return new OverrideOutcome
        {
            CachedTitle = page.CurrentTitle,
            LiveUrl = liveUrl?.AbsoluteUri,
            LiveTitle = liveTitle,
            LiveProbeId = liveProbeId,
            ContentHasMarker = liveHtml?.Contains(OverrideMarker, StringComparison.Ordinal) == true,
            ContentPreview = TrimPreview(liveHtml),
            Elapsed = stopwatch.Elapsed,
            ExpectedProbeId = probeId,
        };
    }

    /// <summary>
    /// Живой опрос вкладки не должен ронять пробу: сбой опроса — тоже результат, его надо показать
    /// в отчёте, а не потерять под исключением.
    /// </summary>
    private static async Task<T?> SafeAsync<T>(Func<ValueTask<T?>> probe)
    {
        try
        {
            return await probe().ConfigureAwait(false);
        }
        catch (Exception)
        {
            return default;
        }
    }

    private static string TrimPreview(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return "<пусто>";

        var normalized = content.Replace('\n', ' ').Replace('\r', ' ');
        return normalized.Length <= 220 ? normalized : normalized[..220] + "…";
    }

    private sealed class OverrideOutcome
    {
        public required string? CachedTitle { get; init; }

        public required string? LiveUrl { get; init; }

        public required string? LiveTitle { get; init; }

        public required string? LiveProbeId { get; init; }

        public required bool ContentHasMarker { get; init; }

        public required string ContentPreview { get; init; }

        public required TimeSpan Elapsed { get; init; }

        public required string ExpectedProbeId { get; init; }

        /// <summary>
        /// Подмена засчитывается только по живым признакам из браузера: маркер в DOM и наш
        /// probe id в window. Кэш драйвера намеренно не участвует в критерии.
        /// </summary>
        public bool Applied
            => ContentHasMarker
                && string.Equals(LiveProbeId, ExpectedProbeId, StringComparison.Ordinal);

        public string Describe(string caseName)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"[{caseName}] подмена NavigationSettings.Html не доехала до живого документа.");
            builder.AppendLine($"  живой title: {LiveTitle ?? "<null>"}");
            builder.AppendLine($"  живой url: {LiveUrl ?? "<null>"}");
            builder.AppendLine($"  window.__atomOverrideProbe: '{LiveProbeId}' (ожидался '{ExpectedProbeId}')");
            builder.AppendLine($"  маркер в DOM: {ContentHasMarker}");
            builder.AppendLine($"  кэш драйвера CurrentTitle: {CachedTitle ?? "<null>"}");
            builder.AppendLine($"  время пробы: {Elapsed.TotalSeconds:F1}с");
            builder.AppendLine($"  фрагмент документа: {ContentPreview}");
            return builder.ToString();
        }
    }
}
