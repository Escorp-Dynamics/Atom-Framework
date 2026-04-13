namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описывает запись в speechSynthesis.getVoices().
/// </summary>
public sealed class SpeechVoiceSettings
{
    /// <summary>
    /// Получает или задаёт имя голоса.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Получает или задаёт языковой тег BCP 47.
    /// </summary>
    public required string Lang { get; set; }

    /// <summary>
    /// Получает или задаёт voice URI.
    /// </summary>
    public Uri? VoiceUri { get; set; }

    /// <summary>
    /// Получает или задаёт признак локального голоса.
    /// </summary>
    public bool UseLocalService { get; set; } = true;

    /// <summary>
    /// Получает или задаёт признак голоса по умолчанию.
    /// </summary>
    public bool IsDefault { get; set; }
}