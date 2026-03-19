using System.Globalization;
using System.Text.Json;

namespace Atom.Net.Browsing.WebDriver;

internal interface IPageLocalInputBackend
{
    PageInputCapabilities Capabilities { get; }

    ValueTask ClickPointAsync(double viewportX, double viewportY, CancellationToken cancellationToken);

    ValueTask ClickElementAsync(ElementSelector selector, bool scrollIntoView, CancellationToken cancellationToken);

    ValueTask FocusElementAsync(ElementSelector selector, bool scrollIntoView, CancellationToken cancellationToken);

    ValueTask HoverElementAsync(ElementSelector selector, bool scrollIntoView, CancellationToken cancellationToken);

    ValueTask TypeElementAsync(ElementSelector selector, string text, bool scrollIntoView, CancellationToken cancellationToken);

    ValueTask CheckElementAsync(ElementSelector selector, bool scrollIntoView, CancellationToken cancellationToken);

    ValueTask KeyPressAsync(string key, CancellationToken cancellationToken);
}

internal sealed class SyntheticPageLocalInputBackend : IPageLocalInputBackend
{
    private const string SelectorResolutionJavaScript = """
        function findByText(text, root = document) {
            const walkRoot = root === document ? document.body : root;
            if (!walkRoot) {
                return null;
            }

            const walker = document.createTreeWalker(walkRoot, NodeFilter.SHOW_TEXT);
            while (walker.nextNode()) {
                if (walker.currentNode.textContent?.trim() === text) {
                    return walker.currentNode.parentElement;
                }
            }

            return null;
        }

        function findSingle(strategy, value, root = document) {
            switch (strategy) {
                case 'Css':
                    return root.querySelector(value);
                case 'XPath':
                    return document.evaluate(value, root, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
                case 'Id':
                    return root.getElementById ? root.getElementById(value) : root.querySelector(`#${CSS.escape(value)}`);
                case 'Text':
                    return findByText(value, root);
                case 'Name':
                    return root.querySelector(`[name="${CSS.escape(value)}"]`);
                case 'TagName':
                    return root.querySelector(value);
                default:
                    return null;
            }
        }

        const target = findSingle(strategy, value);
        if (!(target instanceof Element)) {
            return false;
        }

        if (scrollIntoView) {
            target.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
        }

        target.focus?.({ preventScroll: true });
    """;

    private const string SyntheticPointTargetResolutionJavaScript = """
        const stack = document.elementsFromPoint(viewportX, viewportY) || [];
        const interactiveSelector = 'button, a, input, textarea, select, option, label, summary, [role], [tabindex]';
        const target = stack.find(element =>
            element !== document.documentElement
            && element !== document.body
            && (element.matches?.(interactiveSelector)
                || typeof element.onclick === 'function'
                || typeof element.onmousedown === 'function'))
            || stack.find(element => element !== document.documentElement && element !== document.body)
            || document.elementFromPoint(viewportX, viewportY);

        if (!target) {
            return false;
        }

        target.focus?.({ preventScroll: true });
    """;

    private const string SyntheticPointEventDispatchJavaScript = """
        const pointerBase = {
            bubbles: true,
            cancelable: true,
            composed: true,
            clientX: viewportX,
            clientY: viewportY,
            button: 0,
            buttons: 0,
            pointerId: 1,
            pointerType: 'mouse',
            isPrimary: true,
            view: window,
        };

        const mouseBase = {
            bubbles: true,
            cancelable: true,
            composed: true,
            clientX: viewportX,
            clientY: viewportY,
            button: 0,
            buttons: 0,
            detail: 1,
            view: window,
        };

        if (typeof PointerEvent === 'function') {
            target.dispatchEvent(new PointerEvent('pointerover', pointerBase));
            target.dispatchEvent(new PointerEvent('pointerenter', { ...pointerBase, bubbles: false }));
            target.dispatchEvent(new PointerEvent('pointermove', pointerBase));
        }

        target.dispatchEvent(new MouseEvent('mouseover', mouseBase));
        target.dispatchEvent(new MouseEvent('mouseenter', { ...mouseBase, bubbles: false }));
        target.dispatchEvent(new MouseEvent('mousemove', mouseBase));

        if (typeof PointerEvent === 'function') {
            target.dispatchEvent(new PointerEvent('pointerdown', { ...pointerBase, buttons: 1 }));
        }

        target.dispatchEvent(new MouseEvent('mousedown', { ...mouseBase, buttons: 1 }));

        if (typeof PointerEvent === 'function') {
            target.dispatchEvent(new PointerEvent('pointerup', { ...pointerBase, buttons: 0 }));
        }

        target.dispatchEvent(new MouseEvent('mouseup', { ...mouseBase, buttons: 0 }));
        target.dispatchEvent(new MouseEvent('click', { ...mouseBase, buttons: 0 }));
        return true;
    """;

