namespace Atom.Debug;

public partial interface ILog
{
    /// <summary>
    /// Делает запись информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask InfoAsync<T>(string message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask InfoAsync<T>(string message, LogMode mode, ConsoleColor color, T? data) => InfoAsync(message, mode, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask InfoAsync<T>(string message, LogMode mode, T? data, CancellationToken cancellationToken) => InfoAsync(message, mode, ConsoleColor.White, data, cancellationToken);

    /// <summary>
    /// Делает запись информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask InfoAsync<T>(string message, LogMode mode, T? data) => InfoAsync(message, mode, data, CancellationToken.None);

    /// <summary>
    /// Делает запись информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask InfoAsync<T>(string message, T? data, CancellationToken cancellationToken) => InfoAsync(message, LogMode.All, data, cancellationToken);

    /// <summary>
    /// Делает запись информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask InfoAsync<T>(string message, T? data) => InfoAsync(message, data, CancellationToken.None);

    /// <summary>
    /// Делает запись информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask InfoAsync<T>(string message, ConsoleColor color, T? data, CancellationToken cancellationToken) => InfoAsync(message, LogMode.All, color, data, cancellationToken);

    /// <summary>
    /// Делает запись информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask InfoAsync<T>(string message, ConsoleColor color, T? data) => InfoAsync(message, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask InfoAsync(string message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <returns></returns>
    ValueTask InfoAsync(string message, LogMode mode, ConsoleColor color) => InfoAsync(message, mode, color, CancellationToken.None);

    /// <summary>
    /// Делает запись информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask InfoAsync(string message, LogMode mode, CancellationToken cancellationToken) => InfoAsync(message, mode, ConsoleColor.White, cancellationToken);

    /// <summary>
    /// Делает запись информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <returns></returns>
    ValueTask InfoAsync(string message, LogMode mode) => InfoAsync(message, mode, CancellationToken.None);

    /// <summary>
    /// Делает запись информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask InfoAsync(string message, CancellationToken cancellationToken) => InfoAsync(message, LogMode.All, cancellationToken);

    /// <summary>
    /// Делает запись информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <returns></returns>
    ValueTask InfoAsync(string message) => InfoAsync(message, CancellationToken.None);

    /// <summary>
    /// Делает запись информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask InfoAsync(string message, ConsoleColor color, CancellationToken cancellationToken) => InfoAsync(message, LogMode.All, color, cancellationToken);

    /// <summary>
    /// Делает запись информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <returns></returns>
    ValueTask InfoAsync(string message, ConsoleColor color) => InfoAsync(message, color, CancellationToken.None);
}