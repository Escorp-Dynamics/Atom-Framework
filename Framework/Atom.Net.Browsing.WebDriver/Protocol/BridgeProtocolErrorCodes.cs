namespace Atom.Net.Browsing.WebDriver.Protocol;

internal static class BridgeProtocolErrorCodes
{
    public const string InvalidPayload = "неверные-данные";
    public const string MissingSecret = "отсутствует-секрет";
    public const string SecretMismatch = "секрет-не-совпадает";
    public const string MissingSessionId = "отсутствует-идентификатор-сеанса";
    public const string DuplicateSessionId = "идентификатор-сеанса-уже-занят";
    public const string UnsupportedProtocolVersion = "версия-протокола-не-поддерживается";
    public const string SessionDisconnected = "сеанс-отключён";
    public const string TabDisconnected = "вкладка-отключена";
    public const string RequestTimeout = "время-ожидания-истекло";
    public const string RequestCanceled = "запрос-отменён";
    public const string SendFailed = "отправка-не-выполнена";
    public const string Closing = "закрытие";
}