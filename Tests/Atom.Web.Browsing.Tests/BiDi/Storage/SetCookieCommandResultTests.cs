namespace Atom.Web.Browsing.BiDi.Storage;

using System.Text.Json;
using Atom.Web.Browsing.BiDi.JsonConverters;

[TestFixture]
public class SetCookieCommandResultTests
{
    [Test]
    public void TestCanDeserialize()
    {
        string json = """
                      {
                        "partition": {
                          "userContext": "myUserContext",
                          "sourceOrigin": "mySourceOrigin",
                          "extraPropertyName": "extraPropertyValue"
                        }
                      }
                      """;
        SetCookieCommandResult? result = JsonSerializer.Deserialize<SetCookieCommandResult>(json, JsonContext.Default.SetCookieCommandResult);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Partition, Is.Not.Null);
            Assert.That(result.Partition.UserContextId, Is.EqualTo("myUserContext"));
            Assert.That(result.Partition.SourceOrigin, Is.EqualTo("mySourceOrigin"));
            Assert.That(result.Partition.AdditionalData, Has.Count.EqualTo(1));
            Assert.That(result.Partition.AdditionalData, Contains.Key("extraPropertyName"));
            Assert.That(result.Partition.AdditionalData["extraPropertyName"], Is.EqualTo("extraPropertyValue"));
        });
    }

    [Test]
    public void TestCanDeserializeWithMissingData()
    {
        string json = """
                      {
                        "partition": {} 
                      }
                      """;
        SetCookieCommandResult? result = JsonSerializer.Deserialize<SetCookieCommandResult>(json, JsonContext.Default.SetCookieCommandResult);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Partition, Is.Not.Null);
            Assert.That(result.Partition.UserContextId, Is.Null);
            Assert.That(result.Partition.SourceOrigin, Is.Null);
            Assert.That(result.Partition.AdditionalData, Is.Empty);
        });
    }

    [Test]
    public void TestCanDeserializingWithMissingPartition()
    {
        string json = "{}";
        Assert.That(() => JsonSerializer.Deserialize<SetCookieCommandResult>(json, JsonContext.Default.SetCookieCommandResult), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializingWithInvalidPartitionDataTypeThrows()
    {
        string json = """
                      {
                        "partition": "invalidPartitionType"
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize<SetCookieCommandResult>(json, JsonContext.Default.SetCookieCommandResult), Throws.InstanceOf<JsonException>());
    }
}
