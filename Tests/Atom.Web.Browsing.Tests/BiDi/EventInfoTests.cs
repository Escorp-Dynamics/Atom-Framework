namespace Atom.Web.Browsing.BiDi;

using Atom.Web.Browsing.BiDi.TestUtilities;

[TestFixture]
public class EventInfoTests
{
    [Test]
    public void TestCanCreateEventArgsFromEventInfo()
    {
        EventInfo<TestEventArgs> eventInfo = new(new TestEventArgs(), ReceivedDataDictionary.Empty);
        TestEventArgs eventArgs = eventInfo.ToEventArgs<TestEventArgs>();
        Assert.Multiple(() =>
        {
            Assert.That(eventArgs.ParamName, Is.EqualTo("paramValue"));
            Assert.That(eventArgs.AdditionalData, Has.Count.EqualTo(0));
        });
    }

    [Test]
    public void TestCanCreateEventArgsUsingParameterizedConstructor()
    {
        EventInfo<TestValidEventData> eventInfo = new(new TestValidEventData("eventName"), ReceivedDataDictionary.Empty);
        TestParameterizedEventArgs eventArgs = eventInfo.ToEventArgs<TestParameterizedEventArgs>();
        Assert.That(eventArgs.EventName, Is.EqualTo("eventName"));
    }

    [Test]
    public void TestConvertingToInvalidTypeThrows()
    {
        EventInfo<TestInvalidEventData> eventInfo = new(new TestInvalidEventData(), ReceivedDataDictionary.Empty);
        Assert.That(() => eventInfo.ToEventArgs<TestParameterizedEventArgs>(), Throws.InstanceOf<BiDiException>().With.Message.Contains("Не удалось создать EventArgs"));
    }
}