    private const string SyntheticElementEventDispatchJavaScript = """
        const rect = target.getBoundingClientRect();
        const viewportX = rect.width > 0 ? rect.left + rect.width / 2 : rect.left;
        const viewportY = rect.height > 0 ? rect.top + rect.height / 2 : rect.top;
    """;

    private const string SyntheticElementHoverJavaScript = """
        const pointerBase = {
            bubbles: true,
            cancelable: true,
            composed: true,
            clientX: viewportX,
            clientY: viewportY,
            button: 0,
            buttons: 0,
            pointerId: 1,
            pointerType: 'mouse',
            isPrimary: true,
            view: window,
        };

        const mouseBase = {
            bubbles: true,
            cancelable: true,
            composed: true,
            clientX: viewportX,
            clientY: viewportY,
            button: 0,
            buttons: 0,
            detail: 1,
            view: window,
        };

        if (typeof PointerEvent === 'function') {
            target.dispatchEvent(new PointerEvent('pointerover', pointerBase));
            target.dispatchEvent(new PointerEvent('pointerenter', { ...pointerBase, bubbles: false }));
            target.dispatchEvent(new PointerEvent('pointermove', pointerBase));
        }

        target.dispatchEvent(new MouseEvent('mouseover', mouseBase));
        target.dispatchEvent(new MouseEvent('mouseenter', { ...mouseBase, bubbles: false }));
        target.dispatchEvent(new MouseEvent('mousemove', mouseBase));
        return true;
    """;

    private const string SyntheticElementCheckJavaScript = """
        const isCheckable = target.matches?.('input[type="checkbox"], input[type="radio"]')
            || target.getAttribute?.('role') === 'checkbox'
            || target.getAttribute?.('role') === 'radio';

        if (!isCheckable) {
            return false;
        }

        if ('checked' in target) {
            if (!target.checked) {
                target.click?.();
            }

            return !!target.checked;
        }

        if (target.getAttribute?.('aria-checked') !== 'true') {
            target.setAttribute?.('aria-checked', 'true');
        }

        target.dispatchEvent(new Event('input', { bubbles: true, cancelable: true, composed: true }));
        target.dispatchEvent(new Event('change', { bubbles: true, cancelable: true, composed: true }));
        target.click?.();
        return true;
    """;

    private readonly Func<string, CancellationToken, ValueTask<JsonElement?>> executeAsync;

    public SyntheticPageLocalInputBackend(Func<string, CancellationToken, ValueTask<JsonElement?>> executeAsync)
    {
        ArgumentNullException.ThrowIfNull(executeAsync);
        this.executeAsync = executeAsync;
    }

    public PageInputCapabilities Capabilities => new(
        SupportsParallelPointClick: true,
        SupportsParallelKeyPress: true);

    public async ValueTask ClickPointAsync(double viewportX, double viewportY, CancellationToken cancellationToken)
    {
        var script = CreateSyntheticPointClickScript(viewportX, viewportY);
        var result = (await executeAsync(script, cancellationToken).ConfigureAwait(false))?.ToString();
        if (!bool.TryParse(result, out var clicked) || !clicked)
            throw new InvalidOperationException($"Не удалось выполнить synthetic click по точке ({viewportX.ToString(CultureInfo.InvariantCulture)}, {viewportY.ToString(CultureInfo.InvariantCulture)}).");
    }

