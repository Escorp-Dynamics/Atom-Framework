namespace Atom.Web.Browsing.BiDi;

using TestUtilities;
using PinchHitter;
using Atom.Web.Browsing.BiDi.Browser;
using Atom.Web.Browsing.BiDi.BrowsingContext;
using Atom.Web.Browsing.BiDi.Input;
using Atom.Web.Browsing.BiDi.Log;
using Atom.Web.Browsing.BiDi.Network;
using Atom.Web.Browsing.BiDi.Protocol;
using Atom.Web.Browsing.BiDi.Script;
using Atom.Web.Browsing.BiDi.Session;
using Atom.Web.Browsing.BiDi.Storage;
using Atom.Web.Browsing.BiDi.Permissions;
using System.Globalization;
using Atom.Web.Browsing.BiDi.WebExtension;
using Atom.Web.Browsing.BiDi.Bluetooth;
using Atom.Web.Browsing.BiDi.JsonConverters.Tests;

[TestFixture]
public class BiDiDriverTests
{
    private const int Timeout = 1500;

    [Test]
    public async Task CanExecuteCommand()
    {
        TestConnection connection = new();
        connection.DataSendComplete += async (object? sender, TestConnectionDataSentEventArgs e) =>
        {
            string eventJson = """
                               {
                                 "type": "success",
                                 "id": 1,
                                 "result": {
                                   "value": "command result value"
                                 }
                               }
                               """;
            await connection.RaiseDataReceivedEventAsync(eventJson);
        };

        Transport transport = new(connection);
        var driver = new BiDiDriver(TimeSpan.FromMilliseconds(Timeout), transport);
        await driver.StartAsync(new Uri("ws://localhost:5555"));

        string commandName = "module.command";
        TestCommandParameters command = new(commandName);
        TestCommandResult result = await driver.ExecuteCommandAsync(command, JsonTestContext.Default.TestCommandParameters, JsonTestContext.Default.CommandResponseMessageTestCommandResult);
        Assert.That(result.Value, Is.EqualTo("command result value"));
    }

    [Test]
    public async Task CanExecuteCommandWithError()
    {
        TestConnection connection = new();
        connection.DataSendComplete += async (object? sender, TestConnectionDataSentEventArgs e) =>
        {
            string errorJson = """
                               {
                                 "type": "error",
                                 "id": 1,
                                 "error": "unknown command", 
                                 "message": "This is a test error message"
                               }
                               """;
            await connection.RaiseDataReceivedEventAsync(errorJson);
        };

        Transport transport = new(connection);
        BiDiDriver driver = new(TimeSpan.FromMilliseconds(Timeout), transport);
        await driver.StartAsync(new Uri("ws://localhost:5555"));

        string commandName = "module.command";
        TestCommandParameters command = new(commandName);
        Assert.That(async () => await driver.ExecuteCommandAsync(command, JsonTestContext.Default.TestCommandParameters, JsonTestContext.Default.CommandResponseMessageTestCommandResult), Throws.InstanceOf<BiDiException>().With.Message.Contains("Получена ошибка 'unknown command' при выполнении команды module.command: This is a test error message"));
    }

    [Test]
    public async Task CanExecuteCommandThatReturnsThrownExceptionThrows()
    {
        TestConnection connection = new();
        connection.DataSendComplete += async (object? sender, TestConnectionDataSentEventArgs e) =>
        {
            string exceptionJson = """
                                   {
                                     "type": "success",
                                     "id": 1, 
                                     "noResult": {
                                       "invalid": "unknown command",
                                       "message": "This is a test error message"
                                     }
                                   }
                                   """;
            await connection.RaiseDataReceivedEventAsync(exceptionJson);
        };

        Transport transport = new(connection);
        BiDiDriver driver = new(TimeSpan.FromMilliseconds(Timeout), transport);
        await driver.StartAsync(new Uri("ws://localhost:5555"));

        string commandName = "module.command";
        TestCommandParameters command = new(commandName);
        Assert.That(async () => await driver.ExecuteCommandAsync(command, JsonTestContext.Default.TestCommandParameters, JsonTestContext.Default.CommandResponseMessageTestCommandResult), Throws.InstanceOf<BiDiException>().With.Message.Contains("Response did not contain properly formed JSON for response type"));
    }

