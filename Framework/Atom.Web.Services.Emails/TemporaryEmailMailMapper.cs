using System.Security.Cryptography;
using System.Text;
using Atom.Text;

namespace Atom.Web.Emails;

/// <summary>
/// Общий mapper для преобразования upstream почтовых моделей в нормализованный IMail surface.
/// </summary>
internal static class TemporaryEmailMailMapper
{
    /// <summary>
    /// Создаёт нормализованную mail-модель из upstream данных.
    /// </summary>
    public static TMail Map<TRecipient, TMail>(
        string upstreamId,
        string? fromAddress,
        string? fromName,
        IEnumerable<TRecipient>? recipients,
        Func<TRecipient, string?> recipientAddressSelector,
        Func<TRecipient, string?> recipientNameSelector,
        string? subject,
        string? body,
        bool isRead,
        Func<string, Guid, string, string, string, string, bool, TMail> factory)
        where TMail : IMail
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(upstreamId);
        ArgumentNullException.ThrowIfNull(recipientAddressSelector);
        ArgumentNullException.ThrowIfNull(recipientNameSelector);
        ArgumentNullException.ThrowIfNull(factory);

        return factory(
            upstreamId,
            CreateStableId(upstreamId),
            ResolveAddress(fromAddress, fromName),
            JoinAddresses(recipients, recipientAddressSelector, recipientNameSelector),
            subject ?? string.Empty,
            body ?? string.Empty,
            isRead);
    }

    public static Guid CreateStableId(string upstreamId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(upstreamId));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }

    private static string ResolveAddress(string? address, string? name)
    {
        if (!string.IsNullOrWhiteSpace(address))
        {
            return address;
        }

        return name ?? string.Empty;
    }

    private static string JoinAddresses<TRecipient>(
        IEnumerable<TRecipient>? recipients,
        Func<TRecipient, string?> recipientAddressSelector,
        Func<TRecipient, string?> recipientNameSelector)
    {
        if (recipients is null)
        {
            return string.Empty;
        }

        var values = recipients
            .Select(recipient => ResolveAddress(recipientAddressSelector(recipient), recipientNameSelector(recipient)))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (values.Length == 0)
        {
            return string.Empty;
        }

        using var builder = new ValueStringBuilder(values.Sum(static value => value.Length) + (values.Length - 1) * 2);
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(';');
                builder.Append(' ');
            }

            builder.Append(values[index]);
        }

        return builder.ToString();
    }
}