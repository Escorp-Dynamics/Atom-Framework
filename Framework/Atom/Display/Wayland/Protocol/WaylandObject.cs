using System.Runtime.Versioning;

namespace Atom.Display.Wayland.Protocol;

/// <summary>
/// Объект протокола, которому клиент шлёт запросы.
/// </summary>
[SupportedOSPlatform("linux")]
internal abstract class WaylandObject
{
    /// <summary>Идентификатор объекта в пространстве клиента.</summary>
    public required uint Id { get; init; }

    /// <summary>Версия интерфейса, запрошенная клиентом.</summary>
    public required uint Version { get; init; }

    /// <summary>Клиент, которому принадлежит объект.</summary>
    public required WaylandClient Client { get; init; }

    /// <summary>Имя интерфейса, как оно объявлено в протоколе.</summary>
    public abstract string InterfaceName { get; }

    /// <summary>
    /// Обрабатывает запрос клиента.
    /// </summary>
    /// <param name="message">Сообщение с кодом операции и аргументами.</param>
    public abstract void HandleRequest(in WaylandMessage message);

    /// <summary>
    /// Освобождает ресурсы объекта при уничтожении.
    /// </summary>
    public virtual void OnDestroyed() { }

    /// <summary>
    /// Очёрёдность разрушения при отключении клиента: больше — раньше.
    /// </summary>
    /// <remarks>
    /// Нужна там, где один объект владеет ресурсом другого и отпускает его по числу ссылок.
    /// </remarks>
    public virtual int DestructionPriority => 0;

    /// <summary>
    /// Отправляет клиенту событие этого объекта.
    /// </summary>
    /// <remarks>Писатель общий на клиента: событий сотни в секунду, и новый на каждое — мусор.</remarks>
    protected void Emit(ushort opcode, Action<WaylandMessageWriter>? writeArguments = null)
    {
        var writer = Client.Writer.Begin(Id, opcode);
        writeArguments?.Invoke(writer);
        Client.Send(writer.Build());
    }

    /// <summary>Отправляет событие вместе с файловым дескриптором.</summary>
    protected void EmitWithDescriptor(ushort opcode, int descriptor, Action<WaylandMessageWriter>? writeArguments = null)
    {
        var writer = Client.Writer.Begin(Id, opcode);
        writeArguments?.Invoke(writer);
        Client.SendWithDescriptor(writer.Build(), descriptor);
    }
}
