using Atom.Text;

namespace Atom.Web.Emails;

/// <summary>
/// Общие утилиты для работы с временными почтовыми адресами.
/// </summary>
internal static class TemporaryEmailAddressUtility
{
    /// <summary>
    /// Собирает полный адрес из alias и domain.
    /// </summary>
    public static string Compose(string alias, string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        using var builder = new ValueStringBuilder(alias.Length + domain.Length + 1);
        builder.Append(alias);
        builder.Append('@');
        builder.Append(domain);
        return builder.ToString();
    }

    /// <summary>
    /// Извлекает логин из полного адреса.
    /// </summary>
    public static string ExtractUserName(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        var separatorIndex = address.IndexOf('@', StringComparison.Ordinal);
        return separatorIndex > 0 ? address[..separatorIndex] : address;
    }

    /// <summary>
    /// Извлекает доменную часть адреса после символа @.
    /// </summary>
    public static string ExtractDomain(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        var separatorIndex = address.IndexOf('@', StringComparison.Ordinal);
        return separatorIndex >= 0 && separatorIndex < address.Length - 1
            ? address[(separatorIndex + 1)..]
            : string.Empty;
    }
}