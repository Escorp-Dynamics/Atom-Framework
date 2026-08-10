namespace Atom.Net.Browsing.WebDriver.Protocol;

/// <summary>
/// Сбой мостовой команды с сохранённой машинно-читаемой причиной.
/// </summary>
/// <remarks>
/// Наследуется от <see cref="InvalidOperationException"/>, чтобы не менять исключение,
/// наблюдаемое вызывающим кодом. Отдельный тип нужен потому, что классификация сбоя раньше
/// делалась подстрочным поиском по локализованному тексту сообщения и ломалась от любой
/// переформулировки.
/// </remarks>
internal sealed class BridgeCommandException : InvalidOperationException
{
    public BridgeCommandException()
    {
    }

    public BridgeCommandException(string? message)
        : base(message)
    {
    }

    public BridgeCommandException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public BridgeCommandException(
        string? message,
        BridgeStatus? status,
        string? errorCode = null,
        bool isSurfaceDisconnected = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Status = status;
        ErrorCode = errorCode;
        IsSurfaceDisconnected = isSurfaceDisconnected;
    }

    /// <summary>Статус мостового ответа, если команда дошла до вкладки.</summary>
    public BridgeStatus? Status { get; }

    /// <summary>Код ошибки протокола из <see cref="BridgeProtocolErrorCodes"/>, если он известен.</summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Вкладка или сеанс отсутствуют либо отключены. Такой сбой ожидаем при перезагрузке
    /// страницы и при закрытии вкладки, и вызывающий код может его поглотить или повторить.
    /// </summary>
    public bool IsSurfaceDisconnected { get; }

    /// <summary>
    /// <see langword="true"/>, если исключение сообщает об отключении мостовой поверхности.
    /// </summary>
    /// <remarks>
    /// Расширение сообщает об отключении вкладки двумя способами: статусом
    /// <see cref="BridgeStatus.Disconnected"/> и обычным <see cref="BridgeStatus.Error"/>
    /// с кодом <see cref="BridgeProtocolErrorCodes.TabDisconnected"/> — второй вариант приходит,
    /// когда команда дошла до фонового слоя, но канал вкладки к тому моменту уже закрылся.
    /// </remarks>
    public static bool IsSurfaceDisconnect(Exception exception)
    {
        if (exception is not BridgeCommandException bridgeException)
            return false;

        return bridgeException.IsSurfaceDisconnected
            || bridgeException.Status is BridgeStatus.Disconnected
            || bridgeException.ErrorCode is BridgeProtocolErrorCodes.TabDisconnected
                or BridgeProtocolErrorCodes.SessionDisconnected;
    }
}
