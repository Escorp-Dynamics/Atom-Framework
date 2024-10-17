namespace Atom.Debug;

public partial interface ILog
{
    /// <summary>
    /// Делает запись отладочной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask DebugAsync<T>(string message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись отладочной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    ValueTask DebugAsync<T>(string message, LogMode mode, ConsoleColor color, T? data) => DebugAsync(message, mode, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись отладочной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask DebugAsync<T>(string message, LogMode mode, T? data, CancellationToken cancellationToken) => DebugAsync(message, mode, ConsoleColor.Gray, data, cancellationToken);

    /// <summary>
    /// Делает запись отладочной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="data">Связанные данные.</param>
    ValueTask DebugAsync<T>(string message, LogMode mode, T? data) => DebugAsync(message, mode, data, CancellationToken.None);

    /// <summary>
    /// Делает запись отладочной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask DebugAsync<T>(string message, T? data, CancellationToken cancellationToken) => DebugAsync(message, LogMode.All, data, cancellationToken);

    /// <summary>
    /// Делает запись отладочной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    ValueTask DebugAsync<T>(string message, T? data) => DebugAsync(message, data, CancellationToken.None);

    /// <summary>
    /// Делает запись отладочной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask DebugAsync<T>(string message, ConsoleColor color, T? data, CancellationToken cancellationToken) => DebugAsync(message, LogMode.All, color, data, cancellationToken);

    /// <summary>
    /// Делает запись отладочной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    ValueTask DebugAsync<T>(string message, ConsoleColor color, T? data) => DebugAsync(message, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись отладочной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask DebugAsync(string message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись отладочной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    ValueTask DebugAsync(string message, LogMode mode, ConsoleColor color) => DebugAsync(message, mode, color, CancellationToken.None);

    /// <summary>
    /// Делает запись отладочной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask DebugAsync(string message, LogMode mode, CancellationToken cancellationToken) => DebugAsync(message, mode, ConsoleColor.Gray, cancellationToken);

    /// <summary>
    /// Делает запись отладочной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    ValueTask DebugAsync(string message, LogMode mode) => DebugAsync(message, mode, CancellationToken.None);

    /// <summary>
    /// Делает запись отладочной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask DebugAsync(string message, CancellationToken cancellationToken) => DebugAsync(message, LogMode.All, cancellationToken);

    /// <summary>
    /// Делает запись отладочной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    ValueTask DebugAsync(string message) => DebugAsync(message, CancellationToken.None);

    /// <summary>
    /// Делает запись отладочной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask DebugAsync(string message, ConsoleColor color, CancellationToken cancellationToken) => DebugAsync(message, LogMode.All, color, cancellationToken);

    /// <summary>
    /// Делает запись отладочной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    ValueTask DebugAsync(string message, ConsoleColor color) => DebugAsync(message, color, CancellationToken.None);
}