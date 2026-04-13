namespace Atom.Net.Browsing.WebDriver.Protocol;

internal enum BridgeEvent
{
    TabConnected,
    TabDisconnected,
    FrameDetached,
    NavigationCompleted,
    DomContentLoaded,
    PageLoaded,
    ConsoleMessage,
    Callback,
    CallbackFinalized,
    RequestIntercepted,
    RequestHeadersObserved,
    ResponseReceived,
    DialogOpened,
    ScriptError,
}