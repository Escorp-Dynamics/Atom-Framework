namespace Atom.Debug;

public partial interface ILog
{
    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask CriticalAsync<T>(string message, LogMode mode, ConsoleColor color, T? data, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask CriticalAsync<T>(string message, LogMode mode, ConsoleColor color, T? data) => CriticalAsync(message, mode, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask CriticalAsync<T>(string message, LogMode mode, T? data, CancellationToken cancellationToken) => CriticalAsync(message, mode, ConsoleColor.Magenta, data, cancellationToken);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask CriticalAsync<T>(string message, LogMode mode, T? data) => CriticalAsync(message, mode, data, CancellationToken.None);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask CriticalAsync<T>(string message, T? data, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, data, cancellationToken);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask CriticalAsync<T>(string message, T? data) => CriticalAsync(message, data, CancellationToken.None);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask CriticalAsync<T>(string message, ConsoleColor color, T? data, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, color, data, cancellationToken);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <typeparam name="T">Тип связанных данных.</typeparam>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="data">Связанные данные.</param>
    /// <returns></returns>
    ValueTask CriticalAsync<T>(string message, ConsoleColor color, T? data) => CriticalAsync(message, color, data, CancellationToken.None);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask CriticalAsync(string message, LogMode mode, ConsoleColor color, Exception ex, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <returns></returns>
    ValueTask CriticalAsync(string message, LogMode mode, ConsoleColor color, Exception ex) => CriticalAsync(message, mode, color, ex, CancellationToken.None);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask CriticalAsync(string message, LogMode mode, Exception ex, CancellationToken cancellationToken) => CriticalAsync(message, mode, ConsoleColor.Magenta, ex, cancellationToken);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <returns></returns>
    ValueTask CriticalAsync(string message, LogMode mode, Exception ex) => CriticalAsync(message, mode, ex, CancellationToken.None);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask CriticalAsync(string message, Exception ex, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, ex, cancellationToken);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <returns></returns>
    ValueTask CriticalAsync(string message, Exception ex) => CriticalAsync(message, ex, CancellationToken.None);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask CriticalAsync(string message, ConsoleColor color, Exception ex, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, color, ex, cancellationToken);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="ex">Информация об ошибке.</param>
    /// <returns></returns>
    ValueTask CriticalAsync(string message, ConsoleColor color, Exception ex) => CriticalAsync(message, color, ex, CancellationToken.None);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask CriticalAsync(string message, LogMode mode, ConsoleColor color, CancellationToken cancellationToken);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <returns></returns>
    ValueTask CriticalAsync(string message, LogMode mode, ConsoleColor color) => CriticalAsync(message, mode, color, CancellationToken.None);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask CriticalAsync(string message, LogMode mode, CancellationToken cancellationToken) => CriticalAsync(message, mode, ConsoleColor.Magenta, cancellationToken);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="mode">Режим записи в журнал.</param>
    /// <returns></returns>
    ValueTask CriticalAsync(string message, LogMode mode) => CriticalAsync(message, mode, CancellationToken.None);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask CriticalAsync(string message, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, cancellationToken);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <returns></returns>
    ValueTask CriticalAsync(string message) => CriticalAsync(message, CancellationToken.None);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    ValueTask CriticalAsync(string message, ConsoleColor color, CancellationToken cancellationToken) => CriticalAsync(message, LogMode.All, color, cancellationToken);

    /// <summary>
    /// Делает запись критической информации в журнал.
    /// </summary>
    /// <param name="message">Сообщение журнала.</param>
    /// <param name="color">Цвет текста записи.</param>
    /// <returns></returns>
    ValueTask CriticalAsync(string message, ConsoleColor color) => CriticalAsync(message, color, CancellationToken.None);
}