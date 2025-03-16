using Atom.Architect.Reactive;
using Microsoft.ClearScript;

namespace Atom.Web.Browsing.BOM;

/// <summary>
/// Представляет консоль отладки браузера.
/// </summary>
public interface IConsole
{
    /// <summary>
    /// Происходит в момент записи сообщения в журнал.
    /// </summary>
    [ScriptMember(ScriptAccess.None)]
    event AsyncEventHandler<ConsoleMessageEventArgs>? Message;

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="condition">TODO.</param>
    /// <param name="data">TODO.</param>
    [ScriptMember]
    void Assert(bool condition, params object?[] data);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="data">TODO.</param>
    [ScriptMember]
    void Assert(params object?[] data) => Assert(default, data);

    /// <summary>
    /// Очищает журнал консоли.
    /// </summary>
    [ScriptMember]
    void Clear();

    /// <summary>
    /// Записывает отладочное сообщение в журнал.
    /// </summary>
    /// <param name="data">Аргументы сообщения.</param>
    [ScriptMember]
    void Debug(params object?[] data);

    /// <summary>
    /// Записывает сообщение об ошибке в журнал.
    /// </summary>
    /// <param name="data">Аргументы сообщения.</param>
    [ScriptMember]
    void Error(params object?[] data);

    /// <summary>
    /// Записывает сообщение в журнал.
    /// </summary>
    /// <param name="data">Аргументы сообщения.</param>
    [ScriptMember]
    void Info(params object?[] data);

    /// <summary>
    /// Записывает сообщение в журнал.
    /// </summary>
    /// <param name="data">Аргументы сообщения.</param>
    [ScriptMember]
    void Log(params object?[] data);

    /// <summary>
    /// Записывает сообщение в журнал.
    /// </summary>
    /// <param name="data">Аргументы сообщения.</param>
    [ScriptMember]
    void Trace(params object?[] data);

    /// <summary>
    /// Записывает сообщение о предупреждении в журнал.
    /// </summary>
    /// <param name="data">Аргументы сообщения.</param>
    [ScriptMember]
    void Warn(params object?[] data);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="data">TODO.</param>
    [ScriptMember("dirxml")]
    void DirXML(params object?[] data);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="tabularData">TODO.</param>
    /// <param name="properties">TODO.</param>
    [ScriptMember]
    void Table(object? tabularData, IEnumerable<string> properties);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="tabularData">TODO.</param>
    [ScriptMember]
    void Table(object? tabularData) => Table(tabularData, []);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="properties">TODO.</param>
    [ScriptMember]
    void Table(IEnumerable<string> properties) => Table(default, properties);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void Table() => Table([]);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="item">TODO.</param>
    /// <param name="options">TODO.</param>
    [ScriptMember]
    void Dir(object? item, IReadOnlyDictionary<string, object?>? options);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="item">TODO.</param>
    [ScriptMember]
    void Dir(object? item) => Dir(item, default);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="options">TODO.</param>
    [ScriptMember]
    void Dir(IReadOnlyDictionary<string, object?>? options) => Dir(default, options);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void Dir() => Dir(default, default);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="label">TODO.</param>
    [ScriptMember]
    void Count(string label);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void Count() => Count("default");

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="label">TODO.</param>
    [ScriptMember]
    void CountReset(string label);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void CountReset() => CountReset("default");

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="data">TODO.</param>
    [ScriptMember]
    void Group(params object?[] data);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="data">TODO.</param>
    [ScriptMember]
    void GroupCollapsed(params object?[] data);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void GroupEnd();

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="label">TODO.</param>
    [ScriptMember]
    void Time(string label);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void Time() => Time("default");

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="label">TODO.</param>
    /// <param name="data">TODO.</param>
    [ScriptMember]
    void TimeLog(string label, params object?[] data);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="label">TODO.</param>
    [ScriptMember]
    void TimeLog(string label) => TimeLog(label, []);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="data">TODO.</param>
    [ScriptMember]
    void TimeLog(params object?[] data) => TimeLog("default", data);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void TimeLog() => TimeLog("default");

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="label">TODO.</param>
    [ScriptMember]
    void TimeEnd(string label);

    /// <summary>
    /// TODO.
    /// </summary>
    [ScriptMember]
    void TimeEnd() => TimeEnd("default");
}