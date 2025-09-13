namespace Atom.Net.Https.Headers.HPack;

/// <summary>
/// Режим индексирования для конкретного заголовка при кодировании.
/// </summary>
public enum HPackIndexingMode : byte
{
    /// <summary>
    /// Литерально, без индексирования (без добавления в динамическую таблицу).
    /// </summary>
    WithoutIndexing = 0,
    /// <summary>
    /// Литерально, «никогда не индексировать» (cookie, authorization, и т.п.).
    /// </summary>
    NeverIndexed = 1,
    /// <summary>
    /// Литерально с инкрементальным индексированием (добавить в динамическую таблицу).
    /// </summary>
    Incremental = 2,
    /// <summary>
    /// Чисто индексированная форма (только индекс, если имя+значение уже есть в таблицах).
    /// </summary>
    Indexed = 3,
}