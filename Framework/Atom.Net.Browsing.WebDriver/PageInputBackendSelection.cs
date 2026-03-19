namespace Atom.Net.Browsing.WebDriver;

internal static class PageInputBackendFactory
{
    public static PageInputBackendSelector Create(
        Func<string, CancellationToken, ValueTask<System.Text.Json.JsonElement?>> executeAsync)
        => new(new SyntheticPageLocalInputBackend(executeAsync));
}

internal sealed class PageInputBackendSelector(IPageLocalInputBackend backend)
{
    private readonly IPageLocalInputBackend pageLocalInputBackend = backend;

    public PageInputCapabilities Capabilities => new(
        SupportsParallelPointClick: pageLocalInputBackend.Capabilities.SupportsParallelPointClick,
        SupportsParallelKeyPress: pageLocalInputBackend.Capabilities.SupportsParallelKeyPress,
        SupportsTrustedPointClick: false,
        SupportsTrustedKeyPress: false);

    public ValueTask ClickPointAsync(double viewportX, double viewportY, PagePointClickOptions _, CancellationToken cancellationToken)
        => pageLocalInputBackend.ClickPointAsync(viewportX, viewportY, cancellationToken);

    public ValueTask ClickElementAsync(
        ElementSelector selector,
        bool scrollIntoView,
        CancellationToken cancellationToken)
        => pageLocalInputBackend.ClickElementAsync(selector, scrollIntoView, cancellationToken);

    public ValueTask FocusElementAsync(
        ElementSelector selector,
        bool scrollIntoView,
        CancellationToken cancellationToken)
        => pageLocalInputBackend.FocusElementAsync(selector, scrollIntoView, cancellationToken);

    public ValueTask HoverElementAsync(
        ElementSelector selector,
        bool scrollIntoView,
        CancellationToken cancellationToken)
        => pageLocalInputBackend.HoverElementAsync(selector, scrollIntoView, cancellationToken);

    public ValueTask TypeElementAsync(
        ElementSelector selector,
        string text,
        bool scrollIntoView,
        CancellationToken cancellationToken)
        => pageLocalInputBackend.TypeElementAsync(selector, text, scrollIntoView, cancellationToken);

    public ValueTask CheckElementAsync(
        ElementSelector selector,
        bool scrollIntoView,
        CancellationToken cancellationToken)
        => pageLocalInputBackend.CheckElementAsync(selector, scrollIntoView, cancellationToken);

    public ValueTask KeyPressAsync(string key, PageKeyPressOptions _, CancellationToken cancellationToken)
        => pageLocalInputBackend.KeyPressAsync(key, cancellationToken);
}