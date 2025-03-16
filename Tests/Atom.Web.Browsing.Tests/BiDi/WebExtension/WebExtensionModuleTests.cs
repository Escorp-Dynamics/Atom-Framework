namespace Atom.Web.Browsing.BiDi.WebExtension;

using TestUtilities;

[TestFixture]
public class WebExtensionModuleTests
{
    [Test]
    public async Task TestInstallActivateCommand()
    {
        TestConnection connection = new();
        connection.DataSendComplete += async (sender, e) =>
        {
            string responseJson = $$"""
                                  {
                                    "type": "success",
                                    "id": {{e.SentCommandId}},
                                    "result": {
                                      "extension": "myExtensionId"
                                    }
                                  }
                                  """;
            await connection.RaiseDataReceivedEventAsync(responseJson);
        };

        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), new(connection));
        await driver.StartAsync(new Uri("ws://localhost/"));
        WebExtensionModule module = new(driver);

        var task = module.InstallAsync(new InstallCommandParameters(new ExtensionPath("myExtensionPath")));
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        InstallCommandResult result = task.Result;
        
        Assert.That(result, Is.Not.Null);
        Assert.That(result.ExtensionId, Is.EqualTo("myExtensionId"));
    }

    [Test]
    public async Task TestUninstallActivateCommand()
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
        WebExtensionModule module = new(driver);
        
        var task = module.UninstallAsync(new UninstallCommandParameters("myExtensionPath"));
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        EmptyResult result = task.Result;
        
        Assert.That(result, Is.Not.Null);
    }
}
