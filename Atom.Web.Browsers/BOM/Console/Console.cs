using Atom.Architect.Reactive;
using Microsoft.ClearScript;

namespace Atom.Web.Browsers.BOM;

/// <summary>
/// Представляет консоль отладки браузера.
/// </summary>
public class Console : IConsole
{
    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.None)]
    public event AsyncEventHandler<ConsoleMessageEventArgs>? Message;

    /// <summary>
    /// Происходит в момент записи сообщения в журнал.
    /// </summary>
    /// <param name="e">Аргументы события.</param>
    [ScriptMember(ScriptAccess.None)]
    protected virtual ValueTask OnMessage(ConsoleMessageEventArgs e) => Message?.Invoke(e) ?? ValueTask.CompletedTask;

    /// <inheritdoc/>
    [ScriptMember]
    public void Assert(bool condition, params object?[] data) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void Assert(params object?[] data) => Assert(default, data);

    /// <inheritdoc/>
    [ScriptMember]
    public void Clear() { }

    /// <inheritdoc/>
    [ScriptMember]
    public async void Debug(params object?[] data) => await OnMessage(new ConsoleMessageEventArgs(ConsoleMessageType.Debug, data)).ConfigureAwait(false);

    /// <inheritdoc/>
    [ScriptMember]
    public async void Error(params object?[] data) => await OnMessage(new ConsoleMessageEventArgs(ConsoleMessageType.Error, data)).ConfigureAwait(false);

    /// <inheritdoc/>
    [ScriptMember]
    public async void Info(params object?[] data) => await OnMessage(new ConsoleMessageEventArgs(ConsoleMessageType.Info, data)).ConfigureAwait(false);

    /// <inheritdoc/>
    [ScriptMember]
    public async void Log(params object?[] data) => await OnMessage(new ConsoleMessageEventArgs(ConsoleMessageType.Log, data)).ConfigureAwait(false);

    /// <inheritdoc/>
    [ScriptMember]
    public async void Trace(params object?[] data) => await OnMessage(new ConsoleMessageEventArgs(ConsoleMessageType.Trace, data)).ConfigureAwait(false);

    /// <inheritdoc/>
    [ScriptMember]
    public async void Warn(params object?[] data) => await OnMessage(new ConsoleMessageEventArgs(ConsoleMessageType.Warn, data)).ConfigureAwait(false);

    /// <inheritdoc/>
    [ScriptMember("dirxml")]
    public void DirXML(params object?[] data) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void Table(object? tabularData, IEnumerable<string> properties) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void Table(object? tabularData) => Table(tabularData, []);

    /// <inheritdoc/>
    [ScriptMember]
    public void Table(IEnumerable<string> properties) => Table(default, properties);

    /// <inheritdoc/>
    [ScriptMember]
    public void Table() => Table([]);

    /// <inheritdoc/>
    [ScriptMember]
    public void Dir(object? item, IReadOnlyDictionary<string, object?>? options) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void Dir(object? item) => Dir(item, default);

    /// <inheritdoc/>
    [ScriptMember]
    public void Dir(IReadOnlyDictionary<string, object?>? options) => Dir(default, options);

    /// <inheritdoc/>
    [ScriptMember]
    public void Dir() => Dir(default, default);

    /// <inheritdoc/>
    [ScriptMember]
    public void Count(string label) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void Count() => Count("default");

    /// <inheritdoc/>
    [ScriptMember]
    public void CountReset(string label) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void CountReset() => CountReset("default");

    /// <inheritdoc/>
    [ScriptMember]
    public void Group(params object?[] data) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void GroupCollapsed(params object?[] data) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void GroupEnd() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void Time(string label) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void Time() => Time("default");

    /// <inheritdoc/>
    [ScriptMember]
    public void TimeLog(string label, params object?[] data) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void TimeLog(string label) => TimeLog(label, []);

    /// <inheritdoc/>
    [ScriptMember]
    public void TimeLog(params object?[] data) => TimeLog("default", data);

    /// <inheritdoc/>
    [ScriptMember]
    public void TimeLog() => TimeLog("default");

    /// <inheritdoc/>
    [ScriptMember]
    public void TimeEnd(string label) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void TimeEnd() => TimeEnd("default");
}