using System.Security.Cryptography;
using Atom.Net.Tls;

namespace Atom.Net.Tests.Tls;

/// <summary>
/// Сверяет таблицу параметров наборов TLS 1.2 с реестром IANA.
/// </summary>
/// <remarks>
/// Проверка выглядит переписыванием таблицы, а ловит она две ошибки, которые иначе НЕ видны без
/// сети и вылезают в самом конце внешне безупречного рукопожатия:
///
/// • неверный хэш PRF (0x009D требует SHA-384) — master secret выйдет другим, и сервер отвергнет
///   Finished; выглядит это как «сервер сломался»;
/// • неверная длина ключа (0x009D требует 32 байта) — тот же симптом при верном master secret.
///
/// Различить их по проводу невозможно. Поэтому эталон записан здесь ЯВНО, а не выведен из того же
/// кода, что проверяется: таблица, сверенная сама с собой, не проверяет ничего.
/// </remarks>
public sealed class Tls12CipherSuiteParametersTests
{
    /// <summary>
    /// Пятнадцать наборов профиля Chrome: код, обмен ключами, шифр, ключ, MAC-ключ, фикс. IV,
    /// явный IV, тег, PRF.
    /// </summary>
    private static readonly object[] Tls12Suites =
    [
        new object[] { (ushort)0xC02B, "ECDHE", "GCM", 16, 0, 4, 8, 16, "SHA256" },
        new object[] { (ushort)0xC02C, "ECDHE", "GCM", 32, 0, 4, 8, 16, "SHA384" },
        new object[] { (ushort)0xC02F, "ECDHE", "GCM", 16, 0, 4, 8, 16, "SHA256" },
        new object[] { (ushort)0xC030, "ECDHE", "GCM", 32, 0, 4, 8, 16, "SHA384" },
        new object[] { (ushort)0xCCA8, "ECDHE", "CHACHA", 32, 0, 12, 0, 16, "SHA256" },
        new object[] { (ushort)0xCCA9, "ECDHE", "CHACHA", 32, 0, 12, 0, 16, "SHA256" },
        new object[] { (ushort)0x009C, "RSA", "GCM", 16, 0, 4, 8, 16, "SHA256" },
        new object[] { (ushort)0x009D, "RSA", "GCM", 32, 0, 4, 8, 16, "SHA384" },
        new object[] { (ushort)0x002F, "RSA", "CBC", 16, 20, 0, 16, 20, "SHA256" },
        new object[] { (ushort)0x0035, "RSA", "CBC", 32, 20, 0, 16, 20, "SHA256" },
        new object[] { (ushort)0xC013, "ECDHE", "CBC", 16, 20, 0, 16, 20, "SHA256" },
        new object[] { (ushort)0xC014, "ECDHE", "CBC", 32, 20, 0, 16, 20, "SHA256" },
        new object[] { (ushort)0xC009, "ECDHE", "CBC", 16, 20, 0, 16, 20, "SHA256" },
        new object[] { (ushort)0xC00A, "ECDHE", "CBC", 32, 20, 0, 16, 20, "SHA256" },
    ];

    [TestCaseSource(nameof(Tls12Suites))]
    public void ParametersMatchIanaRegistry(ushort suite, string keyExchange, string bulk, int keyLength, int macKeyLength, int fixedIvLength, int recordIvLength, int tagLength, string prf)
    {
        Assert.That(Tls12CipherSuiteParameters.TryGet(suite, out var parameters), Is.True, $"набор 0x{suite:X4} отсутствует в таблице");

        Assert.Multiple(() =>
        {
            Assert.That(parameters.KeyExchange.ToString(), Is.EqualTo(keyExchange is "RSA" ? "Rsa" : "Ecdhe"), "обмен ключами");
            Assert.That(DescribeBulk(parameters.Bulk), Is.EqualTo(bulk), "шифр");
            Assert.That(parameters.EncKeyLength, Is.EqualTo(keyLength), "длина ключа шифрования");
            Assert.That(parameters.MacKeyLength, Is.EqualTo(macKeyLength), "длина MAC-ключа");
            Assert.That(parameters.FixedIvLength, Is.EqualTo(fixedIvLength), "фиксированная часть IV");
            Assert.That(parameters.RecordIvLength, Is.EqualTo(recordIvLength), "явный IV в записи");
            Assert.That(parameters.TagLength, Is.EqualTo(tagLength), "длина тега/метки");
            Assert.That(parameters.PrfHash.Name, Is.EqualTo(prf), "хэш PRF");
        });
    }

