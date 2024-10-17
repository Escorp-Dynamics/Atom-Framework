using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atom.Web.Proxies.Tests;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    WriteIndented = true
)]
[JsonSerializable(typeof(Proxy))]
public partial class JsonProxyTestContext : JsonSerializerContext;

[TestFixture]
public class ProxyTests
{
    [Test]
    public void SerializeTest()
    {
        var proxy = new Proxy("localhost", 80, "login", "password")
        {
            Anonymity = AnonymityLevel.High
        };
        
        var json = JsonSerializer.Serialize(proxy, JsonProxyTestContext.Default.Proxy);
        Assert.That(json, Is.EqualTo("{\n  \"host\": \"localhost\",\n  \"port\": 80,\n  \"userName\": \"login\",\n  \"password\": \"password\",\n  \"anonymity\": 3\n}"));
    }
}