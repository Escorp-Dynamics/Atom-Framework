namespace Atom.Web.Browsing.BiDi.Browser;

using TestUtilities;

[TestFixture]
public class BrowserModuleTests
{
    [Test]
    public async Task TestExecuteCloseCommand()
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
        BrowserModule module = new(driver);

        var task = module.CloseAsync(new CloseCommandParameters());
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        EmptyResult result = task.Result;

        Assert.That(result, Is.Not.Null);
    }
    
    [Test]
    public async Task TestExecuteCloseCommandWithNoArgument()
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
        BrowserModule module = new(driver);

        var task = module.CloseAsync();
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        EmptyResult result = task.Result;

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task TestExecuteCreateUserContextCommand()
    {
        TestConnection connection = new();
        connection.DataSendComplete += async (sender, e) =>
        {
            string responseJson = $$"""
                                  {
                                    "type": "success",
                                    "id": {{e.SentCommandId}},
                                    "result": {
                                      "userContext": "myUserContextId"
                                    }
                                  }
                                  """;
            await connection.RaiseDataReceivedEventAsync(responseJson);
        };

        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), new(connection));
        await driver.StartAsync(new Uri("ws://localhost/"));
        BrowserModule module = new(driver);

        var task = module.CreateUserContextAsync(new CreateUserContextCommandParameters());
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        CreateUserContextCommandResult result = task.Result;

        Assert.That(result, Is.Not.Null);
        Assert.That(result.UserContextId, Is.EqualTo("myUserContextId"));
    }

    [Test]
    public async Task TestExecuteCreateUserContextCommandWithNoArgument()
    {
        TestConnection connection = new();
        connection.DataSendComplete += async (sender, e) =>
        {
            string responseJson = $$"""
                                  {
                                    "type": "success",
                                    "id": {{e.SentCommandId}},
                                    "result": {
                                      "userContext": "myUserContextId" 
                                    }
                                  }
                                  """;
            await connection.RaiseDataReceivedEventAsync(responseJson);
        };

        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), new(connection));
        await driver.StartAsync(new Uri("ws://localhost/"));
        BrowserModule module = new(driver);

        var task = module.CreateUserContextAsync();
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        CreateUserContextCommandResult result = task.Result;

        Assert.That(result, Is.Not.Null);
        Assert.That(result.UserContextId, Is.EqualTo("myUserContextId"));
    }

    [Test]
    public async Task TestExecuteGetClientWindowsCommand()
    {
        TestConnection connection = new();
        connection.DataSendComplete += async (sender, e) =>
        {
            string responseJson = $$"""
                                  {
                                    "type": "success",
                                    "id": {{e.SentCommandId}},
                                    "result": {
                                      "clientWindows": [
                                        {
                                          "clientWindow": "myClientWindow",
                                          "active": true,
                                          "state": "normal",
                                          "x": 100,
                                          "y": 200,
                                          "width": 640,
                                          "height": 480
                                        },
                                        {
                                          "clientWindow": "yourClientWindow",
                                          "active": false,
                                          "state": "normal",
                                          "x": 50,
                                          "y": 75,
                                          "width": 960,
                                          "height": 720
                                        }
                                      ]
                                    }
                                  }
                                  """;
            await connection.RaiseDataReceivedEventAsync(responseJson);
        };

        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), new(connection));
        await driver.StartAsync(new Uri("ws://localhost/"));
        BrowserModule module = new(driver);

        var task = module.GetClientWindowsAsync(new GetClientWindowsCommandParameters());
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        GetClientWindowsCommandResult result = task.Result;

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.ClientWindows.ToArray(), Has.Length.EqualTo(2));
            Assert.That(result.ClientWindows.First().ClientWindowId, Is.EqualTo("myClientWindow"));
            Assert.That(result.ClientWindows.First().IsActive, Is.True);
            Assert.That(result.ClientWindows.First().State, Is.EqualTo(ClientWindowState.Normal));
            Assert.That(result.ClientWindows.First().X, Is.EqualTo(100));
            Assert.That(result.ClientWindows.First().Y, Is.EqualTo(200));
            Assert.That(result.ClientWindows.First().Width, Is.EqualTo(640));
            Assert.That(result.ClientWindows.First().Height, Is.EqualTo(480));
            Assert.That(result.ClientWindows.Last().ClientWindowId, Is.EqualTo("yourClientWindow"));
            Assert.That(result.ClientWindows.Last().IsActive, Is.False);
            Assert.That(result.ClientWindows.Last().State, Is.EqualTo(ClientWindowState.Normal));
            Assert.That(result.ClientWindows.Last().X, Is.EqualTo(50));
            Assert.That(result.ClientWindows.Last().Y, Is.EqualTo(75));
            Assert.That(result.ClientWindows.Last().Width, Is.EqualTo(960));
            Assert.That(result.ClientWindows.Last().Height, Is.EqualTo(720));
        });
    }

    [Test]
    public async Task TestExecuteGetClientWindowsCommandWithNoArgument()
    {
        TestConnection connection = new();
        connection.DataSendComplete += async (sender, e) =>
        {
            string responseJson = $$"""
                                  {
                                    "type": "success",
                                    "id": {{e.SentCommandId}},
                                    "result": {
                                      "clientWindows": [
                                        {
                                          "clientWindow": "myClientWindow",
                                          "active": true,
                                          "state": "normal",
                                          "x": 100,
                                          "y": 200,
                                          "width": 640,
                                          "height": 480
                                        },
                                        {
                                          "clientWindow": "yourClientWindow",
                                          "active": false,
                                          "state": "normal",
                                          "x": 50,
                                          "y": 75,
                                          "width": 960,
                                          "height": 720
                                        }
                                      ]
                                    }
                                  }
                                  """;
            await connection.RaiseDataReceivedEventAsync(responseJson);
        };

        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), new(connection));
        await driver.StartAsync(new Uri("ws://localhost/"));
        BrowserModule module = new(driver);

        var task = module.GetClientWindowsAsync();
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        GetClientWindowsCommandResult result = task.Result;

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.ClientWindows.ToArray(), Has.Length.EqualTo(2));
            Assert.That(result.ClientWindows.First().ClientWindowId, Is.EqualTo("myClientWindow"));
            Assert.That(result.ClientWindows.First().IsActive, Is.True);
            Assert.That(result.ClientWindows.First().State, Is.EqualTo(ClientWindowState.Normal));
            Assert.That(result.ClientWindows.First().X, Is.EqualTo(100));
            Assert.That(result.ClientWindows.First().Y, Is.EqualTo(200));
            Assert.That(result.ClientWindows.First().Width, Is.EqualTo(640));
            Assert.That(result.ClientWindows.First().Height, Is.EqualTo(480));
            Assert.That(result.ClientWindows.Last().ClientWindowId, Is.EqualTo("yourClientWindow"));
            Assert.That(result.ClientWindows.Last().IsActive, Is.False);
            Assert.That(result.ClientWindows.Last().State, Is.EqualTo(ClientWindowState.Normal));
            Assert.That(result.ClientWindows.Last().X, Is.EqualTo(50));
            Assert.That(result.ClientWindows.Last().Y, Is.EqualTo(75));
            Assert.That(result.ClientWindows.Last().Width, Is.EqualTo(960));
            Assert.That(result.ClientWindows.Last().Height, Is.EqualTo(720));
        });
    }

    [Test]
    public async Task TestExecuteGetUserContextsCommand()
    {
        TestConnection connection = new();
        connection.DataSendComplete += async (sender, e) =>
        {
            string responseJson = $$"""
                                  {
                                    "type": "success",
                                    "id": {{e.SentCommandId}},
                                    "result": {
                                      "userContexts": [
                                        {
                                          "userContext": "default"
                                        },
                                        {
                                          "userContext": "myUserContextId"
                                        }
                                      ]
                                    }
                                  }
                                  """;
            await connection.RaiseDataReceivedEventAsync(responseJson);
        };

        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), new(connection));
        await driver.StartAsync(new Uri("ws://localhost/"));
        BrowserModule module = new(driver);

        var task = module.GetUserContextsAsync(new GetUserContextsCommandParameters());
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        GetUserContextsCommandResult result = task.Result;

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.UserContexts.ToArray(), Has.Length.EqualTo(2));
            Assert.That(result.UserContexts.First().UserContextId, Is.EqualTo("default"));
            Assert.That(result.UserContexts.Last().UserContextId, Is.EqualTo("myUserContextId"));
        });

    }

    [Test]
    public async Task TestExecuteGetUserContextsCommandWithNoArgument()
    {
        TestConnection connection = new();
        connection.DataSendComplete += async (sender, e) =>
        {
            string responseJson = $$"""
                                  {
                                    "type": "success",
                                    "id": {{e.SentCommandId}},
                                    "result": {
                                      "userContexts": [
                                        {
                                          "userContext": "default"
                                        },
                                        {
                                          "userContext": "myUserContextId"
                                        }
                                      ] 
                                    }
                                  }
                                  """;
            await connection.RaiseDataReceivedEventAsync(responseJson);
        };

        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), new(connection));
        await driver.StartAsync(new Uri("ws://localhost/"));
        BrowserModule module = new(driver);

        var task = module.GetUserContextsAsync();
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        GetUserContextsCommandResult result = task.Result;

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.UserContexts.ToArray(), Has.Length.EqualTo(2));
            Assert.That(result.UserContexts.First().UserContextId, Is.EqualTo("default"));
            Assert.That(result.UserContexts.Last().UserContextId, Is.EqualTo("myUserContextId"));
        });
    }

    [Test]
    public async Task TestExecuteRemoveUserContextCommand()
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
        BrowserModule module = new(driver);

        var task = module.RemoveUserContextAsync(new RemoveUserContextCommandParameters("myUserContextId"));
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        EmptyResult result = task.Result;

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task TestExecuteSetClientWindowStateCommand()
    {
        TestConnection connection = new();
        connection.DataSendComplete += async (sender, e) =>
        {
            string responseJson = $$"""
                                  {
                                    "type": "success",
                                    "id": {{e.SentCommandId}},
                                    "result": {
                                      "clientWindow": "myClientWindow",
                                      "active": true,
                                      "state": "normal",
                                      "x": 100,
                                      "y": 200,
                                      "width": 640,
                                      "height": 480
                                    }
                                  }
                                  """;
            await connection.RaiseDataReceivedEventAsync(responseJson);
        };

        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), new(connection));
        await driver.StartAsync(new Uri("ws://localhost/"));
        BrowserModule module = new(driver);

        var task = module.SetClientWindowStateAsync(new SetClientWindowStateCommandParameters("myClientWindow")
        {
            State = ClientWindowState.Normal,
            X = 100,
            Y = 200,
            Width = 640,
            Height = 480
        });
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        SetClientWindowStateCommandResult result = task.Result;

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.ClientWindowId, Is.EqualTo("myClientWindow"));
            Assert.That(result.IsActive, Is.True);
            Assert.That(result.State, Is.EqualTo(ClientWindowState.Normal));
            Assert.That(result.X, Is.EqualTo(100));
            Assert.That(result.Y, Is.EqualTo(200));
            Assert.That(result.Width, Is.EqualTo(640));
            Assert.That(result.Height, Is.EqualTo(480));
        });
    }
}
