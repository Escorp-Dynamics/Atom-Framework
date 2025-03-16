namespace Atom.Web.Browsing.BiDi;

/// <summary>
/// Представляет данные, полученные из ответа.
/// </summary>
public class CommandResult
{
    /// <summary>
    /// Определяет, являются ли данные ответа ошибкой.
    /// </summary>
    public virtual bool IsError => false;

    /// <summary>
    /// Дополнительные данные, полученные в ответе.
    /// </summary>
    public ReceivedDataDictionary AdditionalData { get; internal set; } = ReceivedDataDictionary.Empty;
}