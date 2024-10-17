using System.Collections.Specialized;
using Atom.Architect.Reactive;

namespace Atom.Debug;

/// <summary>
/// Представляет базовый интерфейс для реализации менеджеров журнала событий.
/// </summary>
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

    /// <summary>
    /// Определяет, активна ли запись в журнал событий.
    /// </summary>
    bool IsBackground { get; set; }

    /// <summary>
    /// Определяет, будет ли отображаться сообщение в фоновом режиме.
    /// </summary>
    bool IsShowBackgroundMessage { get; set; }

    /// <summary>
    /// Фильтр по типам записей журнала для каждого режима записи в журнал.
    /// </summary>
    IDictionary<LogMode, LogType> Filter { get; }

    /// <summary>
    /// Коллекция связанных с журналом команд.
    /// </summary>
    IEnumerable<IConsoleCommand> Commands { get; }

    /// <summary>
    /// Происходит в момент записи в журнал.
    /// </summary>
    event AsyncEventHandler<ILog, LogEventArgs>? Writing;

    /// <summary>
    /// Происходит в момент ввода команды.
    /// </summary>
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
    ILogger UseCommand<TCommand>() where TCommand : class, IConsoleCommand, new() => UseCommand(new TCommand { Log = this, });

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

    /// <summary>
    /// Выводит заголовок журнала.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask HeaderAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Выводит заголовок журнала.
    /// </summary>
    /// <returns></returns>
    ValueTask HeaderAsync() => HeaderAsync(CancellationToken.None);

    /// <summary>
    /// Переводит курсор записи на одну строку вверх.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ResetLineAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Переводит курсор записи на одну строку вверх.
    /// </summary>
    /// <returns></returns>
    ValueTask ResetLineAsync() => ResetLineAsync(CancellationToken.None);

    /// <summary>
    /// Запускает журнал.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Запускает журнал.
    /// </summary>
    /// <returns></returns>
    ValueTask StartAsync() => StartAsync(CancellationToken.None);

    /// <summary>
    /// Ожидает завершение выполнения, блокируя поток.
    /// </summary>
    void Flush();
}