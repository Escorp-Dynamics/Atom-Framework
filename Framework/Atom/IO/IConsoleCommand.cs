namespace Atom.Debug;

/// <summary>
/// Представляет базовый интерфейс для реализации команд консоли.
/// </summary>
public interface IConsoleCommand
{
    /// <summary>
    /// Псевдонимы распознавания команд.
    /// </summary>
    IEnumerable<string> Aliases { get; }

    /// <summary>
    /// Определяет, является ли команда отменяемой.
    /// </summary>
    bool IsCancellable { get; }

    /// <summary>
    /// Название команды для вывода в журнал.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Происходит в момент парсинга команды.
    /// </summary>
    event AsyncEventHandler<IConsoleCommand, ParseCommandEventArgs>? Parsing;

    /// <summary>
    /// Происходит в момент выполнения команды.
    /// </summary>
    event AsyncEventHandler<IConsoleCommand, ExecuteCommandEventArgs>? Execution;

    /// <summary>
    /// Происходит в момент отмены команды.
    /// </summary>
    event AsyncEventHandler<IConsoleCommand, ExecuteCommandEventArgs>? Cancelling;

    /// <summary>
    /// Парсит команду.
    /// </summary>
    /// <param name="command">Исходная строка команды.</param>
    /// <param name="args">Аргументы команды.</param>
    /// <param name="isCancellation">Указывает, находится ли команда в режиме отмены.</param>
    /// <returns>True, если парсинг был успешен, иначе false.</returns>
    bool TryParse(string command, out IEnumerable<string> args, out bool isCancellation);

    /// <summary>
    /// Выполняет команду с заданными аргументами.
    /// </summary>
    /// <param name="args">Аргументы команды.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>True, если процедура была успешна, иначе false.</returns>
    ValueTask<bool> ExecuteAsync(IEnumerable<string> args, CancellationToken cancellationToken);

    /// <summary>
    /// Выполняет команду с заданными аргументами.
    /// </summary>
    /// <param name="args">Аргументы команды.</param>
    /// <returns>True, если процедура была успешна, иначе false.</returns>
    ValueTask<bool> ExecuteAsync(params IEnumerable<string> args) => ExecuteAsync(args, CancellationToken.None);

    /// <summary>
    /// Выполняет команду.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>True, если процедура была успешна, иначе false.</returns>
    ValueTask<bool> ExecuteAsync(CancellationToken cancellationToken) => ExecuteAsync([], cancellationToken);

    /// <summary>
    /// Выполняет команду.
    /// </summary>
    /// <returns>True, если процедура была успешна, иначе false.</returns>
    ValueTask<bool> ExecuteAsync() => ExecuteAsync(CancellationToken.None);

    /// <summary>
    /// Отменяет команду с заданными аргументами.
    /// </summary>
    /// <param name="args">Аргументы команды.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>True, если процедура была успешна, иначе false.</returns>
    ValueTask<bool> CancelAsync(IEnumerable<string> args, CancellationToken cancellationToken);

    /// <summary>
    /// Отменяет команду с заданными аргументами.
    /// </summary>
    /// <param name="args">Аргументы команды.</param>
    /// <returns>True, если процедура была успешна, иначе false.</returns>
    ValueTask<bool> CancelAsync(params IEnumerable<string> args) => CancelAsync(args, CancellationToken.None);

    /// <summary>
    /// Отменяет команду.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>True, если процедура была успешна, иначе false.</returns>
    ValueTask<bool> CancelAsync(CancellationToken cancellationToken) => CancelAsync([], cancellationToken);

    /// <summary>
    /// Отменяет команду.
    /// </summary>
    /// <returns>True, если процедура была успешна, иначе false.</returns>
    ValueTask<bool> CancelAsync() => CancelAsync(CancellationToken.None);
}