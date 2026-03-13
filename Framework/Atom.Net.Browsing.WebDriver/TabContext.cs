using System.Diagnostics;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Изолированный контекст браузера — отдельный процесс с собственным профилем.
/// </summary>
/// <remarks>
/// <para>
/// Контекст обеспечивает полную изоляцию на уровне ОС: cookies, localStorage,
/// кэш, прокси и сетевой стек не пересекаются с другими контекстами
/// или основным браузером.
/// </para>
/// <para>
/// При освобождении завершает процесс браузера и удаляет временный профиль.
/// </para>
/// </remarks>
public sealed class TabContext : IAsyncDisposable
{
    private readonly Process process;
    private readonly string profileDir;
    private bool isDisposed;

    /// <summary>
    /// Страница (вкладка) изолированного контекста.
    /// </summary>
    public WebDriverPage Page { get; }

    /// <summary>
    /// Настройки, с которыми был создан контекст.
    /// </summary>
    public TabContextSettings Settings { get; }

    internal TabContext(Process process, string profileDir, WebDriverPage page, TabContextSettings settings)
    {
        this.process = process;
        this.profileDir = profileDir;
        Page = page;
        Settings = settings;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Процесс мог уже завершиться.
            }
            catch (TimeoutException)
            {
                // Процесс не завершился за 5 с после Kill — продолжаем очистку.
            }
            finally
            {
                process.Dispose();
            }
        }
        else
        {
            process.Dispose();
        }

        try { Directory.Delete(profileDir, recursive: true); }
        catch (IOException) { /* Не критично — ОС удалит при перезагрузке. */ }
    }
}