    [Test]
    public void Tls13SuitesAreNotServedByTheTls12Table()
    {
        // Наборы TLS 1.3 предлагаются тем же ClientHello, но обслуживаются другим потоком.
        // Попади они сюда — Tls12Stream счёл бы их реализованными и повёл бы рукопожатие 1.2
        // по несуществующей схеме.
        Assert.Multiple(() =>
        {
            Assert.That(Tls12CipherSuiteParameters.TryGet(0x1301, out _), Is.False);
            Assert.That(Tls12CipherSuiteParameters.TryGet(0x1302, out _), Is.False);
            Assert.That(Tls12CipherSuiteParameters.TryGet(0x1303, out _), Is.False);
        });
    }

    [Test]
    public void TripleDesSuitesAreRefusedRatherThanSilentlyBroken()
    {
        // Тройку 3DES Safari предлагает ради отпечатка, но реализации у нас нет. Объявить её
        // поддержанной значило бы получить непонятную поломку посреди обмена вместо внятного
        // отказа на ServerHello — это хуже прежнего состояния, а не лучше.
        Assert.Multiple(() =>
        {
            Assert.That(Tls12Stream.IsImplementedSuite(0xC008), Is.False);
            Assert.That(Tls12Stream.IsImplementedSuite(0xC012), Is.False);
            Assert.That(Tls12Stream.IsImplementedSuite(0x000A), Is.False);
        });
    }

    [Test]
    public void NewlyImplementedSuitesAreAnnouncedAsImplemented()
    {
        Assert.Multiple(() =>
        {
            foreach (var suite in new ushort[] { 0x009C, 0x009D, 0x002F, 0x0035, 0xC013, 0xC014 })
                Assert.That(Tls12Stream.IsImplementedSuite(suite), Is.True, $"0x{suite:X4}");
        });
    }

    [Test]
    public void OfferedCheckIsSeparateFromImplementedCheck()
    {
        // ★ Регрессия на дыру, которую легко открыть, расширяя список реализованного: сервер не
        // вправе выбрать набор, которого мы не предлагали, даже если мы умеем его обслужить.
        // Иначе мимикрия превращается в согласие на что угодно.
        CipherSuite[] offered = [CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256];

        Assert.Multiple(() =>
        {
            Assert.That(Tls12Stream.IsImplementedSuite(0x0035), Is.True, "набор реализован");
            Assert.That(Tls12Stream.IsOfferedSuite(0x0035, offered), Is.False, "но не предлагался");
            Assert.That(Tls12Stream.IsOfferedSuite(0xC02F, offered), Is.True);
        });
    }

    private static string DescribeBulk(Tls12BulkCipher bulk) => bulk switch
    {
        Tls12BulkCipher.AesGcm => "GCM",
        Tls12BulkCipher.ChaCha20Poly1305 => "CHACHA",
        Tls12BulkCipher.AesCbc => "CBC",
        _ => bulk.ToString(),
    };

    [Test]
    public void PrfHashIsAlwaysOneTls12PrfUnderstands()
    {
        // Защита от опечатки в имени: Tls12Prf принимает только SHA-256/384/512.
        Assert.Multiple(() =>
        {
            foreach (var entry in Tls12Suites.Cast<object[]>())
            {
                var suite = (ushort)entry[0];
                Assert.That(Tls12CipherSuiteParameters.TryGet(suite, out var parameters), Is.True);
                Assert.That(parameters.PrfHash, Is.EqualTo(HashAlgorithmName.SHA256).Or.EqualTo(HashAlgorithmName.SHA384));
            }
        });
    }
}
