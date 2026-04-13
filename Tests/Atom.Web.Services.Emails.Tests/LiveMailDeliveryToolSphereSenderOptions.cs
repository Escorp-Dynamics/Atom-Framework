namespace Atom.Web.Emails.Tests;

internal sealed class LiveMailDeliveryToolSphereSenderOptions
{
    public const string ApiKeyEnvVar = "ATOM_EMAIL_TEST_TOOLSPHERE_API_KEY";
    public const string SenderNameEnvVar = "ATOM_EMAIL_TEST_TOOLSPHERE_NAME";

    public required string ApiKey { get; init; }

    public required string SenderName { get; init; }

    public static bool TryLoad(out LiveMailDeliveryToolSphereSenderOptions? options, out string? reason)
    {
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            options = null;
            reason = $"Не задана переменная окружения {ApiKeyEnvVar}.";
            return false;
        }

        var senderName = Environment.GetEnvironmentVariable(SenderNameEnvVar);
        if (string.IsNullOrWhiteSpace(senderName))
        {
            senderName = "AtomTests";
        }

        options = new LiveMailDeliveryToolSphereSenderOptions
        {
            ApiKey = apiKey,
            SenderName = senderName,
        };

        reason = null;
        return true;
    }
}