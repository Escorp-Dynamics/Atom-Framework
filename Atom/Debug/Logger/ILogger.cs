using System.Collections.Specialized;

namespace Atom.Debug;

public partial interface ILogger : IAsyncDisposable
{
    /// <summary>
    /// Имя журнала.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Режим записи.
    /// </summary>
    LogMode Mode { get; set; }

    /// <summary>
    /// Путь к файлу журнала, если включён режим <see cref="LogMode.File"/>.
    /// </summary>
    string? Path { get; }

    /// <summary>
    /// Определяет, включено ли форматирование для <see cref="LogMode.Console"/>.
    /// </summary>
    bool IsFormattingEnabled { get; set; }

    bool IsEnabled { get; set; }

    IDictionary<LogMode, LogType> Filter { get; }

    IEnumerable<IConsoleCommand> Commands { get; }

    /// <summary>
    /// Происходит в момент записи в журнал.
    /// </summary>
    event AsyncEventHandler<ILog, LogEventArgs>? Writting;

    event AsyncEventHandler<ILogger, InputEventArgs>? Command;

    /// <summary>
    /// Происходит в момент изменения коллекции подключенных команд.
    /// </summary>
    event AsyncEventHandler<ILogger, NotifyCollectionChangedEventArgs>? CommandsChanged;

    /// <summary>
    /// Подключает команду.
    /// </summary>
    /// <typeparam name="TCommand">Тип команды.</typeparam>
    /// <param name="command">Экземпляр команды.</param>
    /// <returns>Экземпляр текущей фабрики журнала.</returns>
    ILogger UseCommand<TCommand>(TCommand command) where TCommand : class, IConsoleCommand;

    /// <summary>
    /// Подключает команду.
    /// </summary>
    /// <typeparam name="TCommand">Тип команды.</typeparam>
    /// <returns>Экземпляр текущей фабрики журнала.</returns>
    ILogger UseCommand<TCommand>() where TCommand : class, IConsoleCommand, new() => UseCommand(new TCommand() { Log = this, });

    /// <summary>
    /// Определяет, подключена ли команда.
    /// </summary>
    /// <typeparam name="TCommand">Тип команды.</typeparam>
    /// <returns>True, если команда подключена, иначе false.</returns>
    bool HasCommand<TCommand>() where TCommand : class, IConsoleCommand;

    /// <summary>
    /// Получает экземпляр подключенной команды.
    /// </summary>
    /// <typeparam name="TCommand">Тип команды.</typeparam>
    /// <param name="command">Экземпляр команды.</param>
    /// <returns>True, если команда была подключена, иначе false.</returns>
    bool TryGetCommand<TCommand>(out TCommand? command) where TCommand : class, IConsoleCommand;

    ValueTask HeaderAsync(CancellationToken cancellationToken);

    ValueTask HeaderAsync() => HeaderAsync(CancellationToken.None);

    ValueTask ResetLineAsync(int offset, CancellationToken cancellationToken);

    ValueTask ResetLineAsync(int offset) => ResetLineAsync(offset, CancellationToken.None);

    ValueTask ResetLineAsync(CancellationToken cancellationToken) => ResetLineAsync(0, cancellationToken);

    ValueTask ResetLineAsync() => ResetLineAsync(CancellationToken.None);
}