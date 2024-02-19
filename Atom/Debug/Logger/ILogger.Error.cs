namespace Atom.Debug;

public partial interface ILogger
{
    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync<T>(string? message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask ErrorAsync<T>(string? message, LogMode mode, ConsoleColor color, T? data) => ErrorAsync(message, mode, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync<T>(string? message, LogMode mode, T? data, CancellationToken cancellationToken) => ErrorAsync(message, mode, ConsoleColor.Red, data, cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask ErrorAsync<T>(string? message, LogMode mode, T? data) => ErrorAsync(message, mode, data, CancellationToken.None);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync<T>(string? message, T? data, CancellationToken cancellationToken) => ErrorAsync(message, LogMode.All, data, cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask ErrorAsync<T>(string? message, T? data) => ErrorAsync(message, data, CancellationToken.None);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync<T>(string? message, ConsoleColor color, T? data, CancellationToken cancellationToken) => ErrorAsync(message, LogMode.All, color, data, cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask ErrorAsync<T>(string? message, ConsoleColor color, T? data) => ErrorAsync(message, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync<T>(string? message, string messageForFile, ConsoleColor color, T? data, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask ErrorAsync<T>(string? message, string messageForFile, ConsoleColor color, T? data) => ErrorAsync(message, messageForFile, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync<T>(string? message, string messageForFile, T? data, CancellationToken cancellationToken) => ErrorAsync(message, messageForFile, ConsoleColor.Red, data, cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask ErrorAsync<T>(string? message, string messageForFile, T? data) => ErrorAsync(message, messageForFile, data, CancellationToken.None);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, LogMode mode, ConsoleColor color, Exception ex, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, LogMode mode, ConsoleColor color, Exception ex) => ErrorAsync(message, mode, color, ex, CancellationToken.None);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, LogMode mode, Exception ex, CancellationToken cancellationToken) => ErrorAsync(message, mode, ConsoleColor.Red, ex, cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, LogMode mode, Exception ex) => ErrorAsync(message, mode, ex, CancellationToken.None);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, Exception ex, CancellationToken cancellationToken) => ErrorAsync(message, LogMode.All, ex, cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, Exception ex) => ErrorAsync(message, ex, CancellationToken.None);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, ConsoleColor color, Exception ex, CancellationToken cancellationToken) => ErrorAsync(message, LogMode.All, color, ex, cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, ConsoleColor color, Exception ex) => ErrorAsync(message, color, ex, CancellationToken.None);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, string messageForFile, ConsoleColor color, Exception ex, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, string messageForFile, ConsoleColor color, Exception ex) => ErrorAsync(message, messageForFile, color, ex, CancellationToken.None);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, string messageForFile, Exception ex, CancellationToken cancellationToken) => ErrorAsync(message, messageForFile, ConsoleColor.Red, ex, cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, string messageForFile, Exception ex) => ErrorAsync(message, messageForFile, ex, CancellationToken.None);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, LogMode mode, ConsoleColor color) => ErrorAsync(message, mode, color, CancellationToken.None);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, LogMode mode, CancellationToken cancellationToken) => ErrorAsync(message, mode, ConsoleColor.Red, cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, LogMode mode) => ErrorAsync(message, mode, CancellationToken.None);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, CancellationToken cancellationToken) => ErrorAsync(message, LogMode.All, cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message) => ErrorAsync(message, CancellationToken.None);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, ConsoleColor color, CancellationToken cancellationToken) => ErrorAsync(message, LogMode.All, color, cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, ConsoleColor color) => ErrorAsync(message, color, CancellationToken.None);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, string messageForFile, ConsoleColor color, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, string messageForFile, ConsoleColor color) => ErrorAsync(message, messageForFile, color, CancellationToken.None);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, string messageForFile, CancellationToken cancellationToken) => ErrorAsync(message, messageForFile, ConsoleColor.Red, cancellationToken);

    /// <summary>
    /// Делает запись ошибок в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="messageForFile">Сообщение журнала (для записи в файл).</param>
    /// <returns></returns>
    ValueTask ErrorAsync(string? message, string messageForFile) => ErrorAsync(message, messageForFile, CancellationToken.None);
}