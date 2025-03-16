namespace Atom.Web.Browsing.BiDi.Input;

using Atom.Web.Browsing.BiDi.Script;
using Atom.Web.Browsing.BiDi.TestUtilities;

[TestFixture]
public class InputModuleTests
{
    [Test]
    public async Task TestExecutePerformActions()
    {
        TestConnection connection = new();
        connection.DataSendComplete += async (sender, e) =>
        {
            string responseJson = $$"""
                                  {
                                    "type": "success",
                                    "id": {{e.SentCommandId}},
                                    "result": {}
                                  }
                                  """;
            await connection.RaiseDataReceivedEventAsync(responseJson);
        };

        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), new(connection));
        await driver.StartAsync(new Uri("ws://localhost/"));
        InputModule module = new(driver);

        var task = module.PerformActionsAsync(new PerformActionsCommandParameters("myContextId"));
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        EmptyResult result = task.Result;
        
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task TestExecuteReleaseActions()
    {
        TestConnection connection = new();
        connection.DataSendComplete += async (sender, e) =>
        {
            string responseJson = $$"""
                                  {
                                    "type": "success",
                                    "id": {{e.SentCommandId}},
                                    "result": {}
                                  }
                                  """;
            await connection.RaiseDataReceivedEventAsync(responseJson);
        };

        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), new(connection));
        await driver.StartAsync(new Uri("ws://localhost/"));
        InputModule module = new(driver);

        var task = module.ReleaseActionsAsync(new ReleaseActionsCommandParameters("myContextId"));
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        EmptyResult result = task.Result;
        
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task TestExecuteSetFiles()
    {
        TestConnection connection = new();
        connection.DataSendComplete += async (sender, e) =>
        {
            string responseJson = $$"""
                                  {
                                    "type": "success",
                                    "id": {{e.SentCommandId}},
                                    "result": {}
                                  }
                                  """;
            await connection.RaiseDataReceivedEventAsync(responseJson);
        };

        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), new(connection));
        await driver.StartAsync(new Uri("ws://localhost/"));
        InputModule module = new(driver);

        SharedReference element = new("mySharedId");
        var task = module.SetFilesAsync(new SetFilesCommandParameters("myContextId", element));
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        EmptyResult result = task.Result;
        
        Assert.That(result, Is.Not.Null);
    }
}
