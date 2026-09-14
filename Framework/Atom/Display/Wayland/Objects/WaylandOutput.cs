using System.Runtime.Versioning;

using Atom.Display.Wayland.Protocol;

namespace Atom.Display.Wayland.Objects;

/// <summary>
/// Объект <c lang="text">wl_output</c> — экран, на котором живут окна.
/// </summary>
/// <remarks>
/// Параметры выхода наблюдаемы страницей через <c lang="text">screen</c> и <c lang="text">devicePixelRatio</c>,
/// поэтому физический размер задаётся правдоподобным: выход с нулевой диагональю не бывает.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class WaylandOutput : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "wl_output";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        if (message.Opcode == RequestRelease)
            Client.Unregister(Id);
    }

    /// <summary>
    /// Описывает выход клиенту.
    /// </summary>
    /// <remarks>
    /// Вызывается сразу после привязки: клиент ждёт описания, а с четвёртой версии — ещё и
    /// завершающего <c lang="text">done</c>, без которого он считает данные неполными.
    /// </remarks>
    public void SendConfiguration()
    {
        var settings = Client.Compositor.Settings;

        Emit(EventGeometry, writer => writer
            .WriteInt(0)
            .WriteInt(0)
            .WriteInt(settings.PhysicalSizeMillimeters.Width)
            .WriteInt(settings.PhysicalSizeMillimeters.Height)
            .WriteInt(SubpixelUnknown)
            .WriteString(settings.OutputMake)
            .WriteString(settings.OutputModel)
            .WriteInt(TransformNormal));

        Emit(EventMode, writer => writer
            .WriteUInt(ModeCurrent | ModePreferred)
            .WriteInt(settings.Resolution.Width)
            .WriteInt(settings.Resolution.Height)
            .WriteInt(settings.RefreshMilliHertz));

        if (Version >= 2)
            Emit(EventScale, writer => writer.WriteInt(settings.Scale));

        if (Version >= 4)
        {
            Emit(EventName, writer => writer.WriteString(settings.OutputName));
            Emit(EventDescription, writer => writer.WriteString(settings.OutputMake + " " + settings.OutputModel));
        }

        if (Version >= 2)
            Emit(EventDone);
    }

    private const ushort RequestRelease = 0;

    private const ushort EventGeometry = 0;
    private const ushort EventMode = 1;
    private const ushort EventDone = 2;
    private const ushort EventScale = 3;
    private const ushort EventName = 4;
    private const ushort EventDescription = 5;

    private const int SubpixelUnknown = 0;
    private const int TransformNormal = 0;
    private const uint ModeCurrent = 0x1;
    private const uint ModePreferred = 0x2;
}
