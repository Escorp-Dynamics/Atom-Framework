using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Atom.Architect.Builders;
using Atom.Buffers;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tls;

/// <summary>
/// Генератор ClientHello-сообщения TLS.
/// </summary>
public partial class ClientHelloBuilder : IBuilder<ReadOnlySpan<byte>, ClientHelloBuilder>
{
    /// <summary>
    /// Начальный размер буфера сообщения.
    /// </summary>
    /// <remarks>
    /// Замер нынешних профилей: Chrome 1796 байт, Firefox 1875 — запас двукратный. Но запас
    /// кончается незаметно: постквантовая доля ключа занимает больше килобайта, и любое
    /// расширение состава — новая доля, тикет возобновления, длинное имя узла — способно вывести
    /// сообщение за предел. Поэтому буфер РАСТЁТ, а не обрывает сборку: фиксированный размер
    /// здесь означал бы жёсткий отказ на ровном месте.
    /// </remarks>
    private const int InitialBufferSize = 4096;

    private byte[] buffer = new byte[InitialBufferSize];

    private readonly List<ushort> cipherSuites = [];
    private readonly List<ITlsExtension> extensions = [];
    private SessionIdPolicy sessionIdPolicy;
    private bool useVersionFallback;

    /// <summary>Закреплённое случайное число: непусто только для повторного приветствия.</summary>
    private ReadOnlyMemory<byte> fixedRandom;

    /// <summary>Закреплённый идентификатор сессии: непусто только для повторного приветствия.</summary>
    private ReadOnlyMemory<byte> fixedSessionId;

    private bool IsGreaseCipherSuitesEnabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            foreach (var extension in extensions)
            {
                if (extension is GreaseTlsExtension) return true;
            }

            return default;
        }
    }

    /// <summary>
    /// Нужно ли добавлять сигнальный набор TLS_EMPTY_RENEGOTIATION_INFO_SCSV.
    /// </summary>
    /// <remarks>
    /// По RFC 5746 клиент сообщает о поддержке защищённого пересогласования ЛИБО этим сигнальным
    /// набором, ЛИБО расширением renegotiation_info — но не обоими сразу. Браузеры отправляют
    /// расширение, поэтому при его наличии сигнальный набор лишний: он добавляет в список шифров
    /// значение, которого у браузера нет, и меняет отпечаток.
    ///
    /// ★ Когда предлагается ТОЛЬКО TLS 1.3, не нужно ни то, ни другое: пересогласования в этой
    /// версии нет как понятия. Замечено на рукопожатии поверх QUIC, где движки Chromium убирают
    /// renegotiation_info, — сигнальный набор тут же появлялся сам и давал четвёртый набор шифров
    /// там, где настоящий Chrome отправляет ровно три.
    /// </remarks>
    private bool IsRenegotiationScsvRequired
    {
        get
        {
            foreach (var extension in extensions)
            {
                if (extension is RenegotiationInfoTlsExtension) return false;

                if (extension is SupportedVersionsTlsExtension { Versions: var versions }
                    && !versions.Contains(System.Security.Authentication.SslProtocols.Tls12))
                {
                    return false;
                }
            }

            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteCipherSuites(Span<byte> span, ref int offset)
    {
        var lengthPos = offset;
        offset += 2;

        var startOfCodes = offset;

        foreach (var cs in cipherSuites)
        {
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], cs);
            offset += 2;
        }

        var payloadLen = (ushort)(offset - startOfCodes);
        BinaryPrimitives.WriteUInt16BigEndian(span[lengthPos..], payloadLen);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteExtensions(Span<byte> span, ref int offset)
    {
        var lengthPos = offset;
        offset += 2;

        var startOfPayload = offset;

        foreach (var extension in extensions) extension.Write(span, ref offset);

        var payloadLen = (ushort)(offset - startOfPayload);
        BinaryPrimitives.WriteUInt16BigEndian(span[lengthPos..], payloadLen);
    }

    /// <summary>
    /// Добавляет Cipher Suites.
    /// </summary>
    /// <param name="cipherSuites">Cipher Suites.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClientHelloBuilder WithCipherSuites(params IEnumerable<ushort> cipherSuites)
    {
        this.cipherSuites.AddRange(cipherSuites);
        return this;
    }

    /// <summary>
    /// Добавляет Cipher Suites.
    /// </summary>
    /// <param name="cipherSuites">Cipher Suites.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClientHelloBuilder WithCipherSuites(params IEnumerable<CipherSuite> cipherSuites) => WithCipherSuites(cipherSuites.Select(static cs => (ushort)cs));

    /// <summary>
    /// Добавляет расширения.
    /// </summary>
    /// <param name="extensions">Расширения.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClientHelloBuilder WithExtensions(params IEnumerable<ITlsExtension> extensions)
    {
        this.extensions.AddRange(extensions);
        return this;
    }

    /// <summary>
    /// Закрепляет случайное число и идентификатор сессии предыдущего приветствия.
    /// </summary>
    /// <param name="random">Случайное число первого приветствия.</param>
    /// <param name="sessionId">Идентификатор сессии первого приветствия.</param>
    /// <returns>Тот же построитель.</returns>
    /// <remarks>
    /// Нужно только для второго приветствия после HelloRetryRequest: спецификация требует
    /// повторить первое сообщение, изменив лишь долю ключа и добавив эхо cookie.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClientHelloBuilder WithFixedIdentity(ReadOnlyMemory<byte> random, ReadOnlyMemory<byte> sessionId)
    {
        fixedRandom = random;
        fixedSessionId = sessionId;
        return this;
    }

    /// <summary>
    /// Задаёт политику идентификации сессии.
    /// </summary>
    /// <param name="policy">Политика идентификации сессии.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClientHelloBuilder WithSessionIdPolicy(SessionIdPolicy policy)
    {
        sessionIdPolicy = policy;
        return this;
    }

    /// <summary>
    /// Указывает, будет ли использовано понижение с TLS 1.3 до 1.2 версии.
    /// </summary>
    /// <param name="useVersionFallback"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClientHelloBuilder WithVersionFallback(bool useVersionFallback)
    {
        this.useVersionFallback = useVersionFallback;
        return this;
    }

    /// <summary>
    /// Указывает, будет ли использовано понижение с TLS 1.3 до 1.2 версии.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClientHelloBuilder WithVersionFallback() => WithVersionFallback(true);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> Build()
    {
        // Сборка идёт в один проход, поэтому нужный размер оценивается заранее по составу.
        EnsureCapacity(EstimateSize());

        var span = buffer.AsSpan();
        var offset = 0;
        var stage = "record header";

        try
        {
            // Record Layer Header
            span[offset++] = 0x16;
            span[offset++] = 0x03;
            span[offset++] = 0x03;
            var recordLenPos = offset; offset += 2;

            stage = "handshake header";
            span[offset++] = 0x01;
            var hsLenPos = offset; offset += 3;

            stage = "legacy version";
            span[offset++] = 0x03;
            span[offset++] = 0x03;

            stage = "client random";
            Span<byte> clientRandom = stackalloc byte[32];

            // ★ Повторное приветствие после HelloRetryRequest обязано совпадать с первым во всём,
            // кроме доли ключа и эха cookie (RFC 8446, §4.1.2). Свежее случайное число сделало бы
            // его ЧУЖИМ сообщением: строгий сервер вправе оборвать соединение, а от значения
            // random зависят ещё и подставные значения GREASE — они разъехались бы вместе с ним.
            if (fixedRandom.IsEmpty) RandomNumberGenerator.Fill(clientRandom);
            else fixedRandom.Span.CopyTo(clientRandom);

            clientRandom.CopyTo(span.Slice(offset, 32));
            offset += 32;

            // ★ Область выбора подставных значений открывается ВСЕГДА, даже когда подставных
            // расширений в профиле нет. Прежде было две ветки — с областью и без, — и профиль,
            // где подставных расширений нет, а признак GREASE у групп остался, падал изнутри с
            // «Grease.Enter не вызван». Сочетание законное: состав профиля задаёт вызывающая
            // сторона, и она не обязана знать о внутренней связи между этими двумя вещами.
            //
            // Сама область ничего не отправляет — она лишь делает значения выводимыми из
            // случайного числа этого приветствия, поэтому лишней не бывает.
            using (Grease.Enter(clientRandom))
            {
                stage = "session id";
                if (sessionIdPolicy is SessionIdPolicy.Empty)
                {
                    span[offset++] = 0x00;
                }
                else
                {
                    span[offset++] = 0x20; // 32
                    if (fixedSessionId.IsEmpty) RandomNumberGenerator.Fill(span.Slice(offset, 32));
                    else fixedSessionId.Span.CopyTo(span.Slice(offset, 32));
                    offset += 32;
                }

                stage = "cipher suites";

                // ★ ПЕРВЫМ, а не вторым. Браузеры ставят подставной набор в начало списка, и
                // позиция здесь наблюдаема сама по себе: ни ja3, ни ja4 её не показывают —
                // оба выбрасывают GREASE перед подсчётом, — а сырые байты показывают.
                if (IsGreaseCipherSuitesEnabled) cipherSuites.Insert(0, Grease.CipherSuites);

                if (IsRenegotiationScsvRequired && !cipherSuites.Contains(0x00FF)) cipherSuites.Add(0x00FF);
                if (useVersionFallback && !cipherSuites.Contains(0x5600)) cipherSuites.Add(0x5600);

                WriteCipherSuites(span, ref offset);

                stage = "compression methods";
                span[offset++] = 0x01;
                span[offset++] = 0x00;

                stage = "extensions";
                WriteExtensions(span, ref offset);
            }

            stage = "finalize lengths";
            var handshakeLength = offset - (hsLenPos + 3);
            span[hsLenPos + 0] = (byte)((handshakeLength >> 16) & 0xFF);
            span[hsLenPos + 1] = (byte)((handshakeLength >> 8) & 0xFF);
            span[hsLenPos + 2] = (byte)(handshakeLength & 0xFF);

            var recordLength = handshakeLength + 4;
            BinaryPrimitives.WriteUInt16BigEndian(span[recordLenPos..], (ushort)recordLength);

            return span[0..offset];
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"ClientHello build failed at stage '{stage}' with offset={offset}, cipherSuites={cipherSuites.Count}, extensions={extensions.Count}.", exception);
        }
    }

    /// <summary>
    /// Оценивает размер сообщения по нынешнему составу.
    /// </summary>
    /// <returns>Оценка сверху в байтах.</returns>
    /// <remarks>
    /// Именно СВЕРХУ: занизить оценку значит вернуть жёсткий отказ, ради устранения которого всё
    /// и делается. Постоянная часть — заголовки записи и рукопожатия, версия, случайное число,
    /// идентификатор сессии, сжатие и длины векторов.
    /// </remarks>
    private int EstimateSize()
    {
        const int FixedPart = 5 + 4 + 2 + 32 + 1 + 32 + 2 + 2 + 2;

        var size = FixedPart + (cipherSuites.Count * 2) + 8;

        foreach (var extension in extensions) size += extension.Size + 4;

        return size;
    }

    /// <summary>
    /// Обеспечивает вместимость буфера.
    /// </summary>
    /// <param name="required">Требуемый размер.</param>
    private void EnsureCapacity(int required)
    {
        if (buffer.Length >= required) return;

        buffer = new byte[Math.Max(required, buffer.Length * 2)];
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ★ Сбрасывать надо ВСЁ настраиваемое, без исключений. Построитель пулится, и любое
    /// оставшееся поле уедет в ClientHello СЛЕДУЮЩЕГО соединения. Признак отката версии
    /// оставался — а он добавляет в список набор шифров 0x5600, то есть напрямую меняет ja3
    /// соединения, которое об этом не просило.
    /// </remarks>
    [Pooled]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Reset()
    {
        cipherSuites.Clear();
        extensions.Clear();
        sessionIdPolicy = SessionIdPolicy.Empty;
        useVersionFallback = false;
        fixedRandom = default;
        fixedSessionId = default;
        buffer.AsSpan().Clear();
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static IBuilder<ReadOnlySpan<byte>> IBuilder<ReadOnlySpan<byte>>.Create() => Create();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static IBuilder IBuilder.Create() => Create();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ClientHelloBuilder Create() => Rent();
}