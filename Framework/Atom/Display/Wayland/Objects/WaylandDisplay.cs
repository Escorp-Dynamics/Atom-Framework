using System.Runtime.Versioning;

using Atom.Display.Wayland.Protocol;

namespace Atom.Display.Wayland.Objects;

/// <summary>
/// Объект <c lang="text">wl_display</c> — точка входа протокола.
/// </summary>
/// <remarks>
/// Всегда имеет идентификатор 1: клиент знает его заранее и с него начинает разговор.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class WaylandDisplay : WaylandObject
{
    /// <summary>Идентификатор, под которым объект известен клиенту без запроса.</summary>
    public const uint WellKnownId = 1;

    /// <inheritdoc/>
    public override string InterfaceName => "wl_display";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        var offset = 0;

        switch (message.Opcode)
        {
            case RequestSync:
                HandleSync(message.ReadUInt(ref offset));
                break;

            case RequestGetRegistry:
                HandleGetRegistry(message.ReadUInt(ref offset));
                break;
        }
    }

    /// <summary>
    /// Отвечает на запрос синхронизации.
    /// </summary>
    /// <remarks>
    /// Клиент создаёт одноразовый объект обратного вызова и ждёт события <c lang="text">done</c>. Так он
    /// узнаёт, что композитор обработал все предыдущие запросы: ответ приходит строго после них,
    /// потому что сообщения обрабатываются по порядку.
    /// </remarks>
    private void HandleSync(uint callbackId)
    {
        var callback = new WaylandCallback { Id = callbackId, Version = 1, Client = Client };

        // ★ В таблицу НЕ кладём: объект одноразовый и запросов не принимает, а регистрация с
        // последующим удалением дала бы второй delete_id на тот же номер.
        callback.SendDone(Client.Compositor.NextSerial());
        Client.SendDeleteId(callbackId);
    }

    private void HandleGetRegistry(uint registryId)
    {
        var registry = new WaylandRegistry { Id = registryId, Version = 1, Client = Client };
        Client.Register(registry);
        registry.AnnounceGlobals();
    }

    /// <summary>
    /// Сообщает клиенту об удалении объекта.
    /// </summary>
    /// <remarks>
    /// Без этого события клиент не может переиспользовать идентификатор: он обязан дождаться
    /// подтверждения, иначе композитор и клиент разойдутся в понимании таблицы объектов.
    /// </remarks>
    public void SendDeleteId(uint objectId) => Emit(EventDeleteId, writer => writer.WriteUInt(objectId));

    /// <summary>Сообщает клиенту о фатальной ошибке протокола.</summary>
    public void SendError(uint objectId, uint code, string description)
        => Emit(EventError, writer => writer
            .WriteUInt(objectId)
            .WriteUInt(code)
            .WriteString(description));

    private const ushort RequestSync = 0;
    private const ushort RequestGetRegistry = 1;

    private const ushort EventError = 0;
    private const ushort EventDeleteId = 1;
}

/// <summary>
/// Одноразовый объект подтверждения <c lang="text">wl_callback</c>.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class WaylandCallback : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "wl_callback";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        // У wl_callback нет запросов: клиент только принимает событие done.
    }

    /// <summary>Подтверждает завершение и делает объект недействительным.</summary>
    public void SendDone(uint serial) => Emit(EventDone, writer => writer.WriteUInt(serial));

    private const ushort EventDone = 0;
}
