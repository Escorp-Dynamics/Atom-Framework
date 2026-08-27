using System.Security.Cryptography;
using Atom.Net.Tls;

namespace Atom.Net.Tests.Tls;

/// <summary>
/// Проверяет реализацию Curve25519.
/// </summary>
/// <remarks>
/// Эталонные значения получены локально из OpenSSL 3.6: это независимая от нашего кода реализация,
/// поэтому совпадение с ней исключает как ошибку арифметики, так и ошибку кодирования координат.
/// Проверка обязательна — своя криптография, которая компилируется, но считает неверно, опаснее
/// отсутствующей: рукопожатие просто не сойдётся, а причина будет выглядеть как сетевой сбой.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class X25519Tests
{
    private const int TestTimeoutMs = 60000;

    /// <summary>Приватный ключ стороны A (OpenSSL).</summary>
    private const string PrivateA = "d82e6077a4f547ba4b01ced97ac284a56a803e5d68bbee0de0ec2d792f3e3a51";

    /// <summary>Публичный ключ стороны A, вычисленный OpenSSL из <see cref="PrivateA"/>.</summary>
    private const string PublicA = "891671def54493b97418f710c01d228d7bfd240bc5aa549cc34d04cfcb00381c";

    /// <summary>Публичный ключ стороны B (OpenSSL).</summary>
    private const string PublicB = "f08a9ff5e3c4a2b71f8b09f463bbc1c427a2c240d919e3b338c8bb555997b220";

    /// <summary>Общий секрет A и B, вычисленный OpenSSL.</summary>
    private const string SharedAb = "f085e2e25aa77c2fc9c2549bf1779ca02c3c1f1dab1a8939dd27654f5716ad26";

    [Test]
    public void PublicKeyMatchesOpenSslReference()
    {
        var actual = X25519.GetPublicKey(Convert.FromHexString(PrivateA));

        Assert.That(Convert.ToHexString(actual), Is.EqualTo(PublicA).IgnoreCase);
    }

    [Test]
    public void SharedSecretMatchesOpenSslReference()
    {
        var actual = X25519.DeriveSharedSecret(Convert.FromHexString(PrivateA), Convert.FromHexString(PublicB));

        Assert.That(Convert.ToHexString(actual), Is.EqualTo(SharedAb).IgnoreCase);
    }

    [Test]
    public void SharedSecretIsCommutative()
    {
        var privateFirst = X25519.CreatePrivateKey();
        var privateSecond = X25519.CreatePrivateKey();

        var publicFirst = X25519.GetPublicKey(privateFirst);
        var publicSecond = X25519.GetPublicKey(privateSecond);

        var fromFirst = X25519.DeriveSharedSecret(privateFirst, publicSecond);
        var fromSecond = X25519.DeriveSharedSecret(privateSecond, publicFirst);

        // Коммутативность — сильнейшее свойство: почти любая ошибка в лестнице или редукции его
        // нарушает, поэтому проверка ловит и то, чего нет в одном фиксированном векторе.
        Assert.That(fromFirst, Is.EqualTo(fromSecond));
    }

    [Test]
    public void GeneratedPrivateKeyIsClamped()
    {
        var key = X25519.CreatePrivateKey();

        Assert.Multiple(() =>
        {
            Assert.That(key, Has.Length.EqualTo(32));
            Assert.That(key[0] & 0b111, Is.Zero, "младшие три бита обязаны быть сброшены");
            Assert.That(key[31] & 0b1000_0000, Is.Zero, "старший бит обязан быть сброшен");
            Assert.That(key[31] & 0b0100_0000, Is.Not.Zero, "бит 254 обязан быть установлен");
        });
    }

    [Test]
    public void DifferentPrivateKeysProduceDifferentPublicKeys()
    {
        var first = X25519.GetPublicKey(X25519.CreatePrivateKey());
        var second = X25519.GetPublicKey(X25519.CreatePrivateKey());

        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public void ScalarMultiplyRejectsWrongSizes()
    {
        var valid = new byte[32];

        Assert.Multiple(() =>
        {
            Assert.That(() => X25519.ScalarMultiply(new byte[31], valid), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => X25519.ScalarMultiply(valid, new byte[33]), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void LowOrderPointIsRejected()
    {
        // Нулевая координата даёт нулевой общий секрет: такую точку обязаны отвергать, иначе
        // соединение получило бы предсказуемый ключ.
        Assert.That(
            () => X25519.DeriveSharedSecret(X25519.CreatePrivateKey(), new byte[32]),
            Throws.InstanceOf<CryptographicException>());
    }
}
