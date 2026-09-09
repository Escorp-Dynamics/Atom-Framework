using System.Drawing;
using System.Runtime.CompilerServices;
using Atom.Hardware.Input;
using Atom.Media.Audio;
using Atom.Media.Video;
using Atom.Net.Https;

namespace Atom.Net.Browsing.WebDriver;

public sealed partial class WebWindow
{
    public ValueTask ActivateAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return ActivateAsync((WebPage)CurrentPage, cancellationToken);
    }

    /// <summary>
    /// Выводит на передний план окно и КОНКРЕТНУЮ вкладку <paramref name="page"/>.
    /// </summary>
    /// <remarks>
    /// Активировать нужно именно запрошенную страницу, а не <see cref="CurrentPage"/>: поле
    /// currentPage обновляется только при ОТКРЫТИИ вкладки (PublishOpenedPage) и при её закрытии,
    /// но НЕ при активации. Поэтому в окне с несколькими вкладками currentPage навсегда указывает на
    /// последнюю ОТКРЫТУЮ, и активация «по currentPage» уводила на передний план ЧУЖУЮ вкладку:
    /// запрошенная так и оставалась фоновой (её rAF заморожен → Cloudflare не монтирует
    /// challenge-iframe), а координатный клик, летящий по абсолютным экранным координатам, попадал
    /// в чужую вкладку. Внешне выглядело как «виджет отрисован, чекбокс на месте, но клика нет» и
    /// уходило в таймаут. После активации синхронизируем currentPage с фактически выбранной вкладкой.
    /// </remarks>
    internal async ValueTask ActivateAsync(WebPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        // Целевая вкладка адресуется РЕАЛЬНЫМ идентификатором вкладки браузера (BoundBridgeTabId).
        // page.TabId — это внутренний GUID драйвера; расширение разбирает идентификатор как ЧИСЛО,
        // поэтому GUID туда отправлять нельзя. Пока цель не привязана к мостовой вкладке, активировать
        // адресно нечем — тогда ограничиваемся активацией окна и локальным учётом.
        var targetTabId = page.BoundBridgeTabId;

        if (OwnerBrowser.GetBridgeCommandPage() is { } bridgePage
            && bridgePage.BridgeCommands is { } bridge)
        {
            await bridge.ActivateWindowAsync(EffectiveWindowId, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(targetTabId))
                await bridge.ActivateTabAsync(targetTabId, cancellationToken).ConfigureAwait(false);
        }

        SetCurrentPage(page);
        OwnerBrowser.ActivateWindow(this);
    }

    /// <summary>
    /// Готовит вкладку к доверенному вводу: активирует окно и дожидается, пока браузер признает
    /// её документ сфокусированным.
    /// </summary>
    /// <remarks>
    /// Ожидание намеренно живёт здесь, а не в <see cref="ActivateAsync(CancellationToken)"/>:
    /// активация нужна и путям, которым ввод не требуется (например, снимку экрана), и заставлять
    /// их ждать фокуса — чистые потери. Платит только тот, кто собирается нажимать.
    /// </remarks>
    internal async ValueTask PrepareForTrustedInputAsync(WebPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        // Активируем ИМЕННО ту вкладку, в которую собираемся вводить (не CurrentPage — см. ActivateAsync):
        // иначе на передний план выходила чужая вкладка, ожидание фокуса не подтверждалось, а клик по
        // экранным координатам уходил в неё же.
        await ActivateAsync(page, cancellationToken).ConfigureAwait(false);
        await WaitForDocumentFocusAsync(page, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Захватывает ЭКСКЛЮЗИВНЫЙ доступ к доверенному вводу дисплея и готовит к нему вкладку
    /// (активация + подтверждение фокуса). Освобождается через <see cref="IAsyncDisposable"/>.
    /// </summary>
    /// <remarks>
    /// ЗАЧЕМ. Доверенный ввод дисплея глобален: активна ровно одна вкладка, а XTEST-клик уходит по
    /// АБСОЛЮТНЫМ экранным координатам — то есть в ту вкладку, что сейчас на переднем плане.
    /// Без взаимного исключения последовательность «активировать свою вкладку → дождаться фокуса →
    /// посчитать координаты → кликнуть» не атомарна: параллельный солв в СОСЕДНЕЙ вкладке того же
    /// окна успевает вклиниться со своей активацией между подготовкой и кликом, забирает передний
    /// план — и клик прилетает в ЧУЖУЮ вкладку. Своя вкладка при этом визуально в порядке (виджет
    /// отрисован, чекбокс на месте), но нажатия не получает → уход в таймаут. Гонка узкая, поэтому
    /// проявлялась редко и «случайно».
    /// Ключ шлюза — <see cref="VirtualMouse"/>: он один на браузер/дисплей, т.е. ровно тот общий
    /// ресурс, за который идёт борьба (у окон на разных дисплеях шлюзы разные и не мешают друг другу).
    /// Секция короткая (активация + фокус + один клик), поэтому параллельность вкладок сохраняется:
    /// ждущая вкладка всё равно не могла бы кликнуть, пока не на переднем плане.
    /// </remarks>
    /// <summary>
    /// Выводит вкладку на передний план ЭКСКЛЮЗИВНО и удерживает его <paramref name="hold"/>,
    /// после чего отпускает — давая соседним вкладкам того же дисплея честную очередь.
    /// </summary>
    /// <remarks>
    /// ЗАЧЕМ. На дисплее передний план ровно один, и вкладки, работающие параллельно, отбирают его
    /// друг у друга. Без координации одна вкладка может НИ РАЗУ не получить передний план за весь
    /// свой бюджет: её кадры (rAF) не идут, поэтому Cloudflare не монтирует challenge-iframe, и
    /// задача уходит в таймаут, хотя вторая вкладка в это время работает нормально.
    /// Короткое ЭКСКЛЮЗИВНОЕ удержание решает это без потери параллельности: вкладке хватает
    /// нескольких кадров, чтобы отрисовать виджет и смонтировать iframe, после чего очередь честно
    /// переходит к соседке. Это НЕ «удержание на весь солв» (такое лишь морило бы соседку голодом) —
    /// удержание ограничено и делит тот же шлюз, что и клик, поэтому активация и клик больше не
    /// перебивают друг друга.
    /// </remarks>
    internal async ValueTask ActivateExclusiveAsync(WebPage page, TimeSpan hold, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        var mouse = await ResolveMouseAsync(cancellationToken).ConfigureAwait(false);
        var gate = TrustedInputGates.GetValue(mouse, static _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ActivateAsync(page, cancellationToken).ConfigureAwait(false);

            // Ждём ФАКТ фокуса, а не отмеренный срок: активация доходит до содержимого за разное
            // время, и фиксированная пауза либо отпускала шлюз до того, как вкладка реально вышла
            // вперёд (тогда виджет не монтировался и клика не было вовсе), либо держала его зря.
            // WaitForDocumentFocusAsync возвращается сразу, как только документ сфокусирован.
            if (hold > TimeSpan.Zero)
                await WaitForDocumentFocusAsync(page, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = gate.Release();
        }
    }

    internal async ValueTask<TrustedInputScope> AcquireTrustedInputScopeAsync(WebPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        var mouse = await ResolveMouseAsync(cancellationToken).ConfigureAwait(false);
        var gate = TrustedInputGates.GetValue(mouse, static _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PrepareForTrustedInputAsync(page, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _ = gate.Release();
            throw;
        }

        return new TrustedInputScope(gate);
    }

    /// <summary>
    /// Шлюзы доверенного ввода по дисплеям: ключ — <see cref="VirtualMouse"/> (один на браузер/дисплей).
    /// <see cref="ConditionalWeakTable{TKey,TValue}"/> — чтобы шлюз жил ровно столько же, сколько мышь,
    /// и исчезал вместе с закрытым браузером (без утечки на долгоживущих пулах).
    /// </summary>
    private static readonly ConditionalWeakTable<VirtualMouse, SemaphoreSlim> TrustedInputGates = new();

    /// <summary>
    /// Область эксклюзивного доверенного ввода: освобождает шлюз дисплея при уничтожении.
    /// </summary>
    internal readonly struct TrustedInputScope(SemaphoreSlim gate) : IDisposable
    {
        private readonly SemaphoreSlim gate = gate;

        public void Dispose() => _ = gate?.Release();
    }

    // Активация окна и вкладки доходит до содержимого асинхронно, и до её завершения браузер
    // не считает документ сфокусированным. Firefox в этом состоянии отбрасывает ВЕСЬ настоящий
    // пользовательский ввод во вкладке — и указатель, и клавиатуру, — пропуская наружу только
    // собственные синтезированные движения; они дают :hover и события пересечения границ, из-за
    // чего вкладка выглядит отзывчивой, хотя ни одно нажатие до неё не доходит. Прежней
    // фиксированной паузы в 75 мс не хватало: на виртуальном дисплее активация занимает до
    // 2,7 с в Firefox (в Chromium — единицы миллисекунд), поэтому доверенный ввод регулярно
    // уходил в пустоту. Ждём фактической готовности, а не наугад отмеренного срока.
    private async ValueTask WaitForDocumentFocusAsync(WebPage page, CancellationToken cancellationToken)
    {
        if (IsDisposed || page.IsDisposed)
            return;

        // Полный бюджет платим один раз на страницу. Если фокус подтвердить не удалось (окно
        // намеренно фоновое, либо у окружения вовсе нет понятия активного окна), выжидать его
        // целиком перед каждым нажатием — чистые потери на горячем пути; дальше хватит короткой
        // перепроверки, которая сама снимет пометку, как только фокус всё-таки появится.
        //
        // ★ Пометка НЕ переживает границу задачи. Вкладка переиспользуется, поэтому один сбой
        // фокуса урезал бюджет всем последующим задачам на ней: они не успевали дождаться
        // переднего плана, challenge-iframe не монтировался и клика не было вовсе. Замер показал
        // ровно это — у заражённых задач в стадиях страницы нет ни одного 'focus', а провал после
        // провала повторялся в 67% случаев против 8-13% после успеха. Снятие пометки при смене
        // контекста вкладки (см. ResetDocumentFocusTracking) возвращает каждой задаче полный бюджет.
        var budget = ReferenceEquals(documentFocusUnconfirmedPage, page)
            ? DocumentFocusRecheckBudget
            : DocumentFocusActivationBudget;

        var elapsed = System.Diagnostics.Stopwatch.StartNew();

        while (elapsed.Elapsed < budget)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsDisposed || page.IsDisposed)
                return;

            try
            {
                if (await page.EvaluateAsync<bool>("document.hasFocus()", cancellationToken).ConfigureAwait(false))
                {
                    documentFocusUnconfirmedPage = null;
                    OwnerBrowser.LaunchSettings.Logger?.LogWebWindowDocumentFocusConfirmed(WindowId, page.TabId, elapsed.ElapsedMilliseconds);
                    return;
                }
            }
            catch (Protocol.BridgeCommandException exception) when (IsExpectedDuringActivation(exception))
            {
                // Вкладка пересоздаёт мост (навигация или перезагрузка) либо не успела ответить —
                // и то и другое ожидаемо, пока активация ещё идёт. Пробуем снова в пределах бюджета.
            }

            await Task.Delay(DocumentFocusPollInterval, cancellationToken).ConfigureAwait(false);
        }

        documentFocusUnconfirmedPage = page;
        OwnerBrowser.LaunchSettings.Logger?.LogWebWindowDocumentFocusNotConfirmed(WindowId, page.TabId, elapsed.ElapsedMilliseconds);
    }

    private static bool IsExpectedDuringActivation(Protocol.BridgeCommandException exception)
        => Protocol.BridgeCommandException.IsSurfaceDisconnect(exception)
            || exception.Status is Protocol.BridgeStatus.Timeout;

    private WebPage? documentFocusUnconfirmedPage;

    /// <summary>
    /// Снимает пометку «фокус не подтверждён» для страницы: следующая задача на той же вкладке
    /// должна получить полный бюджет ожидания, а не урезанный из-за чужого сбоя.
    /// </summary>
    internal void ResetDocumentFocusTracking(WebPage page)
    {
        if (ReferenceEquals(documentFocusUnconfirmedPage, page))
            documentFocusUnconfirmedPage = null;
    }

    private static readonly TimeSpan DocumentFocusActivationBudget = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan DocumentFocusRecheckBudget = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan DocumentFocusPollInterval = TimeSpan.FromMilliseconds(50);

    public ValueTask ActivateAsync()
        => ActivateAsync(CancellationToken.None);

    public async ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (OwnerBrowser.GetBridgeCommandPage(this) is { } bridgePage
                && bridgePage.BridgeCommands is { } bridge)
            {
                try
                {
                    await bridge.CloseWindowAsync(EffectiveWindowId, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException exception) when (ReferenceEquals(bridgePage.OwnerWindow, this)
                    && Protocol.BridgeCommandException.IsSurfaceDisconnect(exception))
                {
                    // Закрытие единственного мост-связанного окна может отключить отправителя до
                    // прихода ответа. Классификация — по типизированному исключению, а не по
                    // подстроке локализованного сообщения.
                }
            }
        }
        finally
        {
            // DisposeAsync выполняется всегда: незакрытое окно с его страницами иначе утекло бы,
            // если CloseWindow бросит непредвиденное (нетипизированное) исключение.
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    public ValueTask CloseAsync()
        => CloseAsync(CancellationToken.None);

    public async ValueTask ClearAllCookiesAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        WebPage[] pagesSnapshot;
        lock (pageGate)
        {
            ThrowIfDisposed();
            pagesSnapshot = pages.Where(static page => !page.IsDisposed).ToArray();
        }

        OwnerBrowser.LaunchSettings.Logger?.LogWebWindowCookiesClearing(WindowId, pagesSnapshot.Length);

        foreach (var page in pagesSnapshot)
        {
            await page.ClearAllCookiesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask ClearAllCookiesAsync()
        => ClearAllCookiesAsync(CancellationToken.None);

    public async ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        WebPage[] pagesSnapshot;
        lock (pageGate)
        {
            ThrowIfDisposed();
            requestInterceptionState = RequestInterceptionState.Create(enabled, urlPatterns);
            pagesSnapshot = pages.Where(static page => !page.IsDisposed).ToArray();
        }

        foreach (var page in pagesSnapshot)
        {
            await page.ApplyEffectiveRequestInterceptionAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns)
        => SetRequestInterceptionAsync(enabled, urlPatterns, CancellationToken.None);

    public ValueTask SetRequestInterceptionAsync(bool enabled, CancellationToken cancellationToken)
        => SetRequestInterceptionAsync(enabled, urlPatterns: null, cancellationToken);

    public ValueTask SetRequestInterceptionAsync(bool enabled)
        => SetRequestInterceptionAsync(enabled, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings(), cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url)
        => NavigateAsync(url, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationKind kind, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings { Kind = kind }, cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationKind kind)
        => NavigateAsync(url, kind, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings { Headers = headers }, cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, IReadOnlyDictionary<string, string> headers)
        => NavigateAsync(url, headers, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings { Body = body }, cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, ReadOnlyMemory<byte> body)
        => NavigateAsync(url, body, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, string html, CancellationToken cancellationToken)
        => NavigateAsync(url, new NavigationSettings { Html = html }, cancellationToken);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, string html)
        => NavigateAsync(url, html, CancellationToken.None);

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfDisposed();
        var currentPage = (WebPage)CurrentPage;
        OwnerBrowser.LaunchSettings.Logger?.LogWebWindowNavigationStarting(WindowId, currentPage.TabId, url.ToString(), settings.Kind.ToString());
        return currentPage.NavigateAsync(url, settings, cancellationToken);
    }

    public ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationSettings settings)
        => NavigateAsync(url, settings, CancellationToken.None);

    public async ValueTask<HttpsResponseMessage> ReloadAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var currentPage = (WebPage)CurrentPage;
        var reloadUrl = await currentPage.GetUrlAsync(cancellationToken).ConfigureAwait(false) ?? currentPage.CurrentUrl ?? new Uri("about:blank");
        OwnerBrowser.LaunchSettings.Logger?.LogWebWindowReloadStarting(WindowId, currentPage.TabId, reloadUrl.ToString());
        return await currentPage.ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<HttpsResponseMessage> ReloadAsync()
        => ReloadAsync(CancellationToken.None);

    public ValueTask AttachVirtualCameraAsync(VirtualCamera camera, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ThrowIfDisposed();
        return CurrentPage.AttachVirtualCameraAsync(camera, cancellationToken);
    }

    public ValueTask AttachVirtualCameraAsync(VirtualCamera camera)
        => AttachVirtualCameraAsync(camera, CancellationToken.None);

    public ValueTask AttachVirtualMicrophoneAsync(VirtualMicrophone microphone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(microphone);
        ThrowIfDisposed();
        return CurrentPage.AttachVirtualMicrophoneAsync(microphone, cancellationToken);
    }

    public ValueTask AttachVirtualMicrophoneAsync(VirtualMicrophone microphone)
        => AttachVirtualMicrophoneAsync(microphone, CancellationToken.None);

    public ValueTask<Uri?> GetUrlAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return CurrentPage.GetUrlAsync(cancellationToken);
    }

    public ValueTask<Uri?> GetUrlAsync()
        => GetUrlAsync(CancellationToken.None);

    public ValueTask<string?> GetTitleAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return CurrentPage.GetTitleAsync(cancellationToken);
    }

    public ValueTask<string?> GetTitleAsync()
        => GetTitleAsync(CancellationToken.None);

    public async ValueTask<Rectangle?> GetBoundingBoxAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (CurrentPage is WebPage page
            && page.BridgeCommands is { } bridge)
        {
            var bridgeBounds = await bridge.GetWindowBoundsAsync(cancellationToken).ConfigureAwait(false);
            if (OperatingSystem.IsLinux()
                && OwnerBrowser.TryGetLinuxNativeWindowBounds(bridgeBounds.Size, page.CurrentTitle) is Rectangle nativeBounds)
            {
                return nativeBounds;
            }

            return bridgeBounds;
        }

        return new Rectangle(ResolvedWindowPosition, ResolvedWindowSize);
    }

    public ValueTask<Rectangle?> GetBoundingBoxAsync()
        => GetBoundingBoxAsync(CancellationToken.None);
}