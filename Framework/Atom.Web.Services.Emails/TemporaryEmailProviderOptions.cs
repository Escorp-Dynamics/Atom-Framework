namespace Atom.Web.Emails;

/// <summary>
/// Общие настройки concrete provider для временной почты.
/// </summary>
public class TemporaryEmailProviderOptions
{
    /// <summary>
    /// Режим обновления списка доступных доменов.
    /// </summary>
    public TemporaryEmailDomainRefreshMode DomainRefreshMode { get; init; } = TemporaryEmailDomainRefreshMode.WhenEmpty;

    /// <summary>
    /// Префикс для автоматически создаваемого alias, если пользователь не передал Alias в request.
    /// </summary>
    public string GeneratedAliasPrefix { get; init; } = "atom";

    /// <summary>
    /// Длина случайного suffix для автоматически создаваемого alias.
    /// </summary>
    public int GeneratedAliasRandomLength { get; init; } = 12;

    /// <summary>
    /// Длина случайной части автоматически создаваемого пароля.
    /// </summary>
    public int GeneratedPasswordRandomLength { get; init; } = 32;

    /// <summary>
    /// Суффикс, добавляемый к автоматически создаваемому паролю.
    /// </summary>
    public string GeneratedPasswordSuffix { get; init; } = "Aa1!";
}