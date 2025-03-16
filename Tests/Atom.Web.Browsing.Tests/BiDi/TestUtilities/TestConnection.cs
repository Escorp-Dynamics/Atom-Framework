namespace Atom.Web.Browsing.BiDi.TestUtilities;

using System.Text;
using Atom.Web.Browsing.BiDi.Protocol;

public class TestConnection : Connection
{
    public bool BypassStart { get; set; } = true;

    public bool BypassStop { get; set; } = true;

    public bool BypassDataSend { get; set; } = true;

    public string? DataSent { get; set; }

    public TimeSpan? DataSendDelay { get; set; }

    public event EventHandler? DataSendStarting;

    public event EventHandler<TestConnectionDataSentEventArgs>? DataSendComplete;

    public ValueTask RaiseDataReceivedEventAsync(string data) => OnDataReceived.NotifyObserversAsync(new ConnectionDataReceivedEventArgs(Encoding.UTF8.GetBytes(data)));

    public ValueTask RaiseLogMessageEventAsync(string message, BiDiLogLevel level) => OnLogMessage.NotifyObserversAsync(new LogMessageEventArgs(message, level, "TestConnection"));

    public override ValueTask StartAsync(Uri url)
    {
        ConnectedUrl = url;
        return BypassStart ? ValueTask.CompletedTask : base.StartAsync(url);
    }

    public override ValueTask StopAsync() => BypassStop ? ValueTask.CompletedTask : base.StopAsync();

    public override ValueTask SendDataAsync(ReadOnlyMemory<byte> data) => BypassStart ? SendWebSocketDataAsync(data) : base.SendDataAsync(data);

    protected override ValueTask SendWebSocketDataAsync(ReadOnlyMemory<byte> data)
    {
        OnDataSendStarting();

        DataSent = Encoding.UTF8.GetString(data.Span);
        var result = ValueTask.CompletedTask;

        if (!BypassDataSend) result = base.SendWebSocketDataAsync(data);
        if (DataSendDelay.HasValue) Task.Delay(DataSendDelay.Value).Wait();

        OnDataSendComplete();
        return result;
    }

    protected override ValueTask CloseClientWebSocketAsync() => ValueTask.CompletedTask;

    protected virtual void OnDataSendStarting()
    {
        if (DataSendStarting is not null) DataSendStarting(this, new EventArgs());
    }

    protected virtual void OnDataSendComplete()
    {
        if (DataSendComplete is not null) DataSendComplete(this, new TestConnectionDataSentEventArgs(DataSent));
    }
}
