using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using Atom.Buffers;
using Atom.Threading;

namespace Atom.Debug;

/// <summary>
/// Представляет инструментарий для работы с консолью процесса.
/// </summary>
public static class ConsoleProcessor
{
    private static readonly ObservableCollection<IConsoleCommand> commands = [];

    private static event AsyncEventHandler CommandInternal;

    /// <summary>
    /// Привязанные команды ввода.
    /// </summary>
    public static IEnumerable<IConsoleCommand> Commands => commands;

    /// <summary>
    /// Происходит в момент вызова команды.
    /// </summary>
    public static event AsyncEventHandler<InputEventArgs>? Command;

    /// <summary>
    /// Происходит в момент изменения набора доступных команд.
    /// </summary>
    public static event MutableEventHandler<NotifyCollectionChangedEventArgs>? CommandsChanged;

    static ConsoleProcessor()
    {
        commands.CollectionChanged += (_, e) => OnCommandsChanged(e);

        CommandInternal += OnCommandInternal;
        Task.Run(OnCommandInternal);
    }

    private static async ValueTask OnCommandInternal()
    {
        await Wait.UntilAsync(() => Console.KeyAvailable).ConfigureAwait(false);

        var sb = ObjectPool<StringBuilder>.Shared.Rent();
        ConsoleKeyInfo keyInfo;

        do
        {
            keyInfo = Console.ReadKey(true);
            sb.Append(keyInfo.KeyChar);
        } while (keyInfo.Key is not ConsoleKey.Enter);

        var command = sb.ToString().Trim();
        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());

        if (string.IsNullOrEmpty(command))
        {
            if (CommandInternal is not null) await CommandInternal().ConfigureAwait(false);
            return;
        }

        var eventArgs = new InputEventArgs(command);
        if (Command is not null) await Command(eventArgs).ConfigureAwait(false);

        if (eventArgs.IsCancelled)
        {
            if (CommandInternal is not null) await CommandInternal().ConfigureAwait(false);
            return;
        }

        foreach (var c in commands)
        {
            if (c.TryParse(command, out var args, out var isCancellation))
            {
                if (!isCancellation)
                    await c.ExecuteAsync(args).ConfigureAwait(false);
                else
                    await c.CancelAsync(args).ConfigureAwait(false);
            }
        }

        if (CommandInternal is not null) await CommandInternal().ConfigureAwait(false);
    }

    /// <summary>
    /// Происходит в момент изменения коллекции связанных команд.
    /// </summary>
    /// <param name="e">Аргументы события.</param>
    private static void OnCommandsChanged(NotifyCollectionChangedEventArgs e) => CommandsChanged?.Invoke(e);

    /// <summary>
    /// Добавляет команду ввода. Если команда этого типа уже существует, она будет переназначена.
    /// </summary>
    /// <typeparam name="TCommand">Тип команды.</typeparam>
    /// <param name="command">Экземпляр команды.</param>
    public static void UseCommand<TCommand>(TCommand command) where TCommand : class, IConsoleCommand
    {
        if (TryGetCommand<TCommand>(out var m) && m is not null) commands.Remove(m);
        commands.Add(command);
    }

    /// <summary>
    /// Добавляет команду ввода. Если команда этого типа уже существует, она будет переназначена.
    /// </summary>
    /// <typeparam name="TCommand">Тип команды.</typeparam>
    public static void UseCommand<TCommand>() where TCommand : class, IConsoleCommand, new() => UseCommand(new TCommand());

    /// <summary>
    /// Определяет, доступна ли команда указанного типа.
    /// </summary>
    /// <typeparam name="TCommand">Тип команды.</typeparam>
    public static bool HasCommand<TCommand>() where TCommand : class, IConsoleCommand => commands.Any(x => x is TCommand);

    /// <summary>
    /// Возвращает команду указанного типа.
    /// </summary>
    /// <typeparam name="TCommand">Тип команды.</typeparam>
    /// <param name="command">Экземпляр команды.</param>
    public static bool TryGetCommand<TCommand>(out TCommand? command) where TCommand : class, IConsoleCommand
    {
        command = commands.OfType<TCommand>().FirstOrDefault();
        return command is not null;
    }
}