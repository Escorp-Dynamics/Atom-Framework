using System.Security.Cryptography;
using Atom.Net.Tls;

namespace Atom.Net.Tests.Tls;

/// <summary>
/// Проверяет журнал секретов TLS в формате NSS key log.
/// </summary>
/// <remarks>
/// Тесты трогают глобальное состояние процесса — переменную окружения и открытый файл журнала, —
/// поэтому класс помечен как непараллельный, а каждый тест возвращает состояние на место.
/// Расшифровать трафик Wireshark'ом внутри теста нечем, поэтому проверяется то, от чего эта
/// расшифровка зависит целиком: форма строки и факт, что по умолчанию не пишется НИЧЕГО.
/// </remarks>
[NonParallelizable]
[CancelAfter(TestTimeoutMs)]
public sealed class TlsKeyLogTests
{
    private const int TestTimeoutMs = 30000;

    private string? savedEnvironmentValue;
    private string? directory;

    [SetUp]
    public void SetUp()
    {
        savedEnvironmentValue = Environment.GetEnvironmentVariable(TlsKeyLog.EnvironmentVariableName);
        Environment.SetEnvironmentVariable(TlsKeyLog.EnvironmentVariableName, null);

        TlsKeyLog.FilePath = null;
        TlsKeyLog.Close();

        directory = Path.Combine(Path.GetTempPath(), "atom-keylog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    [TearDown]
    public void TearDown()
    {
        TlsKeyLog.FilePath = null;
        TlsKeyLog.Close();

        Environment.SetEnvironmentVariable(TlsKeyLog.EnvironmentVariableName, savedEnvironmentValue);

        try
        {
            if (directory is not null) Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Уборка временного каталога не является предметом теста.
        }
    }

    [Test]
    public void FormatsLineAsNssKeyLogRecord()
    {
        var clientRandom = Convert.FromHexString("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");
        var secret = Convert.FromHexString("aabbccdd");

        var formatted = TlsKeyLog.TryFormatLine(TlsKeyLog.ClientTrafficSecretLabel, clientRandom, secret, out var line);

        Assert.That(formatted, Is.True);
        Assert.That(line, Is.EqualTo("CLIENT_TRAFFIC_SECRET_0 000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f aabbccdd"));
    }

    [Test]
    public void RejectsRecordsWiresharkCannotUse()
    {
        var clientRandom = RandomNumberGenerator.GetBytes(32);
        var secret = RandomNumberGenerator.GetBytes(32);

        Assert.Multiple(() =>
        {
            // Случайное число иной длины связать с сессией нельзя — такая строка только засоряет журнал.
            Assert.That(TlsKeyLog.TryFormatLine(TlsKeyLog.ClientTrafficSecretLabel, RandomNumberGenerator.GetBytes(31), secret, out _), Is.False);
            Assert.That(TlsKeyLog.TryFormatLine(TlsKeyLog.ClientTrafficSecretLabel, [], secret, out _), Is.False);
            Assert.That(TlsKeyLog.TryFormatLine(TlsKeyLog.ClientTrafficSecretLabel, clientRandom, [], out _), Is.False);
            Assert.That(TlsKeyLog.TryFormatLine(" ", clientRandom, secret, out _), Is.False);
        });
    }

    [Test]
    public void IsDisabledWhenNeitherSettingNorEnvironmentVariableIsSet()
    {
        var candidate = Path.Combine(directory!, "must-not-appear.keys");

        TlsKeyLog.Write(TlsKeyLog.ClientTrafficSecretLabel, RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32));

        Assert.Multiple(() =>
        {
            Assert.That(TlsKeyLog.IsEnabled, Is.False);
            Assert.That(TlsKeyLog.FilePath, Is.Null);
            Assert.That(File.Exists(candidate), Is.False);
            Assert.That(Directory.GetFiles(directory!), Is.Empty);
        });
    }

    [Test]
    public void BlankEnvironmentVariableKeepsLogDisabled()
    {
        Environment.SetEnvironmentVariable(TlsKeyLog.EnvironmentVariableName, "   ");

        Assert.Multiple(() =>
        {
            Assert.That(TlsKeyLog.FilePath, Is.Null);
            Assert.That(TlsKeyLog.IsEnabled, Is.False);
            Assert.That(TlsKeyLog.Normalize(""), Is.Null);
            Assert.That(TlsKeyLog.Normalize("/tmp/keys.log"), Is.EqualTo("/tmp/keys.log"));
        });
    }

    [Test]
    public void EnvironmentVariableAloneTurnsLogOn()
    {
        var path = Path.Combine(directory!, "env.keys");
        Environment.SetEnvironmentVariable(TlsKeyLog.EnvironmentVariableName, path);

        Assert.That(TlsKeyLog.IsEnabled, Is.True);

        var clientRandom = RandomNumberGenerator.GetBytes(32);
        TlsKeyLog.Write(TlsKeyLog.ClientHandshakeTrafficSecretLabel, clientRandom, RandomNumberGenerator.GetBytes(32));
        TlsKeyLog.Close();

        var lines = File.ReadAllLines(path);

        Assert.That(lines, Has.Length.EqualTo(1));
        Assert.That(lines[0], Does.StartWith("CLIENT_HANDSHAKE_TRAFFIC_SECRET " + Convert.ToHexStringLower(clientRandom) + " "));
    }

    [Test]
    public void AppendsWithoutTruncatingPreviousSessions()
    {
        var path = Path.Combine(directory!, "append.keys");
        File.WriteAllText(path, "CLIENT_RANDOM 00 00\n");

        TlsKeyLog.FilePath = path;

        TlsKeyLog.Write(TlsKeyLog.ClientHandshakeTrafficSecretLabel, RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32));
        TlsKeyLog.Write(TlsKeyLog.ServerHandshakeTrafficSecretLabel, RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32));
        TlsKeyLog.Close();

        var lines = File.ReadAllLines(path);

        Assert.That(lines, Has.Length.EqualTo(3));
        Assert.That(lines[0], Is.EqualTo("CLIENT_RANDOM 00 00"));
        Assert.That(lines[1], Does.StartWith("CLIENT_HANDSHAKE_TRAFFIC_SECRET "));
        Assert.That(lines[2], Does.StartWith("SERVER_HANDSHAKE_TRAFFIC_SECRET "));
    }

    [Test]
    public void ExplicitSettingWinsOverEnvironmentVariable()
    {
        var fromEnvironment = Path.Combine(directory!, "environment.keys");
        var fromSetting = Path.Combine(directory!, "setting.keys");

        Environment.SetEnvironmentVariable(TlsKeyLog.EnvironmentVariableName, fromEnvironment);
        TlsKeyLog.FilePath = fromSetting;

        TlsKeyLog.Write(TlsKeyLog.ClientTrafficSecretLabel, RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32));
        TlsKeyLog.Close();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(fromSetting), Is.True);
            Assert.That(File.Exists(fromEnvironment), Is.False);
        });
    }

    [Test]
    public void UnwritableTargetNeverThrows()
    {
        // Каталог вместо файла — самый простой способ получить отказ при открытии. Соединение,
        // ради наблюдения за которым журнал и включают, обязано пережить такую ошибку настройки.
        TlsKeyLog.FilePath = directory;

        Assert.DoesNotThrow(() => TlsKeyLog.Write(TlsKeyLog.ClientTrafficSecretLabel, RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32)));
        Assert.That(TlsKeyLog.IsEnabled, Is.False);
    }

    [Test]
    public void ConcurrentWritersProduceWholeLines()
    {
        var path = Path.Combine(directory!, "concurrent.keys");
        TlsKeyLog.FilePath = path;

        const int writers = 8;
        const int perWriter = 32;

        Parallel.For(0, writers, _ =>
        {
            for (var i = 0; i < perWriter; i++)
                TlsKeyLog.Write(TlsKeyLog.ClientTrafficSecretLabel, RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32));
        });

        TlsKeyLog.Close();

        var lines = File.ReadAllLines(path);

        Assert.That(lines, Has.Length.EqualTo(writers * perWriter));
        Assert.That(lines, Is.All.Matches<string>(static line => line.StartsWith("CLIENT_TRAFFIC_SECRET_0 ", StringComparison.Ordinal) && line.Split(' ').Length is 3));
    }
}
