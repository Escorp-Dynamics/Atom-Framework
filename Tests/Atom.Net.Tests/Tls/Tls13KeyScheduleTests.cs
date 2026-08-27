using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Atom.Net.Tls;

namespace Atom.Net.Tests.Tls;

/// <summary>
/// Проверяет расписание ключей TLS 1.3.
/// </summary>
/// <remarks>
/// Тесты намеренно опираются на структурные инварианты спецификации, а не на эталонные дампы:
/// дампы к проекту не приложены, а ошибка в выводе ключей не даёт отказа — она даёт молчаливую
/// порчу трафика, которую внутри рукопожатия ловить несоизмеримо дороже. Там, где нужна
/// независимая проверка самой криптографии, оракулом служит платформенный <see cref="HKDF"/>:
/// собственная часть сводится к структуре метки и к порядку вычислений, их и проверяем.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class Tls13KeyScheduleTests
{
    private const int TestTimeoutMs = 30000;

    [Test]
    public void ExpandLabelMatchesHkdfLabelEncodingFromSpecification()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        const string label = "key";
        var context = RandomNumberGenerator.GetBytes(32);
        const int length = 16;

        var actual = Tls13KeySchedule.ExpandLabel(HashAlgorithmName.SHA256, secret, label, context, length);

        // HkdfLabel собираем независимо, прямо по тексту RFC 8446 §7.1:
        // struct { uint16 length; opaque label<7..255>; opaque context<0..255>; }
        var fullLabel = Encoding.ASCII.GetBytes("tls13 " + label);
        var info = new byte[2 + 1 + fullLabel.Length + 1 + context.Length];
        BinaryPrimitives.WriteUInt16BigEndian(info, length);
        info[2] = (byte)fullLabel.Length;
        fullLabel.CopyTo(info, 3);
        info[3 + fullLabel.Length] = (byte)context.Length;
        context.CopyTo(info, 4 + fullLabel.Length);

        var expected = new byte[length];
        HKDF.Expand(HashAlgorithmName.SHA256, secret, expected, info);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void ExpandLabelHonorsRequestedLength()
    {
        var secret = RandomNumberGenerator.GetBytes(32);

        Assert.Multiple(() =>
        {
            Assert.That(Tls13KeySchedule.ExpandLabel(HashAlgorithmName.SHA256, secret, "iv", [], 12), Has.Length.EqualTo(12));
            Assert.That(Tls13KeySchedule.ExpandLabel(HashAlgorithmName.SHA256, secret, "key", [], 16), Has.Length.EqualTo(16));
            // Для SHA-384 входной секрет обязан быть длиной с хэш: HKDF требует PRK не короче
            // размера хэш-функции. В реальном расписании это выполняется всегда — секреты там
            // ровно хэш-длины, — поэтому и в тесте берём корректный размер.
            Assert.That(Tls13KeySchedule.ExpandLabel(HashAlgorithmName.SHA384, RandomNumberGenerator.GetBytes(48), "key", [], 32), Has.Length.EqualTo(32));
        });
    }

    [Test]
    public void ExpandLabelDistinguishesLabels()
    {
        var secret = RandomNumberGenerator.GetBytes(32);

        var key = Tls13KeySchedule.ExpandLabel(HashAlgorithmName.SHA256, secret, "key", [], 16);
        var iv = Tls13KeySchedule.ExpandLabel(HashAlgorithmName.SHA256, secret, "iv", [], 16);

        // Ключ и IV выводятся из ОДНОГО секрета и различаются только меткой: совпадение означало бы,
        // что метка в вывод не попадает, и тогда весь record layer шифровался бы предсказуемо.
        Assert.That(key, Is.Not.EqualTo(iv));
    }

    [Test]
    public void DeriveSecretReturnsHashSizedOutput()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var transcript = SHA256.HashData(Encoding.ASCII.GetBytes("transcript"));

        Assert.Multiple(() =>
        {
            Assert.That(Tls13KeySchedule.DeriveSecret(HashAlgorithmName.SHA256, secret, "c hs traffic", transcript), Has.Length.EqualTo(32));
            Assert.That(Tls13KeySchedule.DeriveSecret(HashAlgorithmName.SHA384, RandomNumberGenerator.GetBytes(48), "c hs traffic", SHA384.HashData([])), Has.Length.EqualTo(48));
        });
    }

    [Test]
    public void DeriveSecretDependsOnTranscript()
    {
        var secret = RandomNumberGenerator.GetBytes(32);

        var first = Tls13KeySchedule.DeriveSecret(HashAlgorithmName.SHA256, secret, "c hs traffic", SHA256.HashData(Encoding.ASCII.GetBytes("first")));
        var second = Tls13KeySchedule.DeriveSecret(HashAlgorithmName.SHA256, secret, "c hs traffic", SHA256.HashData(Encoding.ASCII.GetBytes("second")));

        // Привязка к транскрипту — то, что защищает рукопожатие от подмены сообщений. Если её нет,
        // секреты совпадут при разной истории обмена.
        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public void EarlySecretWithoutPreSharedKeyUsesZeroKeyAndZeroSalt()
    {
        var actual = Tls13KeySchedule.DeriveEarlySecret(HashAlgorithmName.SHA256, []);

        var zero = new byte[32];
        var expected = new byte[32];
        HKDF.Extract(HashAlgorithmName.SHA256, zero, zero, expected);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void HandshakeSecretGoesThroughDerivedTransition()
    {
        var early = Tls13KeySchedule.DeriveEarlySecret(HashAlgorithmName.SHA256, []);
        var shared = RandomNumberGenerator.GetBytes(32);

        var actual = Tls13KeySchedule.DeriveHandshakeSecret(HashAlgorithmName.SHA256, early, shared);

        // Переход между ступенями обязан идти через Derive-Secret с меткой "derived" по хэшу ПУСТОЙ
        // строки. Пропуск этого шага даёт рабочий с виду вывод, но несовместимый с любым сервером,
        // поэтому проверяем именно его, а не только длину результата.
        var derived = Tls13KeySchedule.ExpandLabel(HashAlgorithmName.SHA256, early, "derived", SHA256.HashData([]), 32);
        var expected = new byte[32];
        HKDF.Extract(HashAlgorithmName.SHA256, shared, derived, expected);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void MasterSecretGoesThroughDerivedTransitionWithZeroInput()
    {
        var early = Tls13KeySchedule.DeriveEarlySecret(HashAlgorithmName.SHA256, []);
        var handshake = Tls13KeySchedule.DeriveHandshakeSecret(HashAlgorithmName.SHA256, early, RandomNumberGenerator.GetBytes(32));

        var actual = Tls13KeySchedule.DeriveMasterSecret(HashAlgorithmName.SHA256, handshake);

        var derived = Tls13KeySchedule.ExpandLabel(HashAlgorithmName.SHA256, handshake, "derived", SHA256.HashData([]), 32);
        var expected = new byte[32];
        HKDF.Extract(HashAlgorithmName.SHA256, new byte[32], derived, expected);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void RecordKeysHaveAeadSizes()
    {
        var secret = RandomNumberGenerator.GetBytes(32);

        var (key, iv) = Tls13KeySchedule.DeriveRecordKeys(HashAlgorithmName.SHA256, secret, keyLength: 16, ivLength: 12);

        Assert.Multiple(() =>
        {
            Assert.That(key, Has.Length.EqualTo(16));
            Assert.That(iv, Has.Length.EqualTo(12));
            Assert.That(key, Is.Not.EqualTo(iv));
        });
    }

    [Test]
    public void FinishedKeyAndVerifyDataAreDeterministic()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var transcript = SHA256.HashData(Encoding.ASCII.GetBytes("finished"));

        var finishedKey = Tls13KeySchedule.DeriveFinishedKey(HashAlgorithmName.SHA256, secret);
        var first = Tls13KeySchedule.ComputeVerifyData(HashAlgorithmName.SHA256, finishedKey, transcript);
        var second = Tls13KeySchedule.ComputeVerifyData(HashAlgorithmName.SHA256, finishedKey, transcript);

        Assert.Multiple(() =>
        {
            Assert.That(finishedKey, Has.Length.EqualTo(32));
            Assert.That(first, Has.Length.EqualTo(32));
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.EqualTo(HMACSHA256.HashData(finishedKey, transcript)));
        });
    }

    [Test]
    public void VerifyDataUsesSha384ForSha384Suites()
    {
        var secret = RandomNumberGenerator.GetBytes(48);
        var transcript = SHA384.HashData(Encoding.ASCII.GetBytes("finished"));

        var finishedKey = Tls13KeySchedule.DeriveFinishedKey(HashAlgorithmName.SHA384, secret);
        var verifyData = Tls13KeySchedule.ComputeVerifyData(HashAlgorithmName.SHA384, finishedKey, transcript);

        Assert.Multiple(() =>
        {
            Assert.That(finishedKey, Has.Length.EqualTo(48));
            Assert.That(verifyData, Is.EqualTo(HMACSHA384.HashData(finishedKey, transcript)));
        });
    }

    [Test]
    public void NextTrafficSecretDiffersFromCurrent()
    {
        var secret = RandomNumberGenerator.GetBytes(32);

        var next = Tls13KeySchedule.DeriveNextTrafficSecret(HashAlgorithmName.SHA256, secret);

        Assert.Multiple(() =>
        {
            Assert.That(next, Has.Length.EqualTo(32));
            Assert.That(next, Is.Not.EqualTo(secret));
        });
    }

    [Test]
    public void EmptyHashMatchesHashOfEmptyInput()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Tls13KeySchedule.EmptyHash(HashAlgorithmName.SHA256), Is.EqualTo(SHA256.HashData([])));
            Assert.That(Tls13KeySchedule.EmptyHash(HashAlgorithmName.SHA384), Is.EqualTo(SHA384.HashData([])));
        });
    }

    [Test]
    public void ExpandLabelRejectsOversizedContext()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var oversized = new byte[256];

        // Длина контекста в HkdfLabel — один байт. Молча обрезать её нельзя: получился бы вывод,
        // не совпадающий с серверным, то есть та самая тихая порча вместо явной ошибки.
        Assert.That(
            () => Tls13KeySchedule.ExpandLabel(HashAlgorithmName.SHA256, secret, "key", oversized, 16),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }
}
