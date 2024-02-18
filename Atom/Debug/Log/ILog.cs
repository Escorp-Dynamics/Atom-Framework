namespace Atom.Debug;

/// <summary>
/// Представляет базовый интерфейс для реализации журналов событий.
/// </summary>
public partial interface ILog : IAsyncDisposable
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
    /// Происходит в момент записи в журнал.
    /// </summary>
    event AsyncEventHandler<ILog, LogEventArgs>? Writting;

    #region Обычная запись

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="info">Сообщение.</param>
    /// <param name="mode">Режим записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteAsync<T>(ILogInfo<T> info, LogMode mode, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="info">Сообщение.</param>
    /// <param name="mode">Режим записи.</param>
    /// <returns></returns>
    ValueTask WriteAsync<T>(ILogInfo<T> info, LogMode mode) => WriteAsync(info, mode, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="info">Сообщение.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteAsync<T>(ILogInfo<T> info, CancellationToken cancellationToken) => WriteAsync(info, LogMode.All, cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="info">Сообщение.</param>
    /// <returns></returns>
    ValueTask WriteAsync<T>(ILogInfo<T> info) => WriteAsync(info, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанный данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="type">Тип сообщения.</param>
    /// <param name="mode">Режим записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteAsync<T>(string message, LogType type, LogMode mode, T? data, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанный данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="type">Тип сообщения.</param>
    /// <param name="mode">Режим записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask WriteAsync<T>(string message, LogType type, LogMode mode, T? data) => WriteAsync(message, type, mode, data, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанный данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="type">Тип сообщения.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteAsync<T>(string message, LogType type, T? data, CancellationToken cancellationToken) => WriteAsync(message, type, LogMode.All, data, cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанный данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="type">Тип сообщения.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask WriteAsync<T>(string message, LogType type, T? data) => WriteAsync(message, type, data, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанный данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteAsync<T>(string message, LogMode mode, T? data, CancellationToken cancellationToken) => WriteAsync(message, LogType.Info, mode, data, cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанный данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask WriteAsync<T>(string message, LogMode mode, T? data) => WriteAsync(message, mode, data, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанный данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteAsync<T>(string message, T? data, CancellationToken cancellationToken) => WriteAsync(message, LogMode.All, data, cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанный данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask WriteAsync<T>(string message, T? data) => WriteAsync(message, data, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="type">Тип сообщения.</param>
    /// <param name="mode">Режим записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteAsync(string message, LogType type, LogMode mode, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="type">Тип сообщения.</param>
    /// <param name="mode">Режим записи.</param>
    /// <returns></returns>
    ValueTask WriteAsync(string message, LogType type, LogMode mode) => WriteAsync(message, type, mode, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="type">Тип сообщения.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteAsync(string message, LogType type, CancellationToken cancellationToken) => WriteAsync(message, type, LogMode.All, cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="type">Тип сообщения.</param>
    /// <returns></returns>
    ValueTask WriteAsync(string message, LogType type) => WriteAsync(message, type, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteAsync(string message, LogMode mode, CancellationToken cancellationToken) => WriteAsync(message, LogType.Info, mode, cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи.</param>
    /// <returns></returns>
    ValueTask WriteAsync(string message, LogMode mode) => WriteAsync(message, mode, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteAsync(string message, CancellationToken cancellationToken) => WriteAsync(message, LogMode.All, cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <returns></returns>
    ValueTask WriteAsync(string message) => WriteAsync(message, CancellationToken.None);

    #endregion

    #region Однострочная запись

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="info">Сообщение.</param>
    /// <param name="mode">Режим записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync<T>(ILogInfo<T> info, LogMode mode, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="info">Сообщение.</param>
    /// <param name="mode">Режим записи.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync<T>(ILogInfo<T> info, LogMode mode) => WriteLineAsync(info, mode, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="info">Сообщение.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync<T>(ILogInfo<T> info, CancellationToken cancellationToken) => WriteLineAsync(info, LogMode.All, cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="info">Сообщение.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync<T>(ILogInfo<T> info) => WriteLineAsync(info, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанный данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="type">Тип сообщения.</param>
    /// <param name="mode">Режим записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync<T>(string message, LogType type, LogMode mode, T? data, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанный данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="type">Тип сообщения.</param>
    /// <param name="mode">Режим записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync<T>(string message, LogType type, LogMode mode, T? data) => WriteLineAsync(message, type, mode, data, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанный данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="type">Тип сообщения.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync<T>(string message, LogType type, T? data, CancellationToken cancellationToken) => WriteLineAsync(message, type, LogMode.All, data, cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанный данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="type">Тип сообщения.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync<T>(string message, LogType type, T? data) => WriteLineAsync(message, type, data, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанный данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync<T>(string message, LogMode mode, T? data, CancellationToken cancellationToken) => WriteLineAsync(message, LogType.Info, mode, data, cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанный данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync<T>(string message, LogMode mode, T? data) => WriteLineAsync(message, mode, data, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанный данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync<T>(string message, T? data, CancellationToken cancellationToken) => WriteLineAsync(message, LogMode.All, data, cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанный данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync<T>(string message, T? data) => WriteLineAsync(message, data, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="type">Тип сообщения.</param>
    /// <param name="mode">Режим записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync(string message, LogType type, LogMode mode, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="type">Тип сообщения.</param>
    /// <param name="mode">Режим записи.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync(string message, LogType type, LogMode mode) => WriteLineAsync(message, type, mode, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="type">Тип сообщения.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync(string message, LogType type, CancellationToken cancellationToken) => WriteLineAsync(message, type, LogMode.All, cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="type">Тип сообщения.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync(string message, LogType type) => WriteLineAsync(message, type, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync(string message, LogMode mode, CancellationToken cancellationToken) => WriteLineAsync(message, LogType.Info, mode, cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync(string message, LogMode mode) => WriteLineAsync(message, mode, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync(string message, CancellationToken cancellationToken) => WriteLineAsync(message, LogMode.All, cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync(string message) => WriteLineAsync(message, CancellationToken.None);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask WriteLineAsync(CancellationToken cancellationToken) => WriteLineAsync(string.Empty, cancellationToken);

    /// <summary>
    /// Делает запись сообщения в журнал.
    /// </summary>
    /// <returns></returns>
    ValueTask WriteLineAsync() => WriteLineAsync(CancellationToken.None);

    #endregion

    ValueTask ResetLineAsync(int offset, CancellationToken cancellationToken);

    ValueTask ResetLineAsync(int offset) => ResetLineAsync(offset, CancellationToken.None);

    ValueTask ResetLineAsync(CancellationToken cancellationToken) => ResetLineAsync(0, cancellationToken);

    ValueTask ResetLineAsync() => ResetLineAsync(CancellationToken.None);
}