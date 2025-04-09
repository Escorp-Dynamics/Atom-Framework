using Microsoft.ClearScript;

namespace Atom.Web.Browsing.BOM;

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
    public void Assert(bool condition, params IEnumerable<object?> data) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void Assert(params IEnumerable<object?> data) => Assert(default, data);

    /// <inheritdoc/>
    [ScriptMember]
    public void Clear() { }

    /// <inheritdoc/>
    [ScriptMember]
    public async void Debug(params IEnumerable<object?> data) => await OnMessage(new ConsoleMessageEventArgs(ConsoleMessageType.Debug, data)).ConfigureAwait(false);

    /// <inheritdoc/>
    [ScriptMember]
    public async void Error(params IEnumerable<object?> data) => await OnMessage(new ConsoleMessageEventArgs(ConsoleMessageType.Error, data)).ConfigureAwait(false);

    /// <inheritdoc/>
    [ScriptMember]
    public async void Info(params IEnumerable<object?> data) => await OnMessage(new ConsoleMessageEventArgs(ConsoleMessageType.Info, data)).ConfigureAwait(false);

    /// <inheritdoc/>
    [ScriptMember]
    public async void Log(params IEnumerable<object?> data) => await OnMessage(new ConsoleMessageEventArgs(ConsoleMessageType.Log, data)).ConfigureAwait(false);

    /// <inheritdoc/>
    [ScriptMember]
    public async void Trace(params IEnumerable<object?> data) => await OnMessage(new ConsoleMessageEventArgs(ConsoleMessageType.Trace, data)).ConfigureAwait(false);

    /// <inheritdoc/>
    [ScriptMember]
    public async void Warn(params IEnumerable<object?> data) => await OnMessage(new ConsoleMessageEventArgs(ConsoleMessageType.Warn, data)).ConfigureAwait(false);

    /// <inheritdoc/>
    [ScriptMember("dirxml")]
    public void DirXML(params IEnumerable<object?> data) => throw new NotImplementedException();

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
    public void Group(params IEnumerable<object?> data) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void GroupCollapsed(params IEnumerable<object?> data) => throw new NotImplementedException();

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
    public void TimeLog(string label, params IEnumerable<object?> data) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void TimeLog(string label) => TimeLog(label, []);

    /// <inheritdoc/>
    [ScriptMember]
    public void TimeLog(params IEnumerable<object?> data) => TimeLog("default", data);

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