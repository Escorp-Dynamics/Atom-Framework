namespace Atom.Web.Browsing.BiDi.Input;

using System.Text.Json;
using Newtonsoft.Json.Linq;
using Atom.Web.Browsing.BiDi.JsonConverters;
using Atom.Web.Browsing.BiDi.Script;

[TestFixture]
public class PointerMoveActionTests
{
    [Test]
    public void TestCanSerializeParameters()
    {
        PointerMoveAction properties = new();
        string json = JsonSerializer.Serialize(properties, JsonContext.Default.PointerMoveAction);
        JObject serialized = JObject.Parse(json);
        Assert.That(serialized, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(serialized, Contains.Key("type"));
            Assert.That(serialized["type"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["type"]!.Value<string>(), Is.EqualTo("pointerMove"));
            Assert.That(serialized, Contains.Key("x"));
            Assert.That(serialized["x"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["x"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(serialized, Contains.Key("y"));
            Assert.That(serialized["y"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["y"]!.Value<long>(), Is.EqualTo(0));
        });
    }

    [Test]
    public void TestCanSerializeParametersWithOptionalDuration()
    {
        PointerMoveAction properties = new()
        {
            Duration = TimeSpan.FromMilliseconds(1),
        };
        string json = JsonSerializer.Serialize(properties, JsonContext.Default.PointerMoveAction);
        JObject serialized = JObject.Parse(json);
        Assert.That(serialized, Has.Count.EqualTo(4));
        Assert.Multiple(() =>
        {
            Assert.That(serialized, Contains.Key("type"));
            Assert.That(serialized["type"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["type"]!.Value<string>(), Is.EqualTo("pointerMove"));
            Assert.That(serialized, Contains.Key("x"));
            Assert.That(serialized["x"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["x"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(serialized, Contains.Key("y"));
            Assert.That(serialized["y"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["y"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(serialized, Contains.Key("duration"));
            Assert.That(serialized["duration"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["duration"]!.Value<long>(), Is.EqualTo(1));
        });
    }

    [Test]
    public void TestCanSerializeParametersWithOptionalViewportOrigin()
    {
        PointerMoveAction properties = new()
        {
            Origin = Origin.Viewport
        };
        string json = JsonSerializer.Serialize(properties, JsonContext.Default.PointerMoveAction);
        JObject serialized = JObject.Parse(json);
        Assert.That(serialized, Has.Count.EqualTo(4));
        Assert.Multiple(() =>
        {
            Assert.That(serialized, Contains.Key("type"));
            Assert.That(serialized["type"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["type"]!.Value<string>(), Is.EqualTo("pointerMove"));
            Assert.That(serialized, Contains.Key("x"));
            Assert.That(serialized["x"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["x"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(serialized, Contains.Key("y"));
            Assert.That(serialized["y"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["y"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(serialized, Contains.Key("origin"));
            Assert.That(serialized["origin"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["origin"]!.Value<string>(), Is.EqualTo("viewport"));
        });
    }

    [Test]
    public void TestCanSerializeParametersWithOptionalPointerOrigin()
    {
        PointerMoveAction properties = new()
        {
            Origin = Origin.Pointer
        };
        string json = JsonSerializer.Serialize(properties, JsonContext.Default.PointerMoveAction);
        JObject serialized = JObject.Parse(json);
        Assert.That(serialized, Has.Count.EqualTo(4));
        Assert.Multiple(() =>
        {
            Assert.That(serialized, Contains.Key("type"));
            Assert.That(serialized["type"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["type"]!.Value<string>(), Is.EqualTo("pointerMove"));
            Assert.That(serialized, Contains.Key("x"));
            Assert.That(serialized["x"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["x"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(serialized, Contains.Key("y"));
            Assert.That(serialized["y"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["y"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(serialized, Contains.Key("origin"));
            Assert.That(serialized["origin"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["origin"]!.Value<string>(), Is.EqualTo("pointer"));
        });
    }

    [Test]
    public void TestCanSerializeParametersWithOptionalElementOrigin()
    {
        string nodeJson = """
                          {
                            "type": "node",
                            "value": {
                              "nodeType": 1,
                              "childNodeCount": 0 
                            },
                            "sharedId": "testSharedId"
                          }
                          """;
        SharedReference node = JsonSerializer.Deserialize<RemoteValue>(nodeJson, JsonContext.Default.RemoteValue)!.ToSharedReference();
        PointerMoveAction properties = new()
        {
            Origin = Origin.Element(new ElementOrigin(node))
        };
        string json = JsonSerializer.Serialize(properties, JsonContext.Default.PointerMoveAction);
        JObject serialized = JObject.Parse(json);
        Assert.That(serialized, Has.Count.EqualTo(4));
        Assert.Multiple(() =>
        {
            Assert.That(serialized, Contains.Key("type"));
            Assert.That(serialized["type"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["type"]!.Value<string>(), Is.EqualTo("pointerMove"));
            Assert.That(serialized, Contains.Key("x"));
            Assert.That(serialized["x"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["x"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(serialized, Contains.Key("y"));
            Assert.That(serialized["y"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["y"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(serialized, Contains.Key("origin"));
            Assert.That(serialized["origin"]!.Type, Is.EqualTo(JTokenType.Object));
        });
        JObject originObject = (JObject)serialized["origin"]!;
        Assert.That(originObject, Has.Count.EqualTo(2));
        Assert.That(originObject, Contains.Key("type"));
        Assert.Multiple(() =>
        {
            Assert.That(originObject["type"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(originObject["type"]!.Value<string>(), Is.EqualTo("element"));
            Assert.That(originObject, Contains.Key("element"));
            Assert.That(originObject["element"]!.Type, Is.EqualTo(JTokenType.Object));
            JObject elementObject = (JObject)originObject["element"]!;
            Assert.That(elementObject, Has.Count.EqualTo(1));
            Assert.That(elementObject, Contains.Key("sharedId"));
            Assert.That(elementObject["sharedId"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(elementObject["sharedId"]!.Value<string>(), Is.EqualTo("testSharedId"));
        });
    }

    [Test]
    public void TestCanSerializeParametersWithOptionalWidthAndHeight()
    {
        PointerMoveAction properties = new()
        {
            Width = 1,
            Height = 1,
        };
        string json = JsonSerializer.Serialize(properties, JsonContext.Default.PointerMoveAction);
        JObject serialized = JObject.Parse(json);
        Assert.That(serialized, Has.Count.EqualTo(5));
        Assert.Multiple(() =>
        {
            Assert.That(serialized, Contains.Key("type"));
            Assert.That(serialized["type"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["type"]!.Value<string>(), Is.EqualTo("pointerMove"));
            Assert.That(serialized, Contains.Key("x"));
            Assert.That(serialized["x"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["x"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(serialized, Contains.Key("y"));
            Assert.That(serialized["y"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["y"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(serialized, Contains.Key("width"));
            Assert.That(serialized["width"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["width"]!.Value<long>(), Is.EqualTo(1));
            Assert.That(serialized, Contains.Key("height"));
            Assert.That(serialized["height"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["height"]!.Value<long>(), Is.EqualTo(1));
        });
    }

    [Test]
    public void TestCanSerializeParametersWithOptionalPressureProperties()
    {
        PointerMoveAction properties = new()
        {
            Pressure = 1,
            TangentialPressure = 1,
        };
        string json = JsonSerializer.Serialize(properties, JsonContext.Default.PointerMoveAction);
        JObject serialized = JObject.Parse(json);
        Assert.That(serialized, Has.Count.EqualTo(5));
        Assert.Multiple(() =>
        {
            Assert.That(serialized, Contains.Key("type"));
            Assert.That(serialized["type"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["type"]!.Value<string>(), Is.EqualTo("pointerMove"));
            Assert.That(serialized, Contains.Key("x"));
            Assert.That(serialized["x"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["x"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(serialized, Contains.Key("y"));
            Assert.That(serialized["y"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["y"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(serialized, Contains.Key("pressure"));
            Assert.That(serialized["pressure"]!.Type, Is.EqualTo(JTokenType.Float));
            Assert.That(serialized["pressure"]!.Value<double>(), Is.EqualTo(1));
            Assert.That(serialized, Contains.Key("tangentialPressure"));
            Assert.That(serialized["tangentialPressure"]!.Type, Is.EqualTo(JTokenType.Float));
            Assert.That(serialized["tangentialPressure"]!.Value<double>(), Is.EqualTo(1));
        });
    }

    [Test]
    public void TestCanSerializeParametersWithOptionalTwistProperty()
    {
        PointerMoveAction properties = new()
        {
            Twist = 1,
        };
        string json = JsonSerializer.Serialize(properties, JsonContext.Default.PointerMoveAction);
        JObject serialized = JObject.Parse(json);
        Assert.That(serialized, Has.Count.EqualTo(4));
        Assert.Multiple(() =>
        {
            Assert.That(serialized, Contains.Key("type"));
            Assert.That(serialized["type"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["type"]!.Value<string>(), Is.EqualTo("pointerMove"));
            Assert.That(serialized, Contains.Key("x"));
            Assert.That(serialized["x"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["x"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(serialized, Contains.Key("y"));
            Assert.That(serialized["y"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["y"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(serialized, Contains.Key("twist"));
            Assert.That(serialized["twist"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["twist"]!.Value<ulong>(), Is.EqualTo(1));
        });
    }

    [Test]
    public void TestSettingTwistPropertyToInvalidValueThrows()
    {
        PointerMoveAction properties = new();
        Assert.That(() => properties.Twist = 360, Throws.InstanceOf<BiDiException>().With.Message.EqualTo("Значение Twist должно быть между 0 и 359"));
    }

    [Test]
    public void TestCanSerializeParametersWithOptionalAngleProperties()
    {
        PointerMoveAction properties = new()
        {
            AzimuthAngle = 1,
            AltitudeAngle = 1,
        };
        string json = JsonSerializer.Serialize(properties, JsonContext.Default.PointerMoveAction);
        JObject serialized = JObject.Parse(json);
        Assert.That(serialized, Has.Count.EqualTo(5));
        Assert.Multiple(() =>
        {
            Assert.That(serialized, Contains.Key("type"));
            Assert.That(serialized["type"]!.Type, Is.EqualTo(JTokenType.String));
            Assert.That(serialized["type"]!.Value<string>(), Is.EqualTo("pointerMove"));
            Assert.That(serialized, Contains.Key("x"));
            Assert.That(serialized["x"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["x"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(serialized, Contains.Key("y"));
            Assert.That(serialized["y"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(serialized["y"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(serialized, Contains.Key("altitudeAngle"));
            Assert.That(serialized["altitudeAngle"]!.Type, Is.EqualTo(JTokenType.Float));
            Assert.That(serialized["altitudeAngle"]!.Value<double>(), Is.EqualTo(1));
            Assert.That(serialized, Contains.Key("azimuthAngle"));
            Assert.That(serialized["azimuthAngle"]!.Type, Is.EqualTo(JTokenType.Float));
            Assert.That(serialized["azimuthAngle"]!.Value<double>(), Is.EqualTo(1));
        });
    }

    [Test]
    public void TestSettingAnglePropertiesToInvalidValueThrows()
    {
        PointerDownAction properties = new(0);
        Assert.Multiple(() =>
        {
            Assert.That(() => properties.AltitudeAngle = -0.01, Throws.InstanceOf<BiDiException>().With.Message.EqualTo("Значение AltitudeAngle должно быть между 0 и 1.5707963267948966 (pi / 2) включительно"));
            Assert.That(() => properties.AltitudeAngle = 1.58, Throws.InstanceOf<BiDiException>().With.Message.EqualTo("Значение AltitudeAngle должно быть между 0 и 1.5707963267948966 (pi / 2) включительно"));
            Assert.That(() => properties.AzimuthAngle = -0.01, Throws.InstanceOf<BiDiException>().With.Message.EqualTo("Значение AzimuthAngle должно быть между 0 и 6.283185307179586 (2 * pi) включительно"));
            Assert.That(() => properties.AzimuthAngle = 6.29, Throws.InstanceOf<BiDiException>().With.Message.EqualTo("Значение AzimuthAngle должно быть между 0 и 6.283185307179586 (2 * pi) включительно"));
        });
    }
}
