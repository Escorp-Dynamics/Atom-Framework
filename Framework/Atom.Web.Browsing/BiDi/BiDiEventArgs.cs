namespace Atom.Web.Browsing.BiDi;

/// <summary>
/// Представляет аргументы события для использования с событиями WebDriver Bidi.
/// </summary>
public class BiDiEventArgs : EventArgs
{
    /// <summary>
    /// Получает или задает дополнительные расширенные данные, отправленные с событием.
    /// </summary>
    public ReceivedDataDictionary AdditionalData { get; internal set; } = ReceivedDataDictionary.Empty;
}