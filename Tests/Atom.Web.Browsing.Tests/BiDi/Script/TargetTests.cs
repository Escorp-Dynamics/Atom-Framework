namespace Atom.Web.Browsing.BiDi.Script;

using System.Text.Json;
using Newtonsoft.Json.Linq;
using Atom.Web.Browsing.BiDi.JsonConverters;

[TestFixture]
public class TargetTests
{
    [Test]
    public void TestCanDeserializeRealmTarget()
    {
        string json = @"{ ""realm"": ""myRealm"" }";
        Target? target = JsonSerializer.Deserialize<Target>(json, JsonContext.Default.Target);
        Assert.That(target, Is.Not.Null);
        Assert.That(target, Is.InstanceOf<RealmTarget>());
        RealmTarget realmTarget = (RealmTarget)target!;
        Assert.That(realmTarget.RealmId, Is.EqualTo("myRealm"));
    }

    [Test]
    public void TestCanDeserializeContextTarget()
    {
        string json = @"{ ""context"": ""myContext"" }";
        Target? target = JsonSerializer.Deserialize<Target>(json, JsonContext.Default.Target);
        Assert.That(target, Is.Not.Null);
        Assert.That(target, Is.InstanceOf<ContextTarget>());
        ContextTarget contextTarget = (ContextTarget)target!;
        Assert.Multiple(() =>
        {
            Assert.That(contextTarget.BrowsingContextId, Is.EqualTo("myContext"));
            Assert.That(contextTarget.Sandbox, Is.Null);
        });
    }

    [Test]
    public void TestCanDeserializeContextTargetWithSandbox()
    {
        string json = @"{ ""context"": ""myContext"", ""sandbox"": ""mySandbox"" }";
        Target? target = JsonSerializer.Deserialize<Target>(json, JsonContext.Default.Target);
        Assert.That(target, Is.Not.Null);
        Assert.That(target, Is.InstanceOf<ContextTarget>());
        ContextTarget contextTarget = (ContextTarget)target!;
        Assert.Multiple(() =>
        {
            Assert.That(contextTarget.BrowsingContextId, Is.EqualTo("myContext"));
            Assert.That(contextTarget.Sandbox, Is.EqualTo("mySandbox"));
        });
    }

    [Test]
    public void TestDeserializationOfInvalidJsonThrows()
    {
        string json = @"{ ""invalid"": ""invalidValue"" }";
        Assert.That(() => JsonSerializer.Deserialize<ContextTarget>(json, JsonContext.Default.ContextTarget), Throws.InstanceOf<JsonException>().With.Message.Contains("missing required properties including: 'context'"));
        Assert.That(() => JsonSerializer.Deserialize<RealmTarget>(json, JsonContext.Default.RealmTarget), Throws.InstanceOf<JsonException>().With.Message.Contains("missing required properties including: 'realm'"));
        Assert.That(() => JsonSerializer.Deserialize<Target>(json, JsonContext.Default.Target), Throws.InstanceOf<JsonException>().With.Message.Contains("ScriptTarget должен содержать либо свойство 'realm', либо свойство 'context'"));
        Assert.That(() => JsonSerializer.Deserialize<Target>(@"[ ""invalid target"" ]", JsonContext.Default.Target), Throws.InstanceOf<JsonException>().With.Message.Contains("JSON цели скрипта должен быть объектом, но был Array"));
    }

    [Test]
    public void TestCanSerializeRealmTarget()
    {
        Target target = new RealmTarget("myRealm");
        string json = JsonSerializer.Serialize(target, JsonContext.Default.RealmTarget);
        JObject deserialized = JObject.Parse(json);
        Assert.That(deserialized, Has.Count.EqualTo(1));
        Assert.That(deserialized, Contains.Key("realm"));
        JToken realmValue = deserialized.GetValue("realm")!;
        Assert.Multiple(() =>
        {
            Assert.That(realmValue.Type, Is.EqualTo(JTokenType.String));
            Assert.That((string?)realmValue, Is.EqualTo("myRealm"));
        });
    }

    [Test]
    public void TestCanSerializeContextTarget()
    {
        Target target = new ContextTarget("myContext")
        {
            Sandbox = "mySandbox"
        };
        string json = JsonSerializer.Serialize(target, JsonContext.Default.ContextTarget);
        JObject deserialized = JObject.Parse(json);
        Assert.That(deserialized, Has.Count.EqualTo(2));
        Assert.That(deserialized, Contains.Key("context"));
        JToken contextValue = deserialized.GetValue("context")!;
        JToken sandboxValue = deserialized.GetValue("sandbox")!;
        
        Assert.Multiple(() =>
        {
            Assert.That(contextValue.Type, Is.EqualTo(JTokenType.String));
            Assert.That((string?)contextValue, Is.EqualTo("myContext"));
            Assert.That(sandboxValue.Type, Is.EqualTo(JTokenType.String));
            Assert.That((string?)sandboxValue, Is.EqualTo("mySandbox"));
        });
    }
}
