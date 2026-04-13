namespace Atom.Web.Emails.Tests;

internal sealed class LiveMailDeliveryHttpSenderOptions
{
    public const string EndpointEnvVar = "ATOM_EMAIL_TEST_HTTP_ENDPOINT";
    public const string AuthorizationEnvVar = "ATOM_EMAIL_TEST_HTTP_AUTHORIZATION";
    public const string FromAddressEnvVar = "ATOM_EMAIL_TEST_HTTP_FROM";
    public const string FromDisplayNameEnvVar = "ATOM_EMAIL_TEST_HTTP_FROM_NAME";

    public required string Endpoint { get; init; }

    public string? Authorization { get; init; }

    public string? FromAddress { get; init; }

    public string? FromDisplayName { get; init; }

    public static bool TryLoad(out LiveMailDeliveryHttpSenderOptions? options, out string? reason)
    {
        var endpoint = Environment.GetEnvironmentVariable(EndpointEnvVar);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            options = null;
            reason = $"Не задана переменная окружения {EndpointEnvVar}.";
            return false;
        }

        options = new LiveMailDeliveryHttpSenderOptions
        {
            Endpoint = endpoint,
            Authorization = Environment.GetEnvironmentVariable(AuthorizationEnvVar),
            FromAddress = Environment.GetEnvironmentVariable(FromAddressEnvVar),
            FromDisplayName = Environment.GetEnvironmentVariable(FromDisplayNameEnvVar),
        };

        reason = null;
        return true;
    }
}