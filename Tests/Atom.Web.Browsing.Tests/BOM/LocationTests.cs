namespace Atom.Web.Browsing.BOM.Tests;

public class LocationTests : BenchmarkTests<LocationTests>
{
    [Test]
    public void AboutBlankTest()
    {
        var location = new Location(new Uri("about:blank"));

        if (!IsBenchmarkEnabled)
        {
            Assert.Multiple(() =>
            {
                Assert.That(location.Hash, Is.Empty);
                Assert.That(location.Host, Is.Empty);
                Assert.That(location.HostName, Is.Empty);
                Assert.That(location.Href.ToString(), Is.EqualTo("about:blank"));
                Assert.That(location.Origin.ToString(), Is.EqualTo("null"));
                Assert.That(location.Path, Is.EqualTo("blank"));
                Assert.That(location.Port, Is.Empty);
                Assert.That(location.Protocol, Is.EqualTo("about:"));
                Assert.That(location.Search, Is.Empty);
            });
        }
    }
}