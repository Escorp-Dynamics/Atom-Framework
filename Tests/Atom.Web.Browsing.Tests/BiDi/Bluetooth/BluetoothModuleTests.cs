namespace Atom.Web.Browsing.BiDi.Bluetooth;

using TestUtilities;

[TestFixture]
public class BluetoothModuleTests
{
    [Test]
    public async Task TestHandleRequestDevicePromptCommandAcceptingPrompt()
    {
        var connection = new TestConnection();

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
        BluetoothModule module = new(driver);

        var task = module.HandleRequestDevicePromptAsync(new HandleRequestDevicePromptAcceptCommandParameters("myContextId", "myPromptId", "myDeviceId"));
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        EmptyResult result = task.Result;
        
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task TestHandleRequestDevicePromptCommandCancelingPrompt()
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
        BluetoothModule module = new(driver);

        ValueTask<EmptyResult> task = module.HandleRequestDevicePromptAsync(new HandleRequestDevicePromptCancelCommandParameters("myContextId", "myPromptId"));
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        EmptyResult result = task.Result;
        
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task TestSimulateAdapterCommand()
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
        BluetoothModule module = new(driver);

        var task = module.SimulateAdapterAsync(new SimulateAdapterCommandParameters("myContextId", AdapterState.PoweredOn));
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        EmptyResult result = task.Result;
        
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task TestSimulatePreconnectedPeripheralCommand()
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
        await driver.StartAsync(new("ws://localhost/"));
        BluetoothModule module = new(driver);

        var task = module.SimulatePreConnectedPeripheralAsync(new SimulatePreConnectedPeripheralCommandParameters("myContextId", "08:08:08:08:08", "myDeviceName"));
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        EmptyResult result = task.Result;
        
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task TestSimulateAdvertisementCommand()
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
        BluetoothModule module = new(driver);

        var task = module.SimulateAdvertisementAsync(new SimulateAdvertisementCommandParameters("myContextId", new SimulateAdvertisementScanEntry("08:08:08:08:08", -10, new ScanRecord())));
        task.AsTask().Wait(TimeSpan.FromSeconds(1));
        EmptyResult result = task.Result;
        
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task TestCanReceiveContextCreatedEvent()
    {
        TestConnection connection = new();
        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), new(connection));
        await driver.StartAsync(new Uri("ws://localhost/"));
        BluetoothModule module = new(driver);

        ManualResetEvent syncEvent = new(false);
        module.OnRequestDevicePromptUpdated.AddObserver((RequestDevicePromptUpdatedEventArgs e) => {
            Assert.Multiple(() =>
            {
                Assert.That(e.BrowsingContextId, Is.EqualTo("myContext"));
                Assert.That(e.Prompt, Is.EqualTo("myPrompt"));
                Assert.That(e.Devices.ToArray(), Has.Length.EqualTo(1));
                Assert.That(e.Devices.First().DeviceId, Is.EqualTo("myDeviceId"));
                Assert.That(e.Devices.First().DeviceName, Is.EqualTo("myDeviceName"));
            });
            syncEvent.Set();
        });

        string eventJson = """
                           {
                             "type": "event",
                             "method": "bluetooth.requestDevicePromptUpdated",
                             "params": {
                               "context": "myContext",
                               "prompt": "myPrompt",
                               "devices": [
                                 {
                                   "id": "myDeviceId",
                                   "name": "myDeviceName"
                                 }
                               ]
                             }
                           }
                           """;
        await connection.RaiseDataReceivedEventAsync(eventJson);
        bool eventRaised = syncEvent.WaitOne(TimeSpan.FromMilliseconds(250));
        Assert.That(eventRaised, Is.True);
    }
}
