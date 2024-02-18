namespace Atom.Debug;

/// <summary>
/// Режим записи журнала событий.
/// </summary>
[Flags]
public enum LogMode
{
    /// <summary>
    /// Запись отключена.
    /// </summary>
    None = 0,
    /// <summary>
    /// Вывод записей в консоль.
    /// </summary>
    Console = 1,
    /// <summary>
    /// Вывод записей в файл.
    /// </summary>
    File = 2,
    /// <summary>
    /// Вывод записей в базу данных.
    /// </summary>
    Database = 4,
    /// <summary>
    /// Вывод записей везде.
    /// </summary>
    All = Console | File | Database,
}