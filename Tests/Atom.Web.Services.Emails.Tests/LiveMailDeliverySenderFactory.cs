namespace Atom.Web.Emails.Tests;

internal static class LiveMailDeliverySenderFactory
{
    public const string SenderKindEnvVar = "ATOM_EMAIL_TEST_SENDER";
    public const string SmtpSenderKind = "smtp";
    public const string HttpSenderKind = "http";
    public const string ToolSphereSenderKind = "toolsphere";

    public static bool TryCreate(out ILiveMailDeliverySender? sender, out string? reason)
    {
        var senderKind = Environment.GetEnvironmentVariable(SenderKindEnvVar);
        if (string.IsNullOrWhiteSpace(senderKind))
        {
            senderKind = SmtpSenderKind;
        }

        if (string.Equals(senderKind, SmtpSenderKind, StringComparison.OrdinalIgnoreCase))
        {
            if (!LiveMailDeliverySmtpOptions.TryLoad(out var smtpOptions, out reason))
            {
                sender = null;
                return false;
            }

            sender = new LiveMailDeliverySmtpClient(smtpOptions!);
            reason = null;
            return true;
        }

        if (string.Equals(senderKind, HttpSenderKind, StringComparison.OrdinalIgnoreCase))
        {
            if (!LiveMailDeliveryHttpSenderOptions.TryLoad(out var httpOptions, out reason))
            {
                sender = null;
                return false;
            }

            sender = new LiveMailDeliveryHttpSender(httpOptions!);
            reason = null;
            return true;
        }

        if (string.Equals(senderKind, ToolSphereSenderKind, StringComparison.OrdinalIgnoreCase))
        {
            if (!LiveMailDeliveryToolSphereSenderOptions.TryLoad(out var toolSphereOptions, out reason))
            {
                sender = null;
                return false;
            }

            sender = new LiveMailDeliveryToolSphereSender(toolSphereOptions!);
            reason = null;
            return true;
        }

        sender = null;
        reason = $"Неизвестный тип отправителя '{senderKind}'. Поддерживаемые значения {SenderKindEnvVar}: {SmtpSenderKind}, {HttpSenderKind}, {ToolSphereSenderKind}.";
        return false;
    }
}