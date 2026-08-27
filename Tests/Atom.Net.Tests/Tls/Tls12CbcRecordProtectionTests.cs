using System.Security.Cryptography;
using Atom.Net.Tls;

namespace Atom.Net.Tests.Tls;

/// <summary>
/// Проверяет защиту записи TLS 1.2 в режиме CBC: формат, подлинность и единство отказа.
/// </summary>
/// <remarks>
/// Режим CBC отличается от AEAD тем, что подлинность здесь обеспечивает НЕ примитив, а наш
/// собственный код: HMAC поверх открытого текста, затем дополнение, затем шифрование. Ошибка в
/// любом из трёх шагов даёт либо неработающий обмен, либо — что хуже — работающий обмен без
/// проверки подлинности. Поэтому проверяется и удачный путь, и все виды подделки.
///
/// Отдельная часть — ЕДИНСТВО отказа. Различимые причины отказа (по коду, по тексту, по времени)
/// и есть уязвимости Lucky13/POODLE (RFC 7457 §2.2): по ним партнёр превращается в оракул. Тест
/// фиксирует, что подделка шифротекста, подделка дополнения и усечение дают ровно один и тот же
/// внешний результат.
/// </remarks>
public sealed class Tls12CbcRecordProtectionTests
{
    private const int Block = 16;
    private const int Mac = 20;

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(10)]
    [TestCase(11)]     // граница: content + MAC + 1 ровно кратно блоку → дополнение 15 байт
    [TestCase(12)]
    [TestCase(27)]
    [TestCase(255)]
    [TestCase(256)]
    [TestCase(1024)]
    [TestCase(16384)]  // максимум открытых данных одной записи
    public void RoundTripsEveryLengthAcrossBlockBoundaries(int length)
    {
        using var protection = CreateProtection(out _, out _);

        var plain = new byte[length];
        RandomNumberGenerator.Fill(plain);

        var record = Protect(protection, seq: 0, TlsContentType.ApplicationData, plain);

        Assert.That(record.Length % Block, Is.Zero, "запись CBC обязана быть кратна блоку");
        Assert.That(record.Length, Is.GreaterThanOrEqualTo(Block + Mac + 1));

        var opened = new byte[record.Length];
        var header = BuildHeader(seq: 0, TlsContentType.ApplicationData, plainLength: 0);

        Assert.That(protection.TryUnprotect(record, header, opened, out var openedLength), Is.True);
        Assert.That(openedLength, Is.EqualTo(length));
        Assert.That(opened.AsSpan(0, length).ToArray(), Is.EqualTo(plain).AsCollection);
    }

    [Test]
    public void ExplicitVectorIsRandomOnEveryRecord()
    {
        // ★ Предсказуемый вектор режима CBC — это BEAST (CVE-2011-3389), ровно тот дефект
        // TLS 1.0, ради устранения которого явный вектор в 1.1/1.2 и вводили. Взять сюда счётчик
        // записи было бы «работающим» кодом с вернувшейся уязвимостью, и по трафику это не видно.
        using var protection = CreateProtection(out _, out _);

        var plain = "одно и то же содержимое"u8.ToArray();
        var first = Protect(protection, seq: 0, TlsContentType.ApplicationData, plain);
        var second = Protect(protection, seq: 1, TlsContentType.ApplicationData, plain);

        Assert.That(first.AsSpan(0, Block).SequenceEqual(second.AsSpan(0, Block)), Is.False, "явный вектор повторился");
        Assert.That(first.AsSpan(Block).SequenceEqual(second.AsSpan(Block)), Is.False, "шифротекст повторился");
    }

    [Test]
    public void MacCoversSequenceTypeVersionAndLengthInThatOrder()
    {
        // Порядок seq || type || version || length || content закрепляется тестом, а не только
        // комментарием: перепутанные поля дают рабочую отправку и нерабочий приём, а разбираться
        // придётся по дампу чужого сервера.
        using var protection = CreateProtection(out var encryptionKey, out var macKey);

        const ulong Sequence = 0x0102030405060708UL;
        var plain = "содержимое записи"u8.ToArray();

        var record = Protect(protection, Sequence, TlsContentType.ApplicationData, plain);

        using var aes = Aes.Create();
        aes.Key = encryptionKey;

        var body = aes.DecryptCbc(record.AsSpan(Block), record.AsSpan(0, Block), PaddingMode.None);

        var reference = new byte[13 + plain.Length];
        BuildHeader(Sequence, TlsContentType.ApplicationData, plain.Length).CopyTo(reference.AsSpan());
        plain.CopyTo(reference.AsSpan(13));

        var expectedMac = HMACSHA1.HashData(macKey, reference);
        var paddingLength = body[^1];

        Assert.Multiple(() =>
        {
            Assert.That(body.AsSpan(0, plain.Length).ToArray(), Is.EqualTo(plain).AsCollection, "открытый текст идёт первым");
            Assert.That(body.AsSpan(plain.Length, Mac).ToArray(), Is.EqualTo(expectedMac).AsCollection, "метка считается по seq|type|version|length|content");
            Assert.That(body.Length, Is.EqualTo(plain.Length + Mac + paddingLength + 1), "дополнение замыкается своей длиной");

            for (var index = 0; index <= paddingLength; index++)
                Assert.That(body[^(index + 1)], Is.EqualTo(paddingLength), "все байты дополнения равны его длине");
        });
    }

    [Test]
    public void EveryKindOfForgeryFailsIdenticallyAndWithoutException()
    {
        // ★ Главное здесь — не «подделка отвергнута», а то, что ВСЕ подделки отвергнуты ОДИНАКОВО.
        // Различие в типе исключения, тексте или самом факте его наличия — это оракул: по нему
        // отличается «сломано дополнение» от «сломана метка», а на этом отличии и строится атака.
        using var protection = CreateProtection(out _, out _);

        var plain = "проверка подлинности записи"u8.ToArray();
        var pristine = Protect(protection, seq: 0, TlsContentType.ApplicationData, plain);

        var forgeries = new List<(string Name, byte[] Record)>
        {
            ("байт шифротекста", Tamper(pristine, Block + 3)),
            ("явный вектор", Tamper(pristine, 0)),
            ("последний байт (длина дополнения)", Tamper(pristine, pristine.Length - 1)),
            ("байт внутри метки", Tamper(pristine, pristine.Length - Block - 2)),
            ("усечение на блок", pristine.AsSpan(0, pristine.Length - Block).ToArray()),
            ("усечение на байт", pristine.AsSpan(0, pristine.Length - 1).ToArray()),
            ("пустая запись", []),
            ("только вектор", pristine.AsSpan(0, Block).ToArray()),
        };

        Assert.Multiple(() =>
        {
            foreach (var (name, forged) in forgeries)
            {
                var opened = new byte[Math.Max(forged.Length, 1)];
                var header = BuildHeader(seq: 0, TlsContentType.ApplicationData, plainLength: 0);

                bool accepted;

                try
                {
                    accepted = protection.TryUnprotect(forged, header, opened, out _);
                }
                catch (Exception error)
                {
                    Assert.Fail($"подделка «{name}» вызвала исключение {error.GetType().Name} вместо единого отказа");
                    continue;
                }

                Assert.That(accepted, Is.False, $"подделка «{name}» принята");
            }
        });
    }

    [Test]
    public void SequenceNumberMismatchIsRejected()
    {
        // Счётчик входит в метку: запись, воспроизведённая не на своём месте, обязана отвергаться.
        // Без этого повтор записи прошёл бы как подлинный.
        using var protection = CreateProtection(out _, out _);

        var plain = "запись номер ноль"u8.ToArray();
        var record = Protect(protection, seq: 0, TlsContentType.ApplicationData, plain);

        var opened = new byte[record.Length];
        var header = BuildHeader(seq: 1, TlsContentType.ApplicationData, plainLength: 0);

        Assert.That(protection.TryUnprotect(record, header, opened, out _), Is.False);
    }

    [Test]
    public void ContentTypeMismatchIsRejected()
    {
        using var protection = CreateProtection(out _, out _);

        var plain = "тревога"u8.ToArray();
        var record = Protect(protection, seq: 0, TlsContentType.Alert, plain);

        var opened = new byte[record.Length];
        var header = BuildHeader(seq: 0, TlsContentType.ApplicationData, plainLength: 0);

        Assert.That(protection.TryUnprotect(record, header, opened, out _), Is.False);
    }

    [Test]
    public void ProtectedLengthMatchesWhatProtectWrites()
    {
        using var protection = CreateProtection(out _, out _);

        Assert.Multiple(() =>
        {
            for (var length = 0; length <= 96; length++)
            {
                var plain = new byte[length];
                var expected = Tls12CbcRecordProtection.ProtectedLength(length);
                var record = new byte[expected];
                var header = BuildHeader(seq: 0, TlsContentType.ApplicationData, length);

                Assert.That(protection.Protect(header, plain, record), Is.EqualTo(expected), $"длина {length}");
            }
        });
    }

    private static Tls12CbcRecordProtection CreateProtection(out byte[] encryptionKey, out byte[] macKey)
    {
        encryptionKey = new byte[16];
        macKey = new byte[Mac];
        RandomNumberGenerator.Fill(encryptionKey);
        RandomNumberGenerator.Fill(macKey);

        return new Tls12CbcRecordProtection(encryptionKey, macKey);
    }

    private static byte[] Protect(Tls12CbcRecordProtection protection, ulong seq, TlsContentType type, byte[] plain)
    {
        var record = new byte[Tls12CbcRecordProtection.ProtectedLength(plain.Length)];
        var written = protection.Protect(BuildHeader(seq, type, plain.Length), plain, record);

        Assert.That(written, Is.EqualTo(record.Length));
        return record;
    }

    /// <summary>
    /// Тринадцать байт seq(8) | type(1) | 0x0303 | length(2) — заголовок MAC (RFC 5246 §6.2.3.1).
    /// </summary>
    private static byte[] BuildHeader(ulong seq, TlsContentType type, int plainLength)
    {
        var header = new byte[13];

        for (var index = 0; index < 8; index++)
            header[7 - index] = unchecked((byte)(seq >> (index * 8)));

        header[8] = (byte)type;
        header[9] = 0x03;
        header[10] = 0x03;

        unchecked
        {
            header[11] = (byte)(plainLength >> 8);
            header[12] = (byte)plainLength;
        }

        return header;
    }

    private static byte[] Tamper(byte[] record, int index)
    {
        var copy = (byte[])record.Clone();
        copy[index] ^= 0xFF;
        return copy;
    }
}
