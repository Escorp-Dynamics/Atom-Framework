namespace Atom.Web.Browsers.Tests;

[TestFixture]
public class DOMParserTests
{
    [Test]
    public void BaseTest()
    {
        var html = "<!DOCTYPE html><html><head><title>TEST</title></head><body><div class=\"test\"></div></body></html>";
        var document = DOMParser.Parse(new Uri("https://google.com"), html);

        Assert.That(document.DocType?.Name, Is.EqualTo("html"));
        //Assert.That(document.Head.Title, Is.EqualTo("TEST"));
    }
}