    public async ValueTask ClickElementAsync(ElementSelector selector, bool scrollIntoView, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var script = CreateSyntheticElementClickScript(selector, scrollIntoView);
        var result = (await executeAsync(script, cancellationToken).ConfigureAwait(false))?.ToString();
        if (!bool.TryParse(result, out var clicked) || !clicked)
            throw new InvalidOperationException($"Не удалось выполнить synthetic click по элементу '{selector.Strategy}:{selector.Value}'.");
    }

    public async ValueTask FocusElementAsync(ElementSelector selector, bool scrollIntoView, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var script = CreateSyntheticElementFocusScript(selector, scrollIntoView);
        var result = (await executeAsync(script, cancellationToken).ConfigureAwait(false))?.ToString();
        if (!bool.TryParse(result, out var focused) || !focused)
            throw new InvalidOperationException($"Не удалось установить фокус на элемент '{selector.Strategy}:{selector.Value}'.");
    }

    public async ValueTask HoverElementAsync(ElementSelector selector, bool scrollIntoView, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var script = CreateSyntheticElementHoverScript(selector, scrollIntoView);
        var result = (await executeAsync(script, cancellationToken).ConfigureAwait(false))?.ToString();
        if (!bool.TryParse(result, out var hovered) || !hovered)
            throw new InvalidOperationException($"Не удалось навести pointer на элемент '{selector.Strategy}:{selector.Value}'.");
    }

    public async ValueTask TypeElementAsync(ElementSelector selector, string text, bool scrollIntoView, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentException.ThrowIfNullOrEmpty(text);

        var script = CreateSyntheticElementTypeScript(selector, text, scrollIntoView);
        var result = (await executeAsync(script, cancellationToken).ConfigureAwait(false))?.ToString();
        if (!bool.TryParse(result, out var typed) || !typed)
            throw new InvalidOperationException($"Не удалось ввести текст в элемент '{selector.Strategy}:{selector.Value}'.");
    }

    public async ValueTask CheckElementAsync(ElementSelector selector, bool scrollIntoView, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var script = CreateSyntheticElementCheckScript(selector, scrollIntoView);
        var result = (await executeAsync(script, cancellationToken).ConfigureAwait(false))?.ToString();
        if (!bool.TryParse(result, out var checkedState) || !checkedState)
            throw new InvalidOperationException($"Не удалось отметить элемент '{selector.Strategy}:{selector.Value}'.");
    }

    public async ValueTask KeyPressAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var script = CreateSyntheticKeyPressScript(key);
        var result = (await executeAsync(script, cancellationToken).ConfigureAwait(false))?.ToString();
        if (!bool.TryParse(result, out var pressed) || !pressed)
            throw new InvalidOperationException($"Не удалось выполнить synthetic key press для клавиши '{key}'.");
    }

    private static string CreateSyntheticPointClickScript(double viewportX, double viewportY)
    {
        var xLiteral = viewportX.ToString("G17", CultureInfo.InvariantCulture);
        var yLiteral = viewportY.ToString("G17", CultureInfo.InvariantCulture);

        return $$"""
        (() => {
            const viewportX = {{xLiteral}};
            const viewportY = {{yLiteral}};
            {{SyntheticPointTargetResolutionJavaScript}}
            {{SyntheticPointEventDispatchJavaScript}}
        })()
        """;
    }

    private static string CreateSyntheticElementClickScript(ElementSelector selector, bool scrollIntoView)
    {
        var strategyLiteral = ToJavaScriptStringLiteral(selector.Strategy.ToString());
        var valueLiteral = ToJavaScriptStringLiteral(selector.Value);
        var scrollLiteral = scrollIntoView ? "true" : "false";

        return $$"""
        (() => {
            const strategy = {{strategyLiteral}};
            const value = {{valueLiteral}};
            const scrollIntoView = {{scrollLiteral}};
            {{SelectorResolutionJavaScript}}
            {{SyntheticElementEventDispatchJavaScript}}
            {{SyntheticPointEventDispatchJavaScript}}
        })()
        """;
    }

    private static string CreateSyntheticElementFocusScript(ElementSelector selector, bool scrollIntoView)
    {
        var strategyLiteral = ToJavaScriptStringLiteral(selector.Strategy.ToString());
        var valueLiteral = ToJavaScriptStringLiteral(selector.Value);
        var scrollLiteral = scrollIntoView ? "true" : "false";

        return $$"""
        (() => {
            const strategy = {{strategyLiteral}};
            const value = {{valueLiteral}};
            const scrollIntoView = {{scrollLiteral}};
            {{SelectorResolutionJavaScript}}
            return true;
        })()
        """;
    }

    private static string CreateSyntheticElementHoverScript(ElementSelector selector, bool scrollIntoView)
    {
        var strategyLiteral = ToJavaScriptStringLiteral(selector.Strategy.ToString());
        var valueLiteral = ToJavaScriptStringLiteral(selector.Value);
        var scrollLiteral = scrollIntoView ? "true" : "false";

        return $$"""
        (() => {
            const strategy = {{strategyLiteral}};
            const value = {{valueLiteral}};
            const scrollIntoView = {{scrollLiteral}};
            {{SelectorResolutionJavaScript}}
            {{SyntheticElementEventDispatchJavaScript}}
            {{SyntheticElementHoverJavaScript}}
        })()
        """;
    }

    private static string CreateSyntheticElementTypeScript(ElementSelector selector, string text, bool scrollIntoView)
    {
        var strategyLiteral = ToJavaScriptStringLiteral(selector.Strategy.ToString());
        var valueLiteral = ToJavaScriptStringLiteral(selector.Value);
        var textLiteral = ToJavaScriptStringLiteral(text);
        var scrollLiteral = scrollIntoView ? "true" : "false";

        return $$"""
        (() => {
            const strategy = {{strategyLiteral}};
            const value = {{valueLiteral}};
            const text = {{textLiteral}};
            const scrollIntoView = {{scrollLiteral}};
            {{SelectorResolutionJavaScript}}

            if ('value' in target) {
                target.value = text;
            } else if (target.isContentEditable) {
                target.textContent = text;
            } else {
                return false;
            }

            target.dispatchEvent(new Event('input', { bubbles: true, cancelable: true, composed: true }));
            target.dispatchEvent(new Event('change', { bubbles: true, cancelable: true, composed: true }));
            return true;
        })()
        """;
    }

    private static string CreateSyntheticElementCheckScript(ElementSelector selector, bool scrollIntoView)
    {
        var strategyLiteral = ToJavaScriptStringLiteral(selector.Strategy.ToString());
        var valueLiteral = ToJavaScriptStringLiteral(selector.Value);
        var scrollLiteral = scrollIntoView ? "true" : "false";

        return $$"""
        (() => {
            const strategy = {{strategyLiteral}};
            const value = {{valueLiteral}};
            const scrollIntoView = {{scrollLiteral}};
            {{SelectorResolutionJavaScript}}
            {{SyntheticElementCheckJavaScript}}
        })()
        """;
    }

    private static string CreateSyntheticKeyPressScript(string key)
    {
        var keyLiteral = ToJavaScriptStringLiteral(key);

        return $$"""
        (() => {
            const key = {{keyLiteral}};
            const target = document.activeElement || document.body || document.documentElement;
            if (!target) {
                return false;
            }

            target.focus?.({ preventScroll: true });

            const eventInit = {
                key,
                code: key,
                bubbles: true,
                cancelable: true,
                composed: true,
            };

            target.dispatchEvent(new KeyboardEvent('keydown', eventInit));
            target.dispatchEvent(new KeyboardEvent('keypress', eventInit));
            target.dispatchEvent(new KeyboardEvent('keyup', eventInit));
            return true;
        })()
        """;
    }

    private static string ToJavaScriptStringLiteral(string value)
    {
        return "'" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            + "'";
    }
}