namespace Atom.Web.Browsing.BiDi.Bluetooth;

using System.Text.Json;
using Newtonsoft.Json.Linq;

[TestFixture]
public class BluetoothManufacturerDataTests
{
    [Test]
    public void TestCanSerialize()
    {
        var properties = new BluetoothManufacturerData(123, "myData");
        var json = JsonSerializer.Serialize(properties, JsonContext.Default.BluetoothManufacturerData);
        var serialized = JObject.Parse(json);

        Assert.That(serialized, Has.Count.EqualTo(2));

        Assert.Multiple(() =>
        {
            Assert.That(serialized, Contains.Key("key"));
            Assert.That(serialized["key"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["key"]!.Value<uint>(), Is.EqualTo(123));
            Assert.That(serialized, Contains.Key("data"));
            Assert.That(serialized["data"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["data"]!.Value<string>(), Is.EqualTo("myData"));
        });
    }

    [Test]
    public void TestCanUpdatePropertiesAfterInstantiation()
    {
        var properties = new BluetoothManufacturerData(123, "myData")
        {
            Key = 456,
            Data = "myUpdatedData"
        };

        var json = JsonSerializer.Serialize(properties, JsonContext.Default.BluetoothManufacturerData);
        var serialized = JObject.Parse(json);

        Assert.That(serialized, Has.Count.EqualTo(2));

        Assert.Multiple(() =>
        {
            Assert.That(serialized, Contains.Key("key"));
            Assert.That(serialized["key"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["key"]!.Value<uint>(), Is.EqualTo(456));
            Assert.That(serialized, Contains.Key("data"));
            Assert.That(serialized["data"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["data"]!.Value<string>(), Is.EqualTo("myUpdatedData"));
        });
    }
}