using System.Net.Sockets;

namespace Atom.Net.Tests.Tcp;

[CancelAfter(TestTimeoutMs * 2)]
public sealed class TcpStreamHappyEyeballsMemoryTests
{
    private const int TestTimeoutMs = 4000;

    [SetUp]
    public void SetUp() => Atom.Net.Tcp.TcpStream.ResetHappyEyeballsMemoryForTests();

    [Test]
    public void RememberStoresIpv4Preference()
    {
        Atom.Net.Tcp.TcpStream.RememberHappyEyeballsFamilyForTests("example.test", AddressFamily.InterNetwork);

        var found = Atom.Net.Tcp.TcpStream.TryGetHappyEyeballsFamilyForTests("example.test", out var preference);

        Assert.That(found, Is.True);
        Assert.That(preference, Is.EqualTo(4));
    }

    [Test]
    public void TryGetIsCaseInsensitiveForAsciiHost()
    {
        Atom.Net.Tcp.TcpStream.RememberHappyEyeballsFamilyForTests("LocalHost", AddressFamily.InterNetworkV6);

        var found = Atom.Net.Tcp.TcpStream.TryGetHappyEyeballsFamilyForTests("localhost", out var preference);

        Assert.That(found, Is.True);
        Assert.That(preference, Is.EqualTo(6));
    }

    [Test]
    public void UnknownAddressFamilyIsIgnored()
    {
        Atom.Net.Tcp.TcpStream.RememberHappyEyeballsFamilyForTests("ignored.test", AddressFamily.Unspecified);

        var found = Atom.Net.Tcp.TcpStream.TryGetHappyEyeballsFamilyForTests("ignored.test", out var preference);

        Assert.That(found, Is.False);
        Assert.That(preference, Is.Zero);
    }

    [Test]
    public void CounterOverflowDoesNotBreakRememberOrLookup()
    {
        Atom.Net.Tcp.TcpStream.SetHappyEyeballsCounterForTests(int.MaxValue - 1);

        Atom.Net.Tcp.TcpStream.RememberHappyEyeballsFamilyForTests("overflow-v4.test", AddressFamily.InterNetwork);
        Atom.Net.Tcp.TcpStream.RememberHappyEyeballsFamilyForTests("overflow-v6.test", AddressFamily.InterNetworkV6);

        var foundV4 = Atom.Net.Tcp.TcpStream.TryGetHappyEyeballsFamilyForTests("overflow-v4.test", out var preferenceV4);
        var foundV6 = Atom.Net.Tcp.TcpStream.TryGetHappyEyeballsFamilyForTests("overflow-v6.test", out var preferenceV6);

        Assert.That(foundV4, Is.True);
        Assert.That(preferenceV4, Is.EqualTo(4));
        Assert.That(foundV6, Is.True);
        Assert.That(preferenceV6, Is.EqualTo(6));
    }
}