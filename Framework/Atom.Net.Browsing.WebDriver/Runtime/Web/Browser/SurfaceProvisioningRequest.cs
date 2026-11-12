using System.Net;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Параметры окна или вкладки, которые движок применяет на уровне процесса и потому должны быть
/// известны транспорту в момент создания поверхности.
/// </summary>
/// <param name="Proxy">Действующий прокси поверхности.</param>
/// <param name="Device">Действующее устройство поверхности (часовой пояс, геолокация).</param>
internal sealed record SurfaceProvisioningRequest(IWebProxy? Proxy, Device? Device)
{
    /// <summary>Поверхность без собственных параметров процесса.</summary>
    public static SurfaceProvisioningRequest Default { get; } = new(Proxy: null, Device: null);
}
