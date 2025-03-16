namespace Atom.Web.Browsing.BiDi.Protocol;

using TestUtilities;

[TestFixture]
public class EventInvokerTests
{
    [Test]
    public async Task TestCanInvokeEvent()
    {
        bool eventInvoked = false;
        ValueTask action(EventInfo<TestEventArgs> info) {
            eventInvoked = true;
            return ValueTask.CompletedTask;
        }
        EventInvoker<TestEventArgs> invoker = new(action);
        await invoker.InvokeEventAsync(new TestEventArgs(), ReceivedDataDictionary.Empty);
        Assert.That(eventInvoked, Is.True);
    }

    [Test]
    public void TestInvokeEventWithInvalidObjectTypeThrows()
    {
        static ValueTask action(EventInfo<TestEventArgs> info)
        {
            return ValueTask.CompletedTask;
        }
        EventInvoker<TestEventArgs> invoker = new(action);
        Assert.That(async () => await invoker.InvokeEventAsync("this is an invalid object", ReceivedDataDictionary.Empty), Throws.InstanceOf<BiDiException>());
    }
}
