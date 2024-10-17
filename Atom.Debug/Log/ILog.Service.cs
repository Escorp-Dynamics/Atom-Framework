namespace Atom.Debug;

public partial interface ILog
{
    /// <summary>
    /// Делает запись сервисной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask ServiceAsync<T>(string message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись сервисной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    ValueTask ServiceAsync<T>(string message, LogMode mode, ConsoleColor color, T? data) => ServiceAsync(message, mode, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись сервисной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask ServiceAsync<T>(string message, LogMode mode, T? data, CancellationToken cancellationToken) => ServiceAsync(message, mode, ConsoleColor.Gray, data, cancellationToken);

    /// <summary>
    /// Делает запись сервисной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="data">Связанные данные.</param>
    ValueTask ServiceAsync<T>(string message, LogMode mode, T? data) => ServiceAsync(message, mode, data, CancellationToken.None);

    /// <summary>
    /// Делает запись сервисной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask ServiceAsync<T>(string message, T? data, CancellationToken cancellationToken) => ServiceAsync(message, LogMode.All, data, cancellationToken);

    /// <summary>
    /// Делает запись сервисной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    ValueTask ServiceAsync<T>(string message, T? data) => ServiceAsync(message, data, CancellationToken.None);

    /// <summary>
    /// Делает запись сервисной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask ServiceAsync<T>(string message, ConsoleColor color, T? data, CancellationToken cancellationToken) => ServiceAsync(message, LogMode.All, color, data, cancellationToken);

    /// <summary>
    /// Делает запись сервисной информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    ValueTask ServiceAsync<T>(string message, ConsoleColor color, T? data) => ServiceAsync(message, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись сервисной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask ServiceAsync(string message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись сервисной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    ValueTask ServiceAsync(string message, LogMode mode, ConsoleColor color) => ServiceAsync(message, mode, color, CancellationToken.None);

    /// <summary>
    /// Делает запись сервисной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask ServiceAsync(string message, LogMode mode, CancellationToken cancellationToken) => ServiceAsync(message, mode, ConsoleColor.Gray, cancellationToken);

    /// <summary>
    /// Делает запись сервисной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    ValueTask ServiceAsync(string message, LogMode mode) => ServiceAsync(message, mode, CancellationToken.None);

    /// <summary>
    /// Делает запись сервисной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask ServiceAsync(string message, CancellationToken cancellationToken) => ServiceAsync(message, LogMode.All, cancellationToken);

    /// <summary>
    /// Делает запись сервисной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    ValueTask ServiceAsync(string message) => ServiceAsync(message, CancellationToken.None);

    /// <summary>
    /// Делает запись сервисной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask ServiceAsync(string message, ConsoleColor color, CancellationToken cancellationToken) => ServiceAsync(message, LogMode.All, color, cancellationToken);

    /// <summary>
    /// Делает запись сервисной информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    ValueTask ServiceAsync(string message, ConsoleColor color) => ServiceAsync(message, color, CancellationToken.None);
}