using Atom.Net.Http;

using System.Net.Http.Json;

namespace Atom.Tests.Net.Http;

[TestFixture]
public class HttpTests
{
    [Test]
    public async Task AsJsonTest()
    {
        var form = new Dictionary<string, object?>
        {
            { "param1", "test" },
            { "param2", true },
            { "param3", 5 }
        }.AsReadOnly();

        using var content = JsonContent.Create(form!, JsonHttpContext.Default.Form);
        var form2 = await content.AsJsonAsync(JsonHttpContext.Default.Form);

        Assert.That(form2, Is.EquivalentTo(form));
    }

    [Test]
    public async Task AsJsonEnumerableTest()
    {
        var data = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                { "param1", "test" },
                { "param2", true },
                { "param3", 5 }
            }.AsReadOnly(),
            new Dictionary<string, object?>
            {
                { "param1", "test" },
                { "param2", true },
                { "param3", 5 }
            }.AsReadOnly()
        };

        using var content = JsonContent.Create(data!, JsonTestsContext.Default.Forms);
        var i = 0;

        await foreach (var item in content.AsJsonAsyncEnumerable(JsonHttpContext.Default.Form))
            Assert.That(item, Is.EquivalentTo(data[i++]));
    }
}