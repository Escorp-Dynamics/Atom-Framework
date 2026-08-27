using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using Atom.Net.Tcp;
using Atom.Net.Tls;

namespace Atom.Net.Tests.Tls;

/// <summary>
/// Проверяет уровень записей TLS 1.2: разбиение длин на разряды и наборы на ChaCha20-Poly1305.
/// </summary>
/// <remarks>
/// Оба проверяемых свойства выглядят мелочью, а стоили полной неработоспособности профиля.
///
/// Для всего фреймворка включена проверка переполнения, а протокольный код по природе работает на
/// усечениях: длина записи занимает два байта, счётчик — восемь. Пока эти места не были объявлены
/// непроверяемыми, TLS 1.2 не мог отправить ни одной записи длиннее 255 байт и не переживал 256-й
/// записи соединения. Проявлялось это как «арифметическое переполнение» посреди обмена — то есть
/// как что угодно, только не ошибка протокола.
///
/// ChaCha20 же отличается от AES-GCM формой записи: у него нет явного вектора инициализации, а
/// nonce получается сложением IV со счётчиком. Перепутать формы — значит получить провал
/// расшифровки на первой же записи.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class Tls12RecordLayerTests
{
    private const int TestTimeoutMs = 30000;

    [TestCase(0)]
    [TestCase(255)]
    [TestCase(256)]
    [TestCase(1024)]
    [TestCase(16384)]
    public void AadCarriesRecordLengthBeyondSingleByte(int plainLength)
    {
        using var transport = CreateTransport();
        using var stream = new ProbeStream(transport, CreateSettings());

        Span<byte> explicitIv = stackalloc byte[8];
        Span<byte> nonce = stackalloc byte[12];
        Span<byte> aad = stackalloc byte[13];

        stream.BuildWrite(TlsContentType.ApplicationData, plainLength, seq: 0, explicitIv, nonce, aad);

        var encoded = (aad[11] << 8) | aad[12];

        Assert.That(encoded, Is.EqualTo(plainLength));
    }

    [TestCase(0UL)]
    [TestCase(255UL)]
    [TestCase(256UL)]
    [TestCase(65536UL)]
    [TestCase(ulong.MaxValue)]
    public void AadCarriesSequenceNumberBeyondSingleByte(ulong sequence)
    {
        using var transport = CreateTransport();
        using var stream = new ProbeStream(transport, CreateSettings());

        Span<byte> explicitIv = stackalloc byte[8];
        Span<byte> nonce = stackalloc byte[12];
        Span<byte> aad = stackalloc byte[13];

        stream.BuildWrite(TlsContentType.ApplicationData, plainLength: 16, sequence, explicitIv, nonce, aad);

        var encoded = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(aad[..8]);

        Assert.That(encoded, Is.EqualTo(sequence));
    }

    [Test]
    public void ChaChaNonceIsFixedIvExclusiveOrSequence()
    {
        // RFC 7905: счётчик дополняется нулями слева до длины IV и складывается по модулю два.
        const ulong Sequence = 0x0102030405060708UL;

        var iv = new byte[12];
        for (var index = 0; index < iv.Length; index++) iv[index] = (byte)(index + 1);

        using var transport = CreateTransport();
        using var stream = new ProbeStream(transport, CreateSettings());
        stream.UseChaChaSuite(iv);

        Span<byte> explicitIv = [];
        Span<byte> nonce = stackalloc byte[12];
        Span<byte> aad = stackalloc byte[13];

        stream.BuildWrite(TlsContentType.ApplicationData, plainLength: 64, Sequence, explicitIv, nonce, aad);

        var expected = (byte[])iv.Clone();
        for (var index = 0; index < 8; index++) expected[11 - index] ^= (byte)(Sequence >> (index * 8));

        Assert.That(nonce.ToArray(), Is.EqualTo(expected).AsCollection);
    }

    [Test]
    public void ChaChaNonceMatchesIvWhenSequenceIsZero()
    {
        var iv = new byte[12];
        RandomNumberGenerator.Fill(iv);

        using var transport = CreateTransport();
        using var stream = new ProbeStream(transport, CreateSettings());
        stream.UseChaChaSuite(iv);

        Span<byte> explicitIv = [];
        Span<byte> nonce = stackalloc byte[12];
        Span<byte> aad = stackalloc byte[13];

        stream.BuildWrite(TlsContentType.ApplicationData, plainLength: 64, seq: 0, explicitIv, nonce, aad);

        Assert.That(nonce.ToArray(), Is.EqualTo(iv).AsCollection);
    }

    [Test]
    public void AesGcmNonceCombinesSaltAndExplicitVector()
    {
        // Форма AES-GCM обязана остаться прежней: явный вектор в записи и соль из key_block.
        using var transport = CreateTransport();
        using var stream = new ProbeStream(transport, CreateSettings());
        var salt = stream.UseAesGcmSuite();

        Span<byte> explicitIv = stackalloc byte[8];
        Span<byte> nonce = stackalloc byte[12];
        Span<byte> aad = stackalloc byte[13];

        stream.BuildWrite(TlsContentType.ApplicationData, plainLength: 64, seq: 7, explicitIv, nonce, aad);

        var noncePrefix = nonce[..4].ToArray();
        var nonceCounter = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(nonce[4..]);
        var explicitCounter = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(explicitIv);

        Assert.Multiple(() =>
        {
            Assert.That(noncePrefix, Is.EqualTo(salt).AsCollection);
            Assert.That(nonceCounter, Is.EqualTo(7UL));
            Assert.That(explicitCounter, Is.EqualTo(7UL));
        });
    }

    [Test]
    public void ChaChaAeadRoundTripsPayload()
    {
        Assert.That(ChaCha20Poly1305Aead.IsSupported, Is.True, "платформа не предоставляет ChaCha20-Poly1305");

        Span<byte> key = stackalloc byte[32];
        Span<byte> nonce = stackalloc byte[12];
        RandomNumberGenerator.Fill(key);
        RandomNumberGenerator.Fill(nonce);

        using var cipher = new ChaCha20Poly1305Aead(key);

        var plaintext = new byte[4096];
        RandomNumberGenerator.Fill(plaintext);

        var aad = new byte[13];
        RandomNumberGenerator.Fill(aad);

        var sealed_ = new byte[plaintext.Length + cipher.TagSize];
        var opened = new byte[plaintext.Length];

        var encrypted = cipher.TryEncrypt(nonce, aad, plaintext, sealed_, out var written);
        var decrypted = cipher.TryDecrypt(nonce, aad, sealed_, opened, out var restored);

        Assert.Multiple(() =>
        {
            Assert.That(encrypted, Is.True);
            Assert.That(written, Is.EqualTo(plaintext.Length + cipher.TagSize));
            Assert.That(decrypted, Is.True);
            Assert.That(restored, Is.EqualTo(plaintext.Length));
            Assert.That(opened, Is.EqualTo(plaintext).AsCollection);
        });
    }

    [Test]
    public void ChaChaAeadRejectsTamperedCiphertext()
    {
        Span<byte> key = stackalloc byte[32];
        Span<byte> nonce = stackalloc byte[12];
        RandomNumberGenerator.Fill(key);
        RandomNumberGenerator.Fill(nonce);

        using var cipher = new ChaCha20Poly1305Aead(key);

        var plaintext = "проверка целостности"u8.ToArray();
        var aad = new byte[13];
        var sealed_ = new byte[plaintext.Length + cipher.TagSize];

        _ = cipher.TryEncrypt(nonce, aad, plaintext, sealed_, out _);
        sealed_[0] ^= 0xFF;

        var opened = new byte[plaintext.Length];

        // Подделка обязана быть отвергнута без исключения: вызывающая сторона отличает провал по
        // возвращаемому значению, а исключение на каждой битой записи стоило бы дорого.
        Assert.That(cipher.TryDecrypt(nonce, aad, sealed_, opened, out _), Is.False);
    }

    private static TlsSettings CreateSettings() => new() { SessionIdPolicy = SessionIdPolicy.Fixed32 };

    private static StubNetworkStream CreateTransport() => new();

    /// <summary>
    /// Транспорт-заглушка: проверяются построения буферов, обмена по сети здесь нет.
    /// </summary>
    private sealed class StubNetworkStream() : NetworkStream(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(0);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Открывает защищённое построение AAD и nonce для проверки.
    /// </summary>
    /// <remarks>
    /// Наследование вместо рефлексии: метод виртуальный, и вызывать его через наследника — тот же
    /// путь, каким пользуется сам поток, а не обход инкапсуляции.
    /// </remarks>
    private sealed class ProbeStream(NetworkStream stream, in TlsSettings settings) : Tls12Stream(stream, settings)
    {
        public void BuildWrite(TlsContentType type, int plainLength, ulong seq, Span<byte> explicitIv, Span<byte> nonce, Span<byte> aad)
            => BuildAadAndNonceForWrite(type, plainLength, seq, explicitIv, nonce, aad);

        /// <summary>
        /// Приводит поток в состояние согласованного набора ChaCha20 с заданным IV.
        /// </summary>
        /// <param name="iv">Фиксированный вектор инициализации длиной 12 байт.</param>
        /// <remarks>
        /// Результат рукопожатия подставляется напрямую: полное рукопожатие требует сервера, а
        /// проверяется здесь ровно то, что от него зависит, — форма записи.
        /// </remarks>
        public void UseChaChaSuite(byte[] iv)
        {
            SetPrivateField("chosenSuite", (ushort)0xCCA9);
            SetPrivateField("clientWriteIvSalt", iv);
        }

        /// <summary>
        /// Приводит поток в состояние согласованного набора AES-GCM.
        /// </summary>
        /// <returns>Соль, попавшая в поток.</returns>
        public byte[] UseAesGcmSuite()
        {
            var salt = new byte[4];
            RandomNumberGenerator.Fill(salt);

            SetPrivateField("chosenSuite", (ushort)0xC02B);
            SetPrivateField("clientWriteIvSalt", salt);

            return salt;
        }

        private void SetPrivateField(string name, object value)
        {
            var field = typeof(Tls12Stream).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Поле {name} не найдено.");

            field.SetValue(this, value);
        }
    }
}
