namespace Atom.Net.Https.Connections;

/// <summary>
/// Партнёр закрыл соединение раньше, чем прислал хоть что-то в ответ.
/// </summary>
/// <remarks>
/// ★ Отдельный тип нужен ради ОДНОГО решения: такой запрос можно повторить. Ответа не было
/// вовсе — значит и обработки на той стороне не произошло, и повтор не грозит повторным
/// действием.
///
/// Случай этот не редкий, а неизбежный для долгоживущего клиента: соединение берётся из пула, а
/// партнёр закрыл его секунду назад по своему сроку простоя (у крупных площадок он измеряется
/// единицами секунд). Проверка живости перед выдачей сужает окно, но не закрывает: между
/// проверкой и отправкой всегда остаётся зазор.
///
/// Замер на четырёх волнах по одним и тем же живым узлам с паузой между ними: первая волна дала
/// 2 отказа из 277, последующие — по 15–19, и все с этой причиной. Среди пострадавших
/// <c lang="text">amazon.com</c>, <c lang="text">adobe.io</c>, <c lang="text">aliyuncs.com</c> — то есть площадки, к которым
/// обращаются постоянно.
///
/// Отличать это по тексту сообщения было бы нельзя: тексты меняются, а решение о повторе
/// зависит от точного смысла отказа.
/// </remarks>
public sealed class HttpsIdleConnectionClosedException : InvalidOperationException
{
    /// <summary>Создаёт исключение.</summary>
    public HttpsIdleConnectionClosedException() : base("Партнёр закрыл соединение, не прислав ответа") { }

    /// <summary>Создаёт исключение с описанием.</summary>
    /// <param name="message">Описание.</param>
    public HttpsIdleConnectionClosedException(string message) : base(message) { }

    /// <summary>Создаёт исключение с описанием и причиной.</summary>
    /// <param name="message">Описание.</param>
    /// <param name="innerException">Причина.</param>
    public HttpsIdleConnectionClosedException(string message, Exception innerException) : base(message, innerException) { }
}
