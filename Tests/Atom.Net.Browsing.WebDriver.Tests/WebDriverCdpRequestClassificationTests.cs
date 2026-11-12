namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
public sealed class WebDriverCdpRequestClassificationTests
{
    [TestCase("Document", "page-1.1", "page-1", ExpectedResult = "main_frame")]
    [TestCase("Document", "page-1.12", "page-1", ExpectedResult = "main_frame")]
    [TestCase("Document", "fetch-3", "page-1", ExpectedResult = "sub_frame")]
    [TestCase("Document", "page-10.1", "page-1", ExpectedResult = "sub_frame")]
    [TestCase("Fetch", "page-1.1", "page-1", ExpectedResult = "xmlhttprequest")]
    [TestCase("Script", "fetch-4", "page-1", ExpectedResult = "script")]
    public string ClassifiesObscuraRequests(string cdpType, string requestId, string targetId)
        => CdpBrowserCommandTransport.MapResourceType(cdpType, CdpBrowserCommandTransport.IsMainFrameRequest(cdpType, requestId, targetId));
}
