namespace Atom.Net.Tls;

/// <summary>
/// Партнёр закрыл соединение на границе записи TLS, не прислав close_notify.
/// </summary>
/// <remarks>
/// Отдельный тип нужен, чтобы отличить ВЕЖЛИВОЕ закрытие от повреждения обмена. На уровне записей
/// это два совершенно разных события, а прежде оба приходили одним <see cref="InvalidOperationException"/>
/// с разными строками: единственным способом их различить оставался разбор текста сообщения, то
/// есть способ, которым пользоваться нельзя.
///
/// Само по себе закрытие без close_notify — не поломка, а обыденность. Так поступают
/// балансировщики, серверы в стиле HTTP/1.0 и любой ответ, тело которого ограничено ИМЕННО
/// закрытием соединения: ни длины, ни кусочной передачи в нём нет, и конец данных объявляется
/// разрывом. Ответ вида «302 без Content-Length» — ровно такой случай, и его отдают, например,
/// корневые адреса craigslist.org и cisco.com.
///
/// Ошибка это или конец данных — решает тот, кто читает: рукопожатию оборванное соединение
/// говорит о неудаче, а прикладному чтению — о том, что данные кончились. Уровень записей такого
/// знания не имеет и поэтому лишь НАЗЫВАЕТ событие, а не судит о нём.
/// </remarks>
public sealed class TlsConnectionClosedException : InvalidOperationException
{
    /// <summary>
    /// Создаёт исключение.
    /// </summary>
    public TlsConnectionClosedException()
        : base("Разрыв соединения при чтении TLS") { }

    /// <summary>
    /// Создаёт исключение с заданным сообщением.
    /// </summary>
    /// <param name="message">Сообщение.</param>
    public TlsConnectionClosedException(string message) : base(message) { }

    /// <summary>
    /// Создаёт исключение с сообщением и причиной.
    /// </summary>
    /// <param name="message">Сообщение.</param>
    /// <param name="innerException">Причина.</param>
    public TlsConnectionClosedException(string message, Exception innerException) : base(message, innerException) { }
}
