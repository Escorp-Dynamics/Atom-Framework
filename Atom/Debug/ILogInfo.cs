namespace Atom.Debug;

/// <summary>
/// Представляет базовый интерфейс для реализации данных записи журнала событий.
/// </summary>
public interface ILogInfo
{
    /// <summary>
    /// Уникальный идентификатор записи.
    /// </summary>
    long Id { get; }

    /// <summary>
    /// Исходное сообщение с форматированием.
    /// </summary>
    string SourceMessage { get; set; }

    /// <summary>
    /// Сообщение после форматирования.
    /// </summary>
    string Message { get; set; }

    /// <summary>
    /// Тип записи журнала.
    /// </summary>
    LogType Type { get; }

    /// <summary>
    /// Время создания сообщения.
    /// </summary>
    DateTime Time { get; }

    /// <summary>
    /// Связанные данные.
    /// </summary>
    object? Data { get; }
}

/// <summary>
/// Представляет базовый интерфейс для реализации данных записи журнала событий.
/// </summary>
/// <typeparam name="T">Тип связанных данных.</typeparam>
public interface ILogInfo<T> : ILogInfo
{
    /// <summary>
    /// Связанные данные.
    /// </summary>
    new T? Data { get; }
}