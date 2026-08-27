using System.Runtime.CompilerServices;
using System.Buffers;

namespace Atom.Net.Https.Http2;

/// <summary>
/// Состояние одного потока HTTP/2 на стороне клиента.
/// </summary>
/// <remarks>
/// Каждый поток живёт независимо: заголовки приходят один раз, данные — произвольным числом
/// кадров, и оба события порождаются общим циклом чтения соединения. Ожидание построено на
/// <see cref="TaskCompletionSource"/>, а не на очереди: очередь на каждый поток стоила бы около
/// полутора килобайт и ничего не давала бы — тело всё равно возвращается целиком. Единственная
/// блокировка охраняет накопитель тела и держится ровно на время копирования одного кадра, так
/// что точкой сериализации всех потоков соединения она не становится.
///
/// Окно управления потоком меняется через <see cref="Interlocked"/> по той же причине: его
/// уменьшает отправитель, а увеличивает цикл чтения, и это разные потоки исполнения.
/// </remarks>
/// <param name="id">Идентификатор потока.</param>
/// <param name="initialWindowSize">Начальный размер окна отправки, объявленный сервером.</param>
public sealed class Http2StreamState(int id, int initialWindowSize)
{
    private readonly TaskCompletionSource<IReadOnlyList<KeyValuePair<string, string>>> headers =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource bodyCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock bodyLock = new();

    /// <summary>
    /// Тело, пришедшее ОДНИМ кадром.
    /// </summary>
    /// <remarks>
    /// Отдельный случай ради самого частого: перенаправления, ответы взамен кэша и почти всё
    /// прикладное укладываются в один кадр DATA, а немалая часть ответов тела не имеет вовсе.
    /// Здесь такой ответ стоит одного копирования и ни одного лишнего объекта, тогда как общий
    /// путь через накопитель стоил бы двух копирований и буфера, который выбрасывается следом.
    /// </remarks>
    private byte[]? singleChunk;

    /// <summary>Накопитель для тела, разбитого на несколько кадров.</summary>
    private ArrayBufferWriter<byte>? body;

    private int sendWindow = initialWindowSize;
    private int receiveWindow;

    /// <summary>Идентификатор потока.</summary>
    public int Id { get; } = id;

    /// <summary>Задача, завершающаяся при получении заголовков ответа.</summary>
    public Task<IReadOnlyList<KeyValuePair<string, string>>> Headers => headers.Task;

    /// <summary>Задача, завершающаяся после получения всего тела ответа.</summary>
    public Task BodyCompleted => bodyCompleted.Task;

    /// <summary>Текущий размер окна отправки.</summary>
    public int SendWindow => Volatile.Read(ref sendWindow);

    /// <summary>Сколько байт принято с последнего приращения окна приёма.</summary>
    public int ReceivedSinceUpdate => Volatile.Read(ref receiveWindow);

    /// <summary>
    /// Публикует заголовки ответа.
    /// </summary>
    /// <param name="value">Список заголовков.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CompleteHeaders(IReadOnlyList<KeyValuePair<string, string>> value) => headers.TrySetResult(value);

    /// <summary>
    /// Передаёт очередной блок данных ответа.
    /// </summary>
    /// <param name="chunk">Блок данных.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBody(ReadOnlyMemory<byte> chunk)
    {
        Interlocked.Add(ref receiveWindow, chunk.Length);

        lock (bodyLock)
        {
            // Копировать обязаны в любом случае: блок — срез буфера чтения соединения, который
            // будет переиспользован под следующий кадр ещё до того, как тело кому-то отдадут.
            if (body is not null)
            {
                body.Write(chunk.Span);
                return;
            }

            if (singleChunk is null)
            {
                singleChunk = chunk.ToArray();
                return;
            }

            // Пришёл второй кадр — только теперь заводим накопитель.
            body = new ArrayBufferWriter<byte>(singleChunk.Length + chunk.Length);
            body.Write(singleChunk);
            body.Write(chunk.Span);
            singleChunk = null;
        }
    }

    /// <summary>
    /// Возвращает накопленное тело ответа.
    /// </summary>
    /// <returns>Тело или пустой массив, если его не было.</returns>
    public byte[] TakeBody()
    {
        lock (bodyLock) return body is not null ? body.WrittenSpan.ToArray() : singleChunk ?? [];
    }

    /// <summary>
    /// Завершает поток нормально.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Complete()
    {
        // Заголовки могли не прийти вовсе (например, поток сброшен сервером). Оставлять ожидание
        // висящим нельзя: запрос завис бы до общего таймаута вместо внятной ошибки.
        headers.TrySetException(new InvalidOperationException("Поток HTTP/2 завершён без заголовков ответа"));
        bodyCompleted.TrySetResult();
    }

    /// <summary>
    /// Завершает поток ошибкой.
    /// </summary>
    /// <param name="error">Причина.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Fail(Exception error)
    {
        headers.TrySetException(error);
        bodyCompleted.TrySetException(error);
    }

    /// <summary>
    /// Закрывает канал данных после получения END_STREAM.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CompleteBody() => bodyCompleted.TrySetResult();

    /// <summary>
    /// Увеличивает окно отправки.
    /// </summary>
    /// <param name="increment">Приращение.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncreaseSendWindow(int increment) => Interlocked.Add(ref sendWindow, increment);

    /// <summary>
    /// Уменьшает окно отправки на размер отправленных данных.
    /// </summary>
    /// <param name="length">Число отправленных байт.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ConsumeSendWindow(int length) => Interlocked.Add(ref sendWindow, -length);

    /// <summary>
    /// Сбрасывает счётчик принятых байт, возвращая его прежнее значение.
    /// </summary>
    /// <returns>Число байт, принятых с прошлого приращения.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ExchangeReceived() => Interlocked.Exchange(ref receiveWindow, 0);
}
