namespace Atom.Debug;

public partial interface ILogger
{
    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask WarningAsync<T>(string? message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    ValueTask WarningAsync<T>(string? message, LogMode mode, ConsoleColor color, T? data) => WarningAsync(message, mode, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask WarningAsync<T>(string? message, LogMode mode, T? data, CancellationToken cancellationToken) => WarningAsync(message, mode, ConsoleColor.Yellow, data, cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="data">Связанные данные.</param>
    ValueTask WarningAsync<T>(string? message, LogMode mode, T? data) => WarningAsync(message, mode, data, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask WarningAsync<T>(string? message, T? data, CancellationToken cancellationToken) => WarningAsync(message, LogMode.All, data, cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    ValueTask WarningAsync<T>(string? message, T? data) => WarningAsync(message, data, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask WarningAsync<T>(string? message, ConsoleColor color, T? data, CancellationToken cancellationToken) => WarningAsync(message, LogMode.All, color, data, cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    ValueTask WarningAsync<T>(string? message, ConsoleColor color, T? data) => WarningAsync(message, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask WarningAsync<T>(string? message, string messageForFile, ConsoleColor color, T? data, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    ValueTask WarningAsync<T>(string? message, string messageForFile, ConsoleColor color, T? data) => WarningAsync(message, messageForFile, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask WarningAsync<T>(string? message, string messageForFile, T? data, CancellationToken cancellationToken) => WarningAsync(message, messageForFile, ConsoleColor.Yellow, data, cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="data">Связанные данные.</param>
    ValueTask WarningAsync<T>(string? message, string messageForFile, T? data) => WarningAsync(message, messageForFile, data, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask WarningAsync(string? message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    ValueTask WarningAsync(string? message, LogMode mode, ConsoleColor color) => WarningAsync(message, mode, color, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask WarningAsync(string? message, LogMode mode, CancellationToken cancellationToken) => WarningAsync(message, mode, ConsoleColor.Yellow, cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    ValueTask WarningAsync(string? message, LogMode mode) => WarningAsync(message, mode, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask WarningAsync(string? message, CancellationToken cancellationToken) => WarningAsync(message, LogMode.All, cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    ValueTask WarningAsync(string? message) => WarningAsync(message, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask WarningAsync(string? message, ConsoleColor color, CancellationToken cancellationToken) => WarningAsync(message, LogMode.All, color, cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    ValueTask WarningAsync(string? message, ConsoleColor color) => WarningAsync(message, color, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask WarningAsync(string? message, string messageForFile, ConsoleColor color, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="color">Цвет текста записи.</param>
    ValueTask WarningAsync(string? message, string messageForFile, ConsoleColor color) => WarningAsync(message, messageForFile, color, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask WarningAsync(string? message, string messageForFile, CancellationToken cancellationToken) => WarningAsync(message, messageForFile, ConsoleColor.Yellow, cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    ValueTask WarningAsync(string? message, string messageForFile) => WarningAsync(message, messageForFile, CancellationToken.None);
}