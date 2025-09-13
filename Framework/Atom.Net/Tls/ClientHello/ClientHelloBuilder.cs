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
    private readonly byte[] buffer = new byte[4096];

    private readonly List<ushort> cipherSuites = [];
    private readonly List<ITlsExtension> extensions = [];
    private SessionIdPolicy sessionIdPolicy;
    private bool useVersionFallback;

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
        var span = buffer.AsSpan();
        var offset = 0;

        // Record Layer Header
        span[offset++] = 0x16;
        span[offset++] = 0x03;
        span[offset++] = 0x03;
        var recordLenPos = offset; offset += 2;

        // Handshake Header
        span[offset++] = 0x01;
        var hsLenPos = offset; offset += 3;

        // client_hello.legacy_version
        span[offset++] = 0x03;
        span[offset++] = 0x03;

        // Random (32 байта) + заведём GREASE-контекст
        Span<byte> clientRandom = stackalloc byte[32];
        RandomNumberGenerator.Fill(clientRandom);
        clientRandom.CopyTo(span.Slice(offset, 32));
        offset += 32;

        using (Grease.Enter(clientRandom))
        {
            // legacy_session_id (по политике билдера, прокинутой из TlsStream)
            if (sessionIdPolicy is SessionIdPolicy.Empty)
            {
                span[offset++] = 0x00;
            }
            else
            {
                span[offset++] = 0x20; // 32
                RandomNumberGenerator.Fill(span.Slice(offset, 32));
                offset += 32;
            }

            // cipher_suites: при необходимости вставим GREASE на 2-ю позицию (как Chromium)
            if (IsGreaseCipherSuitesEnabled)
            {
                // Вставляем Grease.CipherSuites во внутренний список на позицию 1
                var pos = cipherSuites.Count > 0 ? 1 : 0;
                cipherSuites.Insert(pos, Grease.CipherSuites);
            }

            // SCSV в конце списка (браузерный отпечаток)
            if (!cipherSuites.Contains(0x00FF)) cipherSuites.Add(0x00FF); // TLS_EMPTY_RENEGOTIATION_INFO_SCSV
            if (useVersionFallback && !cipherSuites.Contains(0x5600)) cipherSuites.Add(0x5600); // TLS_FALLBACK_SCSV

            WriteCipherSuites(span[offset..], ref offset);

            // compression_methods
            span[offset++] = 0x01;
            span[offset++] = 0x00;

            // extensions (каждое расширение при записи может использовать Grease.* и client_random из статического провайдера)
            WriteExtensions(span[offset..], ref offset);
        }

        // финализация длин
        var handshakeLength = offset - (hsLenPos + 3);
        span[hsLenPos + 0] = (byte)((handshakeLength >> 16) & 0xFF);
        span[hsLenPos + 1] = (byte)((handshakeLength >> 8) & 0xFF);
        span[hsLenPos + 2] = (byte)(handshakeLength & 0xFF);

        var recordLength = handshakeLength + 4;
        BinaryPrimitives.WriteUInt16BigEndian(span[recordLenPos..], (ushort)recordLength);

        return span[0..offset];
    }

    /// <inheritdoc/>
    [Pooled]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Reset()
    {
        cipherSuites.Clear();
        extensions.Clear();
        sessionIdPolicy = SessionIdPolicy.Empty;
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