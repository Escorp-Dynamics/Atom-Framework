namespace Atom.Tests;

[TestFixture]
public class ExtensionsTests
{
    [Test]
    public void GetFriendlyNameTest()
    {
        var type = typeof(int).GetFriendlyName();
        Assert.That(type, Is.EqualTo("int"));

        type = typeof(int).GetFriendlyName(default);
        Assert.That(type, Is.EqualTo("int"));

        type = typeof(int?).GetFriendlyName(default);
        Assert.That(type, Is.EqualTo("int?"));

        type = typeof(int?).GetFriendlyName();
        Assert.That(type, Is.EqualTo("int?"));
    }
}