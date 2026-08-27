using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Atom.Net.Https;

namespace Atom.Net.Tests.Https;

/// <summary>
/// Проверяет повтор запроса, который заведомо не был обработан сервером.
/// </summary>
/// <remarks>
/// ★ Повторов не было вовсе, и это самый заметный пробел в надёжности долгоживущего клиента.
/// Соединение из пула партнёр вправе закрыть в любой миг — у балансировщиков и обратных прокси
/// срок простоя измеряется единицами секунд. Проверка живости перед выдачей сужает окно, но
/// закрыть его нельзя: между проверкой и отправкой всегда остаётся зазор, и попасть в него тем
/// вероятнее, чем больше запросов проходит через клиент.
///
/// Повторяется ТОЛЬКО то, что не дошло до обработки. Ответ сервера — любой, даже с кодом
/// ошибки — не повторяется никогда: за ним уже стоит выполненное действие.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class RequestRetryTests
{
    private const int TestTimeoutMs = 30000;

    [Test]
    public async Task ClosedConnectionIsRetriedOnAFreshOne()
    {
        // Первое соединение обрывается БЕЗ ответа — ровно так выглядит соединение, которое
        // партнёр закрыл, пока оно ждало в пуле. Второе отвечает нормально.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var attempts = 0;
        var server = Task.Run(async () =>
        {
            while (true)
            {
                var socket = await listener.AcceptSocketAsync().ConfigureAwait(false);
                var attempt = Interlocked.Increment(ref attempts);

                using (socket)
                {
                    using var stream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);

                    if (attempt is 1)
                    {
                        // Рвём, не читая запрос: сервер о нём не узнал.
                        socket.Close();
                        continue;
                    }

                    await ReadRequestHeadAsync(stream).ConfigureAwait(false);
                    await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray()).ConfigureAwait(false);
                }

                return;
            }
        });

        using var handler = new HttpsClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/").ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        await server.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(200), "запрос обязан пройти со второй попытки");
            Assert.That(body, Is.EqualTo("ok"));
            Assert.That(attempts, Is.EqualTo(2), "попыток должно быть ровно две");
        });
    }

    [Test]
    public async Task ServerErrorIsNotRetried()
    {
        // ★ Обратная сторона: ОТВЕТ сервера, даже с кодом ошибки, повторять нельзя. За ним уже
        // стоит выполненная работа, и повтор означал бы её удвоение.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var attempts = 0;
        var server = Task.Run(async () =>
        {
            var socket = await listener.AcceptSocketAsync().ConfigureAwait(false);
            Interlocked.Increment(ref attempts);

            using (socket)
            {
                using var stream = new System.Net.Sockets.NetworkStream(socket, ownsSocket: false);

                await ReadRequestHeadAsync(stream).ConfigureAwait(false);
                await stream.WriteAsync("HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray()).ConfigureAwait(false);
            }
        });

        using var handler = new HttpsClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/").ConfigureAwait(false);

        await server.ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(503));
            Assert.That(attempts, Is.EqualTo(1), "ответ сервера повторять нельзя");
        });
    }

    /// <summary>
    /// Дочитывает заголовок запроса до пустой строки.
    /// </summary>
    /// <param name="stream">Поток соединения.</param>
    /// <returns>Задача чтения.</returns>
    private static async Task ReadRequestHeadAsync(Stream stream)
    {
        var buffer = new byte[4096];
        var written = 0;

        while (written < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(written)).ConfigureAwait(false);
            if (read <= 0) return;

            written += read;

            if (Encoding.ASCII.GetString(buffer, 0, written).Contains("\r\n\r\n", StringComparison.Ordinal)) return;
        }
    }
}