    [Test]
    public async Task CanExecuteReceiveErrorWithoutCommand()
    {
        ErrorResult? response = null;
        ManualResetEvent syncEvent = new(false);
        TestConnection connection = new();
        Transport transport = new(connection);
        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), transport);
        driver.OnUnexpectedErrorReceived.AddObserver((ErrorReceivedEventArgs e) =>
        {
            response = e.ErrorData;
            syncEvent.Set();
        });
        await driver.StartAsync(new Uri("ws://localhost:5555"));

        string errorJson = """
                           {
                             "type": "error",
                             "id": null,
                             "error": "unknown command",
                             "message": "This is a test error message"
                           }
                           """;
        await connection.RaiseDataReceivedEventAsync(errorJson);
        syncEvent.WaitOne(TimeSpan.FromMilliseconds(100));

        Assert.That(response, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(response!.ErrorType, Is.EqualTo("unknown command"));
            Assert.That(response.ErrorMessage, Is.EqualTo("This is a test error message"));
        });
    }

    [Test]
    public async Task CanReceiveKnownEvent()
    {
        string receivedEvent = string.Empty;
        object? receivedData = null;
        ManualResetEvent syncEvent = new(false);

        string eventName = "module.event";
        TestConnection connection = new();
        Transport transport = new(connection);
        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), transport);
        driver.RegisterEvent(eventName, JsonTestContext.Default.EventMessageTestEventArgs);
        driver.OnEventReceived.AddObserver((e) =>
        {
            receivedEvent = e.EventName;
            receivedData = e.EventData;
            syncEvent.Set();
        });
        await driver.StartAsync(new Uri("ws://localhost:5555"));

        string eventJson = """
                           {
                             "type": "event",
                             "method": "module.event",
                             "params": {
                               "paramName": "paramValue" 
                             } 
                           }
                           """;
        await connection.RaiseDataReceivedEventAsync(eventJson);
        syncEvent.WaitOne(TimeSpan.FromMilliseconds(100));
        Assert.Multiple(() =>
        {
            Assert.That(receivedEvent, Is.EqualTo(eventName));
            Assert.That(receivedData, Is.Not.Null);
            Assert.That(receivedData, Is.TypeOf<TestEventArgs>());
        });
        TestEventArgs? convertedData = receivedData as TestEventArgs;
        Assert.That(convertedData!.ParamName, Is.EqualTo("paramValue"));
    }

    [Test]
    public async Task TestDriverWillProcessPendingMessagesOnStop()
    {
        string receivedEvent = string.Empty;
        object? receivedData = null;
        ManualResetEvent syncEvent = new(false);

        string eventName = "module.event";
        TestConnection connection = new();
        TestTransport transport = new(connection)
        {
            MessageProcessingDelay = TimeSpan.FromMilliseconds(100)
        };
        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), transport);
        driver.RegisterEvent(eventName, JsonTestContext.Default.EventMessageTestEventArgs);
        driver.OnEventReceived.AddObserver((e) =>
        {
            receivedEvent = e.EventName;
            receivedData = e.EventData;
            syncEvent.Set();
        });
        await driver.StartAsync(new Uri("ws://localhost:5555"));

        string eventJson = """
                           {
                             "type": "event",
                             "method": "module.event",
                             "params": {
                               "paramName": "paramValue"
                             }
                           }
                           """;
        await connection.RaiseDataReceivedEventAsync(eventJson);
        await driver.StopAsync();
        syncEvent.WaitOne(TimeSpan.FromMilliseconds(100));
        Assert.Multiple(() =>
        {
            Assert.That(receivedEvent, Is.EqualTo(eventName));
            Assert.That(receivedData, Is.Not.Null);
            Assert.That(receivedData, Is.TypeOf<TestEventArgs>());
        });
        TestEventArgs? convertedData = receivedData as TestEventArgs;
        Assert.That(convertedData!.ParamName, Is.EqualTo("paramValue"));
    }

    [Test]
    public async Task TestUnregisteredEventRaisesUnknownMessageEvent()
    {
        string receivedMessage = string.Empty;
        ManualResetEvent syncEvent = new(false);

        TestConnection connection = new();
        Transport transport = new(connection);
        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), transport);
        driver.OnUnknownMessageReceived.AddObserver((e) =>
        {
            receivedMessage = e.Message;
            syncEvent.Set();
        });
        await driver.StartAsync(new Uri("ws://localhost:5555"));

        string serialized = """
                            {
                              "type": "event",
                              "method": "module.event",
                              "params": {
                                "paramName": "paramValue"
                              }
                            }
                            """;
        await connection.RaiseDataReceivedEventAsync(serialized);
        syncEvent.WaitOne(TimeSpan.FromMilliseconds(100));
        Assert.That(receivedMessage, Is.EqualTo(serialized));
    }

    [Test]
    public async Task TestUnconformingDataRaisesUnknownMessageEvent()
    {
        string receivedMessage = string.Empty;
        ManualResetEvent syncEvent = new(false);

        TestConnection connection = new();
        Transport transport = new(connection);
        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), transport);
        driver.OnUnknownMessageReceived.AddObserver((e) =>
        {
            receivedMessage = e.Message;
            syncEvent.Set();
        });
        await driver.StartAsync(new Uri("ws://localhost:5555"));

        string serialized = """
                            {
                              "someProperty": "someValue",
                              "params": {
                                "thisMessage": "matches no protocol message"
                              }
                            }
                            """;
        await connection.RaiseDataReceivedEventAsync(serialized);
        syncEvent.WaitOne(TimeSpan.FromMilliseconds(100));
        Assert.That(receivedMessage, Is.EqualTo(serialized));
    }

    [Test]
    public async Task TestModuleAvailability()
    {
        TestConnection connection = new();
        Transport transport = new(connection);
        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), transport);
        await driver.StartAsync(new Uri("ws://localhost:5555"));
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(driver.Bluetooth, Is.InstanceOf<BluetoothModule>());
                Assert.That(driver.Browser, Is.InstanceOf<BrowserModule>());
                Assert.That(driver.BrowsingContext, Is.InstanceOf<BrowsingContextModule>());
                Assert.That(driver.Input, Is.InstanceOf<InputModule>());
                Assert.That(driver.Log, Is.InstanceOf<LogModule>());
                Assert.That(driver.Network, Is.InstanceOf<NetworkModule>());
                Assert.That(driver.Permissions, Is.InstanceOf<PermissionsModule>());
                Assert.That(driver.Script, Is.InstanceOf<ScriptModule>());
                Assert.That(driver.Session, Is.InstanceOf<SessionModule>());
                Assert.That(driver.Storage, Is.InstanceOf<StorageModule>());
                Assert.That(driver.WebExtension, Is.InstanceOf<WebExtensionModule>());
            });
        }
        finally
        {
            await driver.StopAsync();
        }
    }

    [Test]
    public async Task TestDriverCanEmitLogMessagesFromProtocol()
    {
        DateTime testStart = DateTime.UtcNow;
        List<LogMessageEventArgs> logs = new();
        TestConnection connection = new();
        Transport transport = new(connection);
        BiDiDriver driver = new(TimeSpan.FromMilliseconds(100), transport);
        driver.OnLogMessage.AddObserver((e) =>
        {
            logs.Add(e);
        });
        await driver.StartAsync(new Uri("ws://localhost/"));
        await connection.RaiseLogMessageEventAsync("test log message", BiDiLogLevel.Warn);
        Assert.That(logs, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(logs[0].Message, Is.EqualTo("test log message"));
            Assert.That(logs[0].Level, Is.EqualTo(BiDiLogLevel.Warn));
            Assert.That(logs[0].Timestamp, Is.GreaterThanOrEqualTo(testStart));
            Assert.That(logs[0].ComponentName, Is.EqualTo("TestConnection"));
        });
    }

    [Test]
    public void TestCanRegisterModule()
    {
        TestConnection connection = new();
        Transport transport = new(connection);
        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), transport);
        driver.RegisterModule(new TestProtocolModule(driver));
        Assert.That(driver.GetModule<TestProtocolModule>("protocol"), Is.InstanceOf<TestProtocolModule>());
    }

    [Test]
    public void TestGettingInvalidModuleNameThrows()
    {
        TestConnection connection = new();
        Transport transport = new(connection);
        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), transport);
        Assert.That(() => driver.GetModule<TestProtocolModule>("protocol"), Throws.InstanceOf<BiDiException>().With.Message.EqualTo("Модуль 'protocol' не зарегистрирован в этом драйвере"));
    }

    [Test]
    public void TestGettingInvalidModuleTypeThrows()
    {
        TestConnection connection = new();
        Transport transport = new(connection);
        BiDiDriver driver = new(TimeSpan.FromMilliseconds(500), transport);
        driver.RegisterModule(new TestProtocolModule(driver));
        Assert.That(() => driver.GetModule<SessionModule>("protocol"), Throws.InstanceOf<BiDiException>().With.Message.EqualTo("Модуль 'protocol' зарегистрирован в этом драйвере, но объект модуля не является типом Atom.Web.Browsing.BiDi.Session.SessionModule"));
    }

    [Test]
    public async Task TestReceivingNullValueFromSendingCommandThrows()
    {
        TestTransport transport = new(new TestConnection())
        {
            ReturnCustomValue = true
        };
        BiDiDriver driver = new(TimeSpan.FromMilliseconds(250), transport);
        await driver.StartAsync(new Uri("ws://localhost/"));
        Assert.That(async () => await driver.ExecuteCommandAsync(new TestCommandParameters("test.command"), JsonTestContext.Default.TestCommandParameters, JsonTestContext.Default.CommandResponseMessageTestCommandResult), Throws.InstanceOf<BiDiException>().With.Message.EqualTo("Результат и исключение для команды test.command с id 0 равны null"));
    }

    [Test]
    public async Task TestExecutingCommandWillThrowWhenTimeout()
    {
        BiDiDriver driver = new(TimeSpan.Zero, new Transport(new TestConnection()));
        await driver.StartAsync(new Uri("ws://localhost:5555"));
        Assert.That(async () => await driver.ExecuteCommandAsync(new TestCommandParameters("test.command"), JsonTestContext.Default.TestCommandParameters, JsonTestContext.Default.CommandResponseMessageTestCommandResult), Throws.InstanceOf<BiDiException>().With.Message.Contains("Превышено время ожидания выполнения команды test.command"));
    }

    [Test]
    public async Task TestReceivingInvalidErrorValueFromSendingCommandThrows()
    {
        TestCommandResult result = new();
        result.SetIsErrorValue(true);
        TestTransport transport = new(new TestConnection())
        {
            ReturnCustomValue = true,
            CustomReturnValue = result
        };
        BiDiDriver driver = new(TimeSpan.FromMilliseconds(250), transport);
        await driver.StartAsync(new Uri("ws://localhost:5555"));
        Assert.That(async () => await driver.ExecuteCommandAsync(new TestCommandParameters("test.command"), JsonTestContext.Default.TestCommandParameters, JsonTestContext.Default.CommandResponseMessageTestCommandResult), Throws.InstanceOf<BiDiException>().With.Message.EqualTo("Не удалось преобразовать ответ об ошибке от транспорта для SendCommandAndWait в ErrorResult"));
    }

    [Test]
    public async Task TestReceivingInvalidResultTypeFromSendingCommandThrows()
    {
        TestCommandResultInvalid result = new();
        TestTransport transport = new(new TestConnection())
        {
            ReturnCustomValue = true,
            CustomReturnValue = result
        };
        BiDiDriver driver = new(TimeSpan.FromMilliseconds(250), transport);
        await driver.StartAsync(new Uri("ws://localhost:5555"));
        Assert.That(async () => await driver.ExecuteCommandAsync(new TestCommandParameters("test.command"), JsonTestContext.Default.TestCommandParameters, JsonTestContext.Default.CommandResponseMessageTestCommandResult), Throws.InstanceOf<BiDiException>().With.Message.EqualTo("Не удалось преобразовать ответ от транспорта для SendCommandAndWait в Atom.Web.Browsing.BiDi.TestUtilities.TestCommandResult"));
    }

    [Test]
    public async Task TestDriverCanUseDefaultTransport()
    {
        ManualResetEvent connectionSyncEvent = new(false);
        void connectionHandler(ClientConnectionEventArgs e) { connectionSyncEvent.Set(); }
        static void handler(ServerDataReceivedEventArgs e) { }
        Server server = new();
        ServerEventObserver<ServerDataReceivedEventArgs> dataReceivedObserver = server.OnDataReceived.AddObserver(handler);
        server.OnClientConnected.AddObserver(connectionHandler);
        server.Start();

        BiDiDriver driver = new();
        await driver.StartAsync(new Uri($"ws://localhost:{server.Port}"));
        bool connectionEventRaised = connectionSyncEvent.WaitOne(TimeSpan.FromSeconds(1));
        await driver.StopAsync();

        server.Stop();
        dataReceivedObserver.Unobserve();
        Assert.That(connectionEventRaised, Is.True);
    }

    [Test]
    public async Task TestMalformedEventResponseLogsError()
    {
        string connectionId = string.Empty;
        ManualResetEvent connectionSyncEvent = new(false);
        void connectionHandler(ClientConnectionEventArgs e)
        {
            connectionId = e.ConnectionId;
            connectionSyncEvent.Set();
        }

        Server server = new();
        server.OnClientConnected.AddObserver(connectionHandler);
        server.Start();
        BiDiDriver driver = new(TimeSpan.FromSeconds(30));

        try
        {
            await driver.StartAsync(new Uri($"ws://localhost:{server.Port}"));
            connectionSyncEvent.WaitOne(TimeSpan.FromSeconds(1));
            ManualResetEvent logSyncEvent = new(false);
            List<string> driverLog = new();
            driver.OnLogMessage.AddObserver((e) =>
            {
                if (e.Level >= BiDiLogLevel.Error)
                {
                    driverLog.Add(e.Message);
                    logSyncEvent.Set();
                }
            });

            ManualResetEvent unknownMessageSyncEvent = new(false);
            string unknownMessage = string.Empty;
            driver.OnUnknownMessageReceived.AddObserver((e) =>
            {
                unknownMessage = e.Message;
                unknownMessageSyncEvent.Set();
            });

            // This payload omits the required "timestamp" field, which should cause an exception
            // in parsing.
            string eventJson = """
                               {
                                 "type": "event",
                                 "method": "browsingContext.load",
                                 "params": {
                                   "context": "myContext",
                                   "url": "https://example.com",
                                   "navigation": "myNavigationId"
                                 }
                               }
                               """;
            await server.SendDataAsync(connectionId, eventJson);
            bool eventsRaised = WaitHandle.WaitAll(new WaitHandle[] { logSyncEvent, unknownMessageSyncEvent }, TimeSpan.FromSeconds(1));
            Assert.Multiple(() =>
            {
                Assert.That(eventsRaised, Is.True);
                Assert.That(driverLog, Has.Count.EqualTo(1));
                Assert.That(driverLog[0], Contains.Substring("Unexpected error parsing event JSON"));
                Assert.That(unknownMessage, Is.Not.Empty);
            });
        }
        finally
        {
            await driver.StopAsync();
            server.Stop();
        }
    }

    [Test]
    public async Task TestMalformedNonCommandErrorResponseLogsError()
    {
        string connectionId = string.Empty;
        ManualResetEvent connectionSyncEvent = new(false);
        void connectionHandler(ClientConnectionEventArgs e)
        {
            connectionId = e.ConnectionId;
            connectionSyncEvent.Set();
        }

        Server server = new();
        server.OnClientConnected.AddObserver(connectionHandler);
        server.Start();
        BiDiDriver driver = new();

        try
        {
            await driver.StartAsync(new Uri($"ws://localhost:{server.Port}"));
            connectionSyncEvent.WaitOne(TimeSpan.FromSeconds(1));

            driver.BrowsingContext.OnLoad.AddObserver((e) =>
            {
            });

            ManualResetEvent logSyncEvent = new(false);
            List<string> driverLog = new();
            driver.OnLogMessage.AddObserver((e) =>
            {
                if (e.Level >= BiDiLogLevel.Error)
                {
                    driverLog.Add(e.Message);
                    logSyncEvent.Set();
                }
            });

            ManualResetEvent unknownMessageSyncEvent = new(false);
            string unknownMessage = string.Empty;
            driver.OnUnknownMessageReceived.AddObserver((e) =>
            {
                unknownMessage = e.Message;
                unknownMessageSyncEvent.Set();
            });

            // This payload uses an object for the error field, which should cause an exception
            // in parsing.
            string json = """
                          {
                            "type": "error",
                            "id": null,
                            "error": {
                              "code": "unknown error"
                            },
                            "message": "This is a test error message"
                          }
                          """;
            await server.SendDataAsync(connectionId, json);
            bool eventsRaised = WaitHandle.WaitAll(new WaitHandle[] { logSyncEvent, unknownMessageSyncEvent }, TimeSpan.FromSeconds(1));
            Assert.That(eventsRaised, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(driverLog, Has.Count.EqualTo(1));
                Assert.That(driverLog[0], Contains.Substring("Unexpected error parsing error JSON"));
                Assert.That(unknownMessage, Is.Not.Empty);
            });
        }
        finally
        {
            await driver.StopAsync();
            server.Stop();
        }
    }

    [Test]
    public async Task TestMalformedIncomingMessageLogsError()
    {
        string connectionId = string.Empty;
        ManualResetEvent connectionSyncEvent = new(false);
        void connectionHandler(ClientConnectionEventArgs e)
        {
            connectionId = e.ConnectionId;
            connectionSyncEvent.Set();
        }

        Server server = new();
        server.OnClientConnected.AddObserver(connectionHandler);
        server.Start();
        BiDiDriver driver = new();

        try
        {
            await driver.StartAsync(new Uri($"ws://localhost:{server.Port}"));
            connectionSyncEvent.WaitOne(TimeSpan.FromSeconds(1));

            ManualResetEvent logSyncEvent = new(false);
            List<string> driverLog = new();
            driver.OnLogMessage.AddObserver((e) =>
            {
                if (e.Level >= BiDiLogLevel.Error)
                {
                    driverLog.Add(e.Message);
                    logSyncEvent.Set();
                }
            });

            ManualResetEvent unknownMessageSyncEvent = new(false);
            string unknownMessage = string.Empty;
            driver.OnUnknownMessageReceived.AddObserver((e) =>
            {
                unknownMessage = e.Message;
                unknownMessageSyncEvent.Set();
            });

            // This payload uses unparsable JSON, which should cause an exception
            // in parsing.
            string unparsableJson = """
                               {
                                 "type": "error",
                                 "id": null,
                                 { "errorMessage" },
                                 "message": "This is a test error message"
                               }
                               """;
            await server.SendDataAsync(connectionId, unparsableJson);
            bool eventsRaised = WaitHandle.WaitAll(new WaitHandle[] { logSyncEvent, unknownMessageSyncEvent }, TimeSpan.FromSeconds(1));
            Assert.That(eventsRaised, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(driverLog, Has.Count.EqualTo(1));
                Assert.That(driverLog[0], Contains.Substring("Unexpected error parsing JSON message"));
                Assert.That(unknownMessage, Is.Not.Empty);
            });
        }
        finally
        {
            await driver.StopAsync();
            server.Stop();
        }
    }

    [Test]
    public async Task CanExecuteParallelCommands()
    {
        TestConnection connection = new();
        connection.DataSendComplete += (object? sender, TestConnectionDataSentEventArgs e) =>
        {
            Task.Run(async () =>
            {
                DateTime start = DateTime.Now;
                if (e.SentCommandName!.Contains("delay"))
                {
                    Task.Delay(250).Wait();
                }

                TimeSpan elapsed = DateTime.Now - start;
                string eventJson = $$"""
                                   {
                                     "type": "success",
                                     "id": {{e.SentCommandId}},
                                     "result": {
                                       "value": "command result value for {{e.SentCommandName}}",
                                       "elapsed": {{elapsed.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)}}
                                     }
                                   }
                                   """;
                await connection.RaiseDataReceivedEventAsync(eventJson);
            });
       };

        Transport transport = new(connection);
        await transport.ConnectAsync(new Uri("ws://localhost:5555"));
        BiDiDriver driver = new(TimeSpan.FromMilliseconds(Timeout), transport);

        string delayCommandName = "module.delayCommand";
        TestCommandParameters delayCommand = new(delayCommandName);

        string commandName = "module.command";
        TestCommandParameters command = new(commandName);

        Task<TestCommandResult>[] parallelTasks = new[]
        {
            driver.ExecuteCommandAsync(delayCommand, JsonTestContext.Default.TestCommandParameters, JsonTestContext.Default.CommandResponseMessageTestCommandResult).AsTask(),
            driver.ExecuteCommandAsync(command, JsonTestContext.Default.TestCommandParameters, JsonTestContext.Default.CommandResponseMessageTestCommandResult).AsTask(),
        };

        int indexOfFirstFinishedTask = Task.WaitAny(parallelTasks);
        bool allTasksCompleted = Task.WaitAll(parallelTasks, TimeSpan.FromSeconds(1));
        Assert.That(allTasksCompleted, Is.True);
        Assert.That(indexOfFirstFinishedTask, Is.EqualTo(1));
        Assert.That(parallelTasks[0].Result.Value, Is.EqualTo($"command result value for {delayCommandName}"));
        Assert.That(parallelTasks[0].Result.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(240));
        Assert.That(parallelTasks[1].Result.Value, Is.EqualTo($"command result value for {commandName}"));
    }
}
