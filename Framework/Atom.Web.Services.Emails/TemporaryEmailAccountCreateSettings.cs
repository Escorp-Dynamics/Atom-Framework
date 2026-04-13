namespace Atom.Web.Emails;

/// <summary>
/// Описывает пожелания к создаваемому временному почтовому аккаунту.
/// </summary>
public sealed class TemporaryEmailAccountCreateSettings
{
    /// <summary>
    /// Пустой запрос без ограничений.
    /// </summary>
    public static TemporaryEmailAccountCreateSettings Empty { get; } = new();

    /// <summary>
    /// Желаемый локальный идентификатор ящика.
    /// </summary>
    public string? Alias { get; init; }

    /// <summary>
    /// Желаемый домен временной почты.
    /// </summary>
    public string? Domain { get; init; }
}