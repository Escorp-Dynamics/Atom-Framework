namespace Atom.Web.Browsing.BiDi.Bluetooth;

using System.Text.Json;
using Atom.Web.Browsing.BiDi.JsonConverters;

[TestFixture]
public class RequestDevicePromptUpdatedEventArgsTests
{
    [Test]
    public void TestCanDeserialize()
    {
        string json = """
                      {
                        "context": "myContextId",
                        "prompt": "myPromptId",
                        "devices": []
                      }
                      """;
        RequestDevicePromptUpdatedEventArgs? eventArgs = JsonSerializer.Deserialize(json, JsonContext.Default.RequestDevicePromptUpdatedEventArgs);
        Assert.Multiple(() =>
        {
            Assert.That(eventArgs!.BrowsingContextId, Is.EqualTo("myContextId"));
            Assert.That(eventArgs.Prompt, Is.EqualTo("myPromptId"));
            Assert.That(eventArgs.Devices.ToArray(), Has.Length.EqualTo(0));
        });
    }

    [Test]
    public void TestCanDeserializeWithDevices()
    {
        string json = """
                      {
                        "context": "myContextId",
                        "prompt": "myPromptId",
                        "devices": [
                          {
                            "id": "myDeviceId",
                            "name": "myDeviceName"
                          }
                        ]
                      }
                      """;
        RequestDevicePromptUpdatedEventArgs? eventArgs = JsonSerializer.Deserialize(json, JsonContext.Default.RequestDevicePromptUpdatedEventArgs);
        Assert.Multiple(() =>
        {
            Assert.That(eventArgs!.BrowsingContextId, Is.EqualTo("myContextId"));
            Assert.That(eventArgs.Prompt, Is.EqualTo("myPromptId"));
            Assert.That(eventArgs.Devices.ToArray(), Has.Length.EqualTo(1));
            Assert.That(eventArgs.Devices.First().DeviceId, Is.EqualTo("myDeviceId"));
            Assert.That(eventArgs.Devices.First().DeviceName, Is.EqualTo("myDeviceName"));
        });
    }

    [Test]
    public void TestDeserializingWithMissingContextThrows()
    {
        string json = """
                      {
                        "prompt": "myPromptId",
                        "devices": []
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.RequestDevicePromptUpdatedEventArgs), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializingWithInvalidContextTypeThrows()
    {
        string json = """
                      {
                        "context": {},
                        "prompt": "myPromptId",
                        "devices": []
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.RequestDevicePromptUpdatedEventArgs), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializingWithMissingPromptThrows()
    {
        string json = """
                      {
                        "context": "myContextId",
                        "devices": []
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.RequestDevicePromptUpdatedEventArgs), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializingWithInvalidPromptTypeThrows()
    {
        string json = """
                      {
                        "context": "myContextId",
                        "prompt": {},
                        "devices": []
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.RequestDevicePromptUpdatedEventArgs), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializingWithMissingDevicesThrows()
    {
        string json = """
                      {
                        "context": "myContextId",
                        "prompt": "myPromptId"
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.RequestDevicePromptUpdatedEventArgs), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void TestDeserializingWithInvalidDevicesTypeThrows()
    {
        string json = """
                      {
                        "context": "myContextId",
                        "prompt": "myPromptId",
                        "devices": "someDevice"
                      }
                      """;
        Assert.That(() => JsonSerializer.Deserialize(json, JsonContext.Default.RequestDevicePromptUpdatedEventArgs), Throws.InstanceOf<JsonException>());
    }
}
