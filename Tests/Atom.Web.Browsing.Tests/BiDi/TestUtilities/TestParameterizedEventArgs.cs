namespace Atom.Web.Browsing.BiDi.TestUtilities;

public class TestParameterizedEventArgs : BiDiEventArgs
{
    private readonly string eventName;

    public TestParameterizedEventArgs(TestValidEventData data)
    {
        this.eventName = data.Name;
    }

    public string EventName => this.eventName;
}
