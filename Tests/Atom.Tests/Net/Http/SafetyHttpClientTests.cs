//using Atom.Net.Http;

namespace Atom.Tests.Net.Http;

[TestFixture]
public class SafetyHttpClientTests
{
    /*[Test]
    public async Task SimpleTest()
    {
        using var client = new SafetyHttpClient();
        using var response = await client.GetAsync(new Uri("Https://www.google.com/"));
        Assert.That(response.IsSuccessStatusCode, Is.True);
    }*/

    /*[Test]
    public async Task GetAsyncEnumerableTest()
    {
        using var client = new SafetyHttpClient();

        await foreach (var item in client.GetAsyncEnumerable(new Uri("Https://www.google.com/"), JsonHttpContext.Default.String))
            Assert.That(item, Is.Not.Null);
    }*/
}