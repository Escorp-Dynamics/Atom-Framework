namespace Atom.Web.Emails.Tests;

internal sealed class LiveMailDeliverySmtpOptions
{
    public const string HostEnvVar = "ATOM_EMAIL_TEST_SMTP_HOST";
    public const string PortEnvVar = "ATOM_EMAIL_TEST_SMTP_PORT";
    public const string EnableSslEnvVar = "ATOM_EMAIL_TEST_SMTP_SSL";
    public const string UserNameEnvVar = "ATOM_EMAIL_TEST_SMTP_USERNAME";
    public const string PasswordEnvVar = "ATOM_EMAIL_TEST_SMTP_PASSWORD";
    public const string FromAddressEnvVar = "ATOM_EMAIL_TEST_SMTP_FROM";
    public const string FromDisplayNameEnvVar = "ATOM_EMAIL_TEST_SMTP_FROM_NAME";

    public required string Host { get; init; }

    public required int Port { get; init; }

    public required bool EnableSsl { get; init; }

    public string? UserName { get; init; }

    public string? Password { get; init; }

    public required string FromAddress { get; init; }

    public string? FromDisplayName { get; init; }

    public static bool TryLoad(out LiveMailDeliverySmtpOptions? options, out string? reason)
    {
        var host = Environment.GetEnvironmentVariable(HostEnvVar);
        if (string.IsNullOrWhiteSpace(host))
        {
            options = null;
            reason = $"Не задана переменная окружения {HostEnvVar}.";
            return false;
        }

        var fromAddress = Environment.GetEnvironmentVariable(FromAddressEnvVar);
        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            options = null;
            reason = $"Не задана переменная окружения {FromAddressEnvVar}.";
            return false;
        }

        var portValue = Environment.GetEnvironmentVariable(PortEnvVar);
        if (!int.TryParse(portValue, out var port) || port <= 0)
        {
            options = null;
            reason = $"Переменная окружения {PortEnvVar} должна содержать корректный TCP-порт SMTP.";
            return false;
        }

        var sslValue = Environment.GetEnvironmentVariable(EnableSslEnvVar);
        var enableSsl = string.Equals(sslValue, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sslValue, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sslValue, "yes", StringComparison.OrdinalIgnoreCase);

        options = new LiveMailDeliverySmtpOptions
        {
            Host = host,
            Port = port,
            EnableSsl = enableSsl,
            UserName = Environment.GetEnvironmentVariable(UserNameEnvVar),
            Password = Environment.GetEnvironmentVariable(PasswordEnvVar),
            FromAddress = fromAddress,
            FromDisplayName = Environment.GetEnvironmentVariable(FromDisplayNameEnvVar),
        };

        reason = null;
        return true;
    }
}