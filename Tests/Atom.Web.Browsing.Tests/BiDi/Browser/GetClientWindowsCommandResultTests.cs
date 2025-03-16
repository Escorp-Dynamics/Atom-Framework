namespace Atom.Web.Browsing.BiDi.Browser;

using System.Text.Json;
using Atom.Web.Browsing.BiDi.JsonConverters;

[TestFixture]
public class GetClientWindowsCommandResultTests
{
    [Test]
    public void TestCanDeserialize()
    {
        string json = """
                      {
                        "clientWindows": [
                          {
                            "clientWindow": "myClientWindow",
                            "active": true,
                            "state": "normal",
                            "x": 100,
                            "y": 200,
                            "width": 300,
                            "height": 400
                          }
                        ]
                      }
                      """;
        GetClientWindowsCommandResult? result = JsonSerializer.Deserialize<GetClientWindowsCommandResult>(json, JsonContext.Default.GetClientWindowsCommandResult);
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.ClientWindows.ToArray(), Has.Length.EqualTo(1));
            Assert.That(result.ClientWindows.First().ClientWindowId, Is.EqualTo("myClientWindow"));
            Assert.That(result.ClientWindows.First().State, Is.EqualTo(ClientWindowState.Normal));
            Assert.That(result.ClientWindows.First().IsActive, Is.True);
            Assert.That(result.ClientWindows.First().X, Is.EqualTo(100));
            Assert.That(result.ClientWindows.First().Y, Is.EqualTo(200));
            Assert.That(result.ClientWindows.First().Width, Is.EqualTo(300));
            Assert.That(result.ClientWindows.First().Height, Is.EqualTo(400));
        });
    }

    [Test]
    public void TestCanDeserializeWithEmptyList()
    {
        string json = """
                      {
                        "clientWindows": []
                      }
                      """;
        GetClientWindowsCommandResult? result = JsonSerializer.Deserialize<GetClientWindowsCommandResult>(json, JsonContext.Default.GetClientWindowsCommandResult);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ClientWindows, Is.Empty);
    }
}
