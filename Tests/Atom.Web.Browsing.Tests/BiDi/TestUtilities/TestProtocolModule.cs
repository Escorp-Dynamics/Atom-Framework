using Atom.Web.Browsing.BiDi.JsonConverters.Tests;

namespace Atom.Web.Browsing.BiDi.TestUtilities;

public sealed class TestProtocolModule : Module
{
    private ObservableEvent<TestEventArgs> onEventInvokedEvent;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestProtocolModule"/> class.
    /// </summary>
    /// <param name="driver">The <see cref="BiDiDriver"/> used in the module commands and events.</param>
    public TestProtocolModule(BiDiDriver driver, int maxObserverCount = 0)
        : base(driver)
    {
        onEventInvokedEvent = new ObservableEvent<TestEventArgs>(maxObserverCount);
        RegisterAsyncEventInvoker("protocol.event", JsonTestContext.Default.EventMessageTestEventArgs, OnEventInvokedAsync);
    }

    public ObservableEvent<TestEventArgs> OnEventInvoked => onEventInvokedEvent;

    public override string ModuleName => "protocol";

    private async ValueTask OnEventInvokedAsync(EventInfo<TestEventArgs> eventData)
    {
        TestEventArgs eventArgs = eventData.EventData;
        eventArgs.AdditionalData = eventData.AdditionalData;
        await onEventInvokedEvent.NotifyObserversAsync(eventArgs);
    }
}
