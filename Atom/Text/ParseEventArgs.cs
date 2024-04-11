namespace Atom.Text;

/// <summary>
/// Представляет аргументы события парсинга строковых данных.
/// </summary>
/// <param name="origin">Исходная строка.</param>
public class ParseEventArgs(string origin) : AsyncEventArgs
{
    /// <summary>
    /// Исходная строка.
    /// </summary>
    public string Origin { get; protected set; } = origin;

    /// <summary>
    /// Указывает, является ли исходная строка валидной.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Указывает, была ли процедура парсинга завершена.
    /// </summary>
    public bool IsParsed { get; set; }
}