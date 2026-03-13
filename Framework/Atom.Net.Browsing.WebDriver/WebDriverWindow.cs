using System.Collections.Concurrent;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет окно браузера, управляемое через WebSocket-мост.
/// </summary>
/// <remarks>
/// Окно группирует вкладки (<see cref="WebDriverPage"/>), принадлежащие
/// одному окну браузера. Каждая вкладка при этом остаётся полностью изолированной
/// на уровне канала связи.
/// </remarks>
public sealed class WebDriverWindow : IWebWindow
{
    private readonly ConcurrentDictionary<string, WebDriverPage> pages = new(StringComparer.Ordinal);
    private string? currentPageId;
    private bool isDisposed;

    /// <summary>
    /// Идентификатор окна.
    /// </summary>
    public string WindowId { get; }

    /// <inheritdoc/>
    public IEnumerable<IWebPage> Pages => pages.Values;

    /// <inheritdoc/>
#pragma warning disable CA1065 // Состояние «нет вкладок» — исключительная ситуация.
    public IWebPage CurrentPage
    {
        get
        {
            if (currentPageId is not null && pages.TryGetValue(currentPageId, out var page))
                return page;

            return pages.Values.FirstOrDefault()
                ?? throw new BridgeException("В окне нет открытых вкладок.");
        }
    }
#pragma warning restore CA1065

    /// <summary>
    /// Количество открытых вкладок в окне.
    /// </summary>
    public int PageCount => pages.Count;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebDriverWindow"/>.
    /// </summary>
    /// <param name="windowId">Идентификатор окна.</param>
    internal WebDriverWindow(string windowId) => WindowId = windowId;

    /// <summary>
    /// Добавляет вкладку в окно.
    /// </summary>
    /// <param name="page">Вкладка.</param>
    internal void AddPage(WebDriverPage page)
    {
        pages[page.TabId] = page;
        currentPageId ??= page.TabId;
    }

    /// <summary>
    /// Удаляет вкладку из окна.
    /// </summary>
    /// <param name="tabId">Идентификатор вкладки.</param>
    internal bool RemovePage(string tabId)
    {
        var removed = pages.TryRemove(tabId, out _);

        if (removed && string.Equals(currentPageId, tabId, StringComparison.Ordinal))
            currentPageId = pages.Keys.FirstOrDefault();

        return removed;
    }

    /// <summary>
    /// Возвращает вкладку по идентификатору.
    /// </summary>
    /// <param name="tabId">Идентификатор вкладки.</param>
    public WebDriverPage? GetPage(string tabId) => pages.GetValueOrDefault(tabId);

    /// <summary>
    /// Устанавливает активную вкладку окна.
    /// </summary>
    /// <param name="tabId">Идентификатор вкладки.</param>
    public void SetCurrentPage(string tabId)
    {
        if (!pages.ContainsKey(tabId))
            throw new BridgeException($"Вкладка '{tabId}' не принадлежит данному окну.");

        currentPageId = tabId;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        foreach (var page in pages.Values)
            await page.DisposeAsync().ConfigureAwait(false);

        pages.Clear();
    }
}
