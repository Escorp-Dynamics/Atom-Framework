namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Транспорт, который сам создаёт окна и вкладки с учётом их параметров процесса, а не через команду
/// уже открытой вкладки.
/// </summary>
/// <remarks>
/// Нужен движкам, у которых прокси, часовой пояс и геолокация задаются процессу целиком: окно или
/// вкладка с другими параметрами размещается в отдельном процессе, и выбрать его может только транспорт.
/// </remarks>
internal interface ISurfaceProvisioningTransport
{
    /// <summary>Создаёт окно с первой вкладкой.</summary>
    ValueTask<(string TabId, string? WindowId)> OpenWindowAsync(SurfaceProvisioningRequest request, CancellationToken cancellationToken);

    /// <summary>Создаёт вкладку в окне <paramref name="windowId"/>.</summary>
    ValueTask<(string TabId, string? WindowId)> OpenTabAsync(string windowId, SurfaceProvisioningRequest request, CancellationToken cancellationToken);
}
