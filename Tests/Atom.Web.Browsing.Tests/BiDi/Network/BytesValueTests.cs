namespace Atom.Web.Browsing.BiDi.Network;

using System.Text;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using Atom.Web.Browsing.BiDi.JsonConverters;

[TestFixture]
public class BytesValueTests
{
    [Test]
    public void TestCanSerializeStringValue()
    {
        BytesValue value = BytesValue.FromString("this is my string");
        string json = JsonSerializer.Serialize(value, JsonContext.Default.BytesValue);
        JObject serialized = JObject.Parse(json);
        Assert.Multiple(() =>
        {
            Assert.That(serialized, Has.Count.EqualTo(2));
            Assert.That(serialized, Contains.Key("type"));
            Assert.That(serialized["type"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["type"]!.Value<string>(), Is.EqualTo("string"));
            Assert.That(serialized, Contains.Key("value"));
            Assert.That(serialized["value"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["value"]!.Value<string>(), Is.EqualTo("this is my string"));
        });
    }

    [Test]
    public void TestCanSerializeBase64Value()
    {
        string base64String = Convert.ToBase64String(Encoding.UTF8.GetBytes("this is my string"));
        BytesValue value = BytesValue.FromBase64String(base64String);
        string json = JsonSerializer.Serialize(value, JsonContext.Default.BytesValue);
        JObject serialized = JObject.Parse(json);
        Assert.Multiple(() =>
        {
            Assert.That(serialized, Has.Count.EqualTo(2));
            Assert.That(serialized, Contains.Key("type"));
            Assert.That(serialized["type"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["type"]!.Value<string>(), Is.EqualTo("base64"));
            Assert.That(serialized, Contains.Key("value"));
            Assert.That(serialized["value"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["value"]!.Value<string>(), Is.EqualTo(base64String));
        });
    }

    [Test]
    public void TestCanSerializeBase64ValueFromByteArray()
    {
        byte[] byteArray = Encoding.UTF8.GetBytes("this is my string");
        string base64String = Convert.ToBase64String(byteArray);
        BytesValue value = BytesValue.FromByteArray(byteArray);
        string json = JsonSerializer.Serialize(value, JsonContext.Default.BytesValue);
        JObject serialized = JObject.Parse(json);
        Assert.Multiple(() =>
        {
            Assert.That(serialized, Has.Count.EqualTo(2));
            Assert.That(serialized, Contains.Key("type"));
            Assert.That(serialized["type"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["type"]!.Value<string>(), Is.EqualTo("base64"));
            Assert.That(serialized, Contains.Key("value"));
            Assert.That(serialized["value"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["value"]!.Value<string>(), Is.EqualTo(base64String));
        });
    }

    [Test]
    public void TestCanDeserializeStringValue()
    {
        string stringValue = "this is my string";
        ReadOnlyMemory<byte> valueArray = Encoding.UTF8.GetBytes(stringValue);
        string json = $$"""
                      {
                        "type": "string",
                        "value": "{{stringValue}}"
                      }
                      """;
        BytesValue? value = JsonSerializer.Deserialize<BytesValue>(json, JsonContext.Default.BytesValue);
        Assert.Multiple(() =>
        {
            Assert.That(value, Is.Not.Null);
            Assert.That(value!.Type, Is.EqualTo(BytesValueType.String));
            Assert.That(value.Value, Is.EqualTo("this is my string"));
            Assert.That(value.ValueAsByteArray.ToArray(), Is.EqualTo(valueArray.ToArray()));
        });
    }

    [Test]
    public void TestCanDeserializeBase64Value()
    {
        // Disable spell checking only for the base64-encoded value.
        // cspell: disable-next
        string base64Value = "dGhpcyBpcyBteSBzdHJpbmc=";
        ReadOnlyMemory<byte> valueArray = Convert.FromBase64String(base64Value);
        string json = $$"""
                      {
                        "type": "base64",
                        "value": "{{base64Value}}"
                      }
                      """;
        BytesValue? value = JsonSerializer.Deserialize<BytesValue>(json, JsonContext.Default.BytesValue);
        Assert.Multiple(() =>
        {
            Assert.That(value, Is.Not.Null);
            Assert.That(value!.Type, Is.EqualTo(BytesValueType.Base64));

            // Disable spell checking only for the base64-encoded value.
            // cspell: disable-next
            Assert.That(value.Value, Is.EqualTo("dGhpcyBpcyBteSBzdHJpbmc="));
            Assert.That(value.ValueAsByteArray.ToArray(), Is.EqualTo(valueArray.ToArray()));
        });
    }

    [Test]
    public void TestDeserializeWithMissingTypeThrows()
    {
        string json = """
                      {
                        "value": "this is my string"
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize<BytesValue>(json, JsonContext.Default.BytesValue), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializeWithInvalidTypeValueThrows()
    {
        string json = $$"""
                      {
                        "type": "invalid",
                        "value": "this is my string"
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize<BytesValue>(json, JsonContext.Default.BytesValue), Throws.InstanceOf<BiDiException>());
    }

    [Test]
    public void TestDeserializeWithInvalidTypeThrows()
    {
        string json = $$"""
                      {
                        "type": [],
                        "value": "this is my string"
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize<BytesValue>(json, JsonContext.Default.BytesValue), Throws.InstanceOf<BiDiException>());
    }

    [Test]
    public void TestDeserializeWithMissingValueThrows()
    {
        string json = $$"""
                      {
                        "type": "string"
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize<BytesValue>(json, JsonContext.Default.BytesValue), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializeWithInvalidValueThrows()
    {
        string json = $$"""
                      {
                        "type": "base64",
                        "value": []
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize<BytesValue>(json, JsonContext.Default.BytesValue), Throws.InstanceOf<JsonException>());
    }
}
