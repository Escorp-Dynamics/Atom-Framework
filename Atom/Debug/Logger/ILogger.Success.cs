namespace Atom.Debug;

public partial interface ILogger
{
    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask SuccessAsync<T>(string? message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask SuccessAsync<T>(string? message, LogMode mode, ConsoleColor color, T? data) => SuccessAsync(message, mode, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask SuccessAsync<T>(string? message, LogMode mode, T? data, CancellationToken cancellationToken) => SuccessAsync(message, mode, ConsoleColor.Green, data, cancellationToken);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask SuccessAsync<T>(string? message, LogMode mode, T? data) => SuccessAsync(message, mode, data, CancellationToken.None);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask SuccessAsync<T>(string? message, T? data, CancellationToken cancellationToken) => SuccessAsync(message, LogMode.All, data, cancellationToken);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask SuccessAsync<T>(string? message, T? data) => SuccessAsync(message, data, CancellationToken.None);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask SuccessAsync<T>(string? message, ConsoleColor color, T? data, CancellationToken cancellationToken) => SuccessAsync(message, LogMode.All, color, data, cancellationToken);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask SuccessAsync<T>(string? message, ConsoleColor color, T? data) => SuccessAsync(message, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask SuccessAsync<T>(string? message, string messageForFile, ConsoleColor color, T? data, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask SuccessAsync<T>(string? message, string messageForFile, ConsoleColor color, T? data) => SuccessAsync(message, messageForFile, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask SuccessAsync<T>(string? message, string messageForFile, T? data, CancellationToken cancellationToken) => SuccessAsync(message, messageForFile, ConsoleColor.Green, data, cancellationToken);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask SuccessAsync<T>(string? message, string messageForFile, T? data) => SuccessAsync(message, messageForFile, data, CancellationToken.None);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask SuccessAsync(string? message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <returns></returns>
    ValueTask SuccessAsync(string? message, LogMode mode, ConsoleColor color) => SuccessAsync(message, mode, color, CancellationToken.None);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask SuccessAsync(string? message, LogMode mode, CancellationToken cancellationToken) => SuccessAsync(message, mode, ConsoleColor.Green, cancellationToken);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <returns></returns>
    ValueTask SuccessAsync(string? message, LogMode mode) => SuccessAsync(message, mode, CancellationToken.None);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask SuccessAsync(string? message, CancellationToken cancellationToken) => SuccessAsync(message, LogMode.All, cancellationToken);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <returns></returns>
    ValueTask SuccessAsync(string? message) => SuccessAsync(message, CancellationToken.None);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask SuccessAsync(string? message, ConsoleColor color, CancellationToken cancellationToken) => SuccessAsync(message, LogMode.All, color, cancellationToken);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <returns></returns>
    ValueTask SuccessAsync(string? message, ConsoleColor color) => SuccessAsync(message, color, CancellationToken.None);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask SuccessAsync(string? message, string messageForFile, ConsoleColor color, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <returns></returns>
    ValueTask SuccessAsync(string? message, string messageForFile, ConsoleColor color) => SuccessAsync(message, messageForFile, color, CancellationToken.None);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask SuccessAsync(string? message, string messageForFile, CancellationToken cancellationToken) => SuccessAsync(message, messageForFile, ConsoleColor.Green, cancellationToken);

    /// <summary>
    /// Делает запись успешной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <returns></returns>
    ValueTask SuccessAsync(string? message, string messageForFile) => SuccessAsync(message, messageForFile, CancellationToken.None);
}