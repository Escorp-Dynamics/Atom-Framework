namespace Atom.Debug;

public partial interface ILog
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
    /// <returns></returns>
    ValueTask WarningAsync<T>(string message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask WarningAsync<T>(string message, LogMode mode, ConsoleColor color, T? data) => WarningAsync(message, mode, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask WarningAsync<T>(string message, LogMode mode, T? data, CancellationToken cancellationToken) => WarningAsync(message, mode, ConsoleColor.Yellow, data, cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask WarningAsync<T>(string message, LogMode mode, T? data) => WarningAsync(message, mode, data, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WarningAsync<T>(string message, T? data, CancellationToken cancellationToken) => WarningAsync(message, LogMode.All, data, cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask WarningAsync<T>(string message, T? data) => WarningAsync(message, data, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WarningAsync<T>(string message, ConsoleColor color, T? data, CancellationToken cancellationToken) => WarningAsync(message, LogMode.All, color, data, cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask WarningAsync<T>(string message, ConsoleColor color, T? data) => WarningAsync(message, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WarningAsync(string message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <returns></returns>
    ValueTask WarningAsync(string message, LogMode mode, ConsoleColor color) => WarningAsync(message, mode, color, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WarningAsync(string message, LogMode mode, CancellationToken cancellationToken) => WarningAsync(message, mode, ConsoleColor.Yellow, cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <returns></returns>
    ValueTask WarningAsync(string message, LogMode mode) => WarningAsync(message, mode, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WarningAsync(string message, CancellationToken cancellationToken) => WarningAsync(message, LogMode.All, cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <returns></returns>
    ValueTask WarningAsync(string message) => WarningAsync(message, CancellationToken.None);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WarningAsync(string message, ConsoleColor color, CancellationToken cancellationToken) => WarningAsync(message, LogMode.All, color, cancellationToken);

    /// <summary>
    /// Делает запись предупреждений в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <returns></returns>
    ValueTask WarningAsync(string message, ConsoleColor color) => WarningAsync(message, color, CancellationToken.None);
}