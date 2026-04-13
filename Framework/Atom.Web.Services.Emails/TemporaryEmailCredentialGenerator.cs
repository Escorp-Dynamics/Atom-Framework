using System.Security.Cryptography;
using Atom.Text;

namespace Atom.Web.Emails;

/// <summary>
/// Общая генерация alias и password для concrete provider временной почты.
/// </summary>
internal static class TemporaryEmailCredentialGenerator
{
    /// <summary>
    /// Нормализует пользовательский alias либо генерирует новый на основе общих provider options.
    /// </summary>
    public static string NormalizeAlias(string? alias, TemporaryEmailProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(alias))
        {
            return CreateGeneratedAlias(options);
        }

        using var builder = new ValueStringBuilder(alias.Length);
        foreach (var character in alias.Trim())
        {
            var normalized = char.ToLowerInvariant(character);
            if (char.IsAsciiLetterOrDigit(normalized) || normalized is '.' or '_' or '-')
            {
                builder.Append(normalized);
            }
        }

        var value = builder.ToString();
        return string.IsNullOrWhiteSpace(value)
            ? CreateGeneratedAlias(options)
            : value;
    }

    /// <summary>
    /// Создаёт новый пароль на основе общих provider options.
    /// </summary>
    public static string CreatePassword(TemporaryEmailProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return string.Concat(CreateRandomToken(options.GeneratedPasswordRandomLength), options.GeneratedPasswordSuffix);
    }

    private static string CreateGeneratedAlias(TemporaryEmailProviderOptions options)
        => string.Concat(options.GeneratedAliasPrefix, CreateRandomToken(options.GeneratedAliasRandomLength));

    private static string CreateRandomToken(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        var bytes = RandomNumberGenerator.GetBytes(length);
        return Convert.ToHexString(bytes).ToLowerInvariant()[..length];
    }
}