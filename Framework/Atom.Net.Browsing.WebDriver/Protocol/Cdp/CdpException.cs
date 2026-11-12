using System.Globalization;

namespace Atom.Net.Browsing.WebDriver.Protocol.Cdp;

/// <summary>
/// Ошибка, которую вернул сервер протокола отладки в ответ на команду.
/// </summary>
internal sealed class CdpException : InvalidOperationException
{
    public CdpException()
    {
    }

    public CdpException(string? message)
        : base(message)
    {
    }

    public CdpException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public CdpException(string method, int code, string serverMessage)
        : base(string.Create(CultureInfo.InvariantCulture, $"Команда CDP '{method}' завершилась ошибкой {code}: {serverMessage}"))
    {
        Method = method;
        Code = code;
        ServerMessage = serverMessage;
    }

    /// <summary>Команда, вызвавшая ошибку.</summary>
    public string? Method { get; }

    /// <summary>Код ошибки протокола (JSON-RPC).</summary>
    public int Code { get; }

    /// <summary>Исходный текст ошибки сервера.</summary>
    public string? ServerMessage { get; }
}
