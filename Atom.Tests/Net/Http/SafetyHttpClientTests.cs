using Atom.Net.Http;

namespace Atom.Tests.Net.Http;

public class SafetyHttpClientTests
{
    [Fact]
    public async Task SimpleTest()
    {
        using var client = new SafetyHttpClient();
        using var response = await client.GetAsync(new Uri("Https://www.google.com/"));
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task GetAsyncEnumerableTest()
    {
        using var client = new SafetyHttpClient();

        await foreach (var item in client.GetAsyncEnumerable(new Uri("Https://www.google.com/"), JsonHttpContext.Default.String))
            Assert.True(item is not null);
    }
}