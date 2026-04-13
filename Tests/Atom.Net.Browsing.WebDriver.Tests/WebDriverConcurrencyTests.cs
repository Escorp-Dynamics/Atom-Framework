using System.Collections.Concurrent;
using System.Drawing;
using System.Net;
using Atom.Media.Audio;
using Atom.Media.Audio.Backends;
using Atom.Media.Video;
using Atom.Media.Video.Backends;
using Atom.Net.Browsing.WebDriver.Protocol;
using RuntimeWebBrowser = Atom.Net.Browsing.WebDriver.WebBrowser;
using WebBrowser = Atom.Net.Browsing.WebDriver.Tests.WebDriverTestEnvironment;

namespace Atom.Net.Browsing.WebDriver.Tests;

[NonParallelizable]
[Category("Concurrency")]
public sealed class WebDriverConcurrencyTests
{
    private delegate bool TryDequeueBridgeMessage(out BridgeMessage? message);
    private readonly record struct LookupTarget(string Title, Uri Url);
    private const int BridgeEventsPerNavigation = 5;
    private const int LifecycleEventsPerNavigation = 3;
    private const string LocalCookieDomain = "127.0.0.1";
    private static readonly string[] ExpectedCookieNames = ["session", "preferences"];

    [Test]
    [Repeat(3)]
    public async Task PageNavigationStateConcurrentDrainPreservesAllNavigationEnvelopeMessages()
    {
        const string windowId = "window-concurrency";
        const string tabId = "tab-concurrency";
        const int navigationCount = 256;
        var state = new PageNavigationState(windowId, tabId);

        for (var index = 0; index < navigationCount; index++)
        {
            var url = new Uri($"https://127.0.0.1/state/{index}");
            _ = state.Navigate(url, new NavigationSettings
            {
                Html = $"<html><head><title>State {index}</title></head><body>{index}</body></html>",
            });
        }

        var messages = await DrainMessagesConcurrentlyAsync(state.TryDequeueEvent, navigationCount * BridgeEventsPerNavigation).ConfigureAwait(false);

        AssertNavigationEnvelope(messages, navigationCount, windowId, tabId);
    }

    [Test]
    [Repeat(3)]
    public async Task BridgeQueuesConcurrentDrainPreservesAllNavigationEnvelopeMessagesAcrossScopes()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)window.CurrentPage;
        const int navigationCount = 256;

        await browser.SetRequestInterceptionAsync(true).ConfigureAwait(false);

        for (var index = 0; index < navigationCount; index++)
        {
            var url = new Uri($"https://127.0.0.1/browser/{index}");
            await page.NavigateAsync(url, new NavigationSettings
            {
                Html = $"<html><head><title>Browser {index}</title></head><body>{index}</body></html>",
            }).ConfigureAwait(false);
        }

        var expectedMessages = navigationCount * BridgeEventsPerNavigation;
        var pageMessages = await DrainMessagesConcurrentlyAsync(page.TryDequeueBridgeEvent, expectedMessages).ConfigureAwait(false);
        var windowMessages = await DrainMessagesConcurrentlyAsync(window.TryDequeueBridgeEvent, expectedMessages).ConfigureAwait(false);
        var browserMessages = await DrainMessagesConcurrentlyAsync(browser.TryDequeueBridgeEvent, expectedMessages).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            AssertNavigationEnvelope(pageMessages, navigationCount, window.WindowId, page.TabId);
            AssertNavigationEnvelope(windowMessages, navigationCount, window.WindowId, page.TabId);
            AssertNavigationEnvelope(browserMessages, navigationCount, window.WindowId, page.TabId);
        });
    }

    [Test]
    [Repeat(2)]
    public async Task PageNavigationStateMixedConcurrentNavigateAndDrainPreservesAllNavigationEnvelopeMessages()
    {
        const string windowId = "window-mixed";
        const string tabId = "tab-mixed";
        const int producerCount = 8;
        const int navigationsPerProducer = 64;
        const int consumerCount = 32;
        var state = new PageNavigationState(windowId, tabId);
        var expectedNavigations = producerCount * navigationsPerProducer;
        var expectedMessages = expectedNavigations * BridgeEventsPerNavigation;
        var messages = new ConcurrentBag<BridgeMessage>();
        var errors = new ConcurrentQueue<Exception>();
        using var start = new ManualResetEventSlim(false);
        var observedCount = 0;
        var completedProducers = 0;

        var producers = Enumerable.Range(0, producerCount)
            .Select(producerIndex => Task.Run(() =>
            {
                try
                {
                    start.Wait();

                    for (var navigationIndex = 0; navigationIndex < navigationsPerProducer; navigationIndex++)
                    {
                        var sequence = (producerIndex * navigationsPerProducer) + navigationIndex;
                        _ = state.Navigate(new Uri($"https://127.0.0.1/mixed/{sequence}"), new NavigationSettings
                        {
                            Html = $"<html><head><title>Mixed {sequence}</title></head><body>{sequence}</body></html>",
                        });
                    }
                }
                catch (Exception exception)
                {
                    errors.Enqueue(exception);
                }
                finally
                {
                    Interlocked.Increment(ref completedProducers);
                }
            }))
            .ToArray();

        var consumers = Enumerable.Range(0, consumerCount)
            .Select(_ => Task.Run(() =>
            {
                try
                {
                    start.Wait();
                    var emptyReads = 0;

                    while (Volatile.Read(ref observedCount) < expectedMessages || Volatile.Read(ref completedProducers) < producerCount)
                    {
                        if (state.TryDequeueEvent(out var message))
                        {
                            emptyReads = 0;
                            if (message is not null)
                            {
                                messages.Add(message);
                            }

                            Interlocked.Increment(ref observedCount);
                            continue;
                        }

                        if (Volatile.Read(ref completedProducers) == producerCount && Volatile.Read(ref observedCount) >= expectedMessages)
                        {
                            break;
                        }

                        emptyReads++;
                        if (emptyReads > 8192)
                        {
                            Thread.Yield();
                            emptyReads = 0;
                        }
                    }
                }
                catch (Exception exception)
                {
                    errors.Enqueue(exception);
                }
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(producers.Concat(consumers)).ConfigureAwait(false);

        Assert.That(errors, Is.Empty, "Mixed concurrent navigation/drain must not throw.");
        AssertNavigationEnvelope(messages.ToArray(), expectedNavigations, windowId, tabId);
    }

    [Test]
    [Repeat(2)]
    public async Task BrowserMixedConcurrentOpenWindowAndReadCurrentWindowKeepsPublishedWindowInSnapshot()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        const int producerCount = 8;
        const int windowsPerProducer = 32;
        const int observerCount = 32;
        var createdWindowIds = new ConcurrentBag<string>();
        var errors = new ConcurrentQueue<Exception>();
        using var start = new ManualResetEventSlim(false);
        var completedProducers = 0;

        var producers = Enumerable.Range(0, producerCount)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    start.Wait();

                    for (var index = 0; index < windowsPerProducer; index++)
                    {
                        var window = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
                        createdWindowIds.Add(window.WindowId);
                    }
                }
                catch (Exception exception)
                {
                    errors.Enqueue(exception);
                }
                finally
                {
                    Interlocked.Increment(ref completedProducers);
                }
            }))
            .ToArray();

        var observers = Enumerable.Range(0, observerCount)
            .Select(_ => Task.Run(() =>
            {
                try
                {
                    start.Wait();

                    while (Volatile.Read(ref completedProducers) < producerCount)
                    {
                        var currentWindow = (WebWindow)browser.CurrentWindow;
                        var snapshot = browser.Windows.Cast<WebWindow>().ToArray();
                        if (!snapshot.Any(window => string.Equals(window.WindowId, currentWindow.WindowId, StringComparison.Ordinal)))
                        {
                            throw new AssertionException("Current window must always be present in browser window snapshot.");
                        }
                    }
                }
                catch (Exception exception)
                {
                    errors.Enqueue(exception);
                }
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(producers.Concat(observers)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, "Mixed open-window/current-window reads must not throw.");
            Assert.That(createdWindowIds, Is.Not.Empty);
            Assert.That(createdWindowIds.All(windowId => browser.Windows.Cast<WebWindow>().Any(window => string.Equals(window.WindowId, windowId, StringComparison.Ordinal))), Is.True);
        });
    }

    [Test]
    [Repeat(2)]
    public async Task WindowMixedConcurrentOpenPageAndReadCurrentPageKeepsPublishedPageInSnapshot()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        const int producerCount = 8;
        const int pagesPerProducer = 32;
        const int observerCount = 32;
        var createdPageIds = new ConcurrentBag<string>();
        var errors = new ConcurrentQueue<Exception>();
        using var start = new ManualResetEventSlim(false);
        var completedProducers = 0;

        var producers = Enumerable.Range(0, producerCount)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    start.Wait();

                    for (var index = 0; index < pagesPerProducer; index++)
                    {
                        var page = (WebPage)await window.OpenPageAsync().ConfigureAwait(false);
                        createdPageIds.Add(page.TabId);
                    }
                }
                catch (Exception exception)
                {
                    errors.Enqueue(exception);
                }
                finally
                {
                    Interlocked.Increment(ref completedProducers);
                }
            }))
            .ToArray();

        var observers = Enumerable.Range(0, observerCount)
            .Select(_ => Task.Run(() =>
            {
                try
                {
                    start.Wait();

                    while (Volatile.Read(ref completedProducers) < producerCount)
                    {
                        var currentPage = (WebPage)window.CurrentPage;
                        var snapshot = window.Pages.Cast<WebPage>().ToArray();
                        if (!snapshot.Any(page => string.Equals(page.TabId, currentPage.TabId, StringComparison.Ordinal)))
                        {
                            throw new AssertionException("CurrentPage must always point to a page that is already published in Pages.");
                        }

                        Thread.Yield();
                    }
                }
                catch (Exception exception)
                {
                    errors.Enqueue(exception);
                }
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(producers.Concat(observers)).ConfigureAwait(false);

        var pages = window.Pages.Cast<WebPage>().ToArray();
        var expectedCount = 1 + (producerCount * pagesPerProducer);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, "Concurrent page publication/read must not throw.");
            Assert.That(createdPageIds, Has.Count.EqualTo(producerCount * pagesPerProducer));
            Assert.That(pages, Has.Length.EqualTo(expectedCount));
            Assert.That(pages.Select(page => page.TabId).Distinct().Count(), Is.EqualTo(expectedCount));
            Assert.That(pages.Any(page => string.Equals(page.TabId, ((WebPage)window.CurrentPage).TabId, StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    [Repeat(2)]
    public async Task BrowserConcurrentDisposeAndOpenWindowAllowsOnlySuccessOrObjectDisposed()
    {
        var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        const int producerCount = 8;
        const int attemptsPerProducer = 256;
        const int disposerCount = 8;
        var errors = new ConcurrentQueue<Exception>();
        var successfulOpens = 0;
        var disposedOpens = 0;
        var disposeStarted = 0;
        using var start = new ManualResetEventSlim(false);

        try
        {
            var producers = Enumerable.Range(0, producerCount)
                .Select(_ => Task.Run(async () =>
                {
                    start.Wait();

                    for (var attempt = 0; attempt < attemptsPerProducer; attempt++)
                    {
                        try
                        {
                            await browser.OpenWindowAsync().ConfigureAwait(false);
                            Interlocked.Increment(ref successfulOpens);
                        }
                        catch (ObjectDisposedException)
                        {
                            Interlocked.Increment(ref disposedOpens);
                        }
                        catch (Exception exception)
                        {
                            errors.Enqueue(exception);
                        }

                        if ((attempt & 15) == 15 || Volatile.Read(ref disposeStarted) != 0)
                        {
                            await Task.Yield();
                        }
                    }
                }))
                .ToArray();

            var disposers = Enumerable.Range(0, disposerCount)
                .Select(_ => Task.Run(async () =>
                {
                    try
                    {
                        start.Wait();
                        Interlocked.Exchange(ref disposeStarted, 1);
                        await browser.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }))
                .ToArray();

            start.Set();
            await Task.WhenAll(producers.Concat(disposers)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(errors, Is.Empty, "Browser dispose/open race must not throw unexpected exceptions.");
                Assert.That(successfulOpens + disposedOpens, Is.EqualTo(producerCount * attemptsPerProducer));
                Assert.That(disposedOpens, Is.GreaterThan(0));
            });
        }
        finally
        {
            await browser.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    [Repeat(2)]
    public async Task WindowConcurrentDisposeAndOpenPageAllowsOnlySuccessOrObjectDisposed()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        const int producerCount = 8;
        const int attemptsPerProducer = 256;
        const int disposerCount = 8;
        var errors = new ConcurrentQueue<Exception>();
        var successfulOpens = 0;
        var disposedOpens = 0;
        var disposeStarted = 0;
        using var start = new ManualResetEventSlim(false);

        var producers = Enumerable.Range(0, producerCount)
            .Select(_ => Task.Run(async () =>
            {
                start.Wait();

                for (var attempt = 0; attempt < attemptsPerProducer; attempt++)
                {
                    try
                    {
                        await window.OpenPageAsync().ConfigureAwait(false);
                        Interlocked.Increment(ref successfulOpens);
                    }
                    catch (ObjectDisposedException)
                    {
                        Interlocked.Increment(ref disposedOpens);
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }

                    if ((attempt & 15) == 15 || Volatile.Read(ref disposeStarted) != 0)
                    {
                        await Task.Yield();
                    }
                }
            }))
            .ToArray();

        var disposers = Enumerable.Range(0, disposerCount)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    start.Wait();
                    Interlocked.Exchange(ref disposeStarted, 1);
                    await window.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    errors.Enqueue(exception);
                }
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(producers.Concat(disposers)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, "Window dispose/open race must not throw unexpected exceptions.");
            Assert.That(successfulOpens + disposedOpens, Is.EqualTo(producerCount * attemptsPerProducer));
            Assert.That(disposedOpens, Is.GreaterThan(0));
        });
    }

    [Test]
    [Repeat(2)]
    public async Task BrowserConcurrentDisposeAndNavigateAllowsOnlySuccessOrObjectDisposed()
    {
        var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        const int producerCount = 8;
        const int attemptsPerProducer = 256;
        const int disposerCount = 8;
        var errors = new ConcurrentQueue<Exception>();
        var successfulNavigations = 0;
        var disposedNavigations = 0;
        var disposeStarted = 0;
        using var start = new ManualResetEventSlim(false);

        try
        {
            var producers = Enumerable.Range(0, producerCount)
                .Select(producerIndex => Task.Run(async () =>
                {
                    start.Wait();

                    for (var attempt = 0; attempt < attemptsPerProducer; attempt++)
                    {
                        try
                        {
                            var sequence = (producerIndex * attemptsPerProducer) + attempt;
                            await browser.NavigateAsync(new Uri($"https://127.0.0.1/dispose-browser/{sequence}"), new NavigationSettings
                            {
                                Html = $"<html><head><title>Browser Dispose {sequence}</title></head><body>{sequence}</body></html>",
                            }).ConfigureAwait(false);
                            Interlocked.Increment(ref successfulNavigations);
                        }
                        catch (ObjectDisposedException)
                        {
                            Interlocked.Increment(ref disposedNavigations);
                        }
                        catch (Exception exception)
                        {
                            errors.Enqueue(exception);
                        }

                        if ((attempt & 15) == 15 || Volatile.Read(ref disposeStarted) != 0)
                        {
                            await Task.Yield();
                        }
                    }
                }))
                .ToArray();

            var disposers = Enumerable.Range(0, disposerCount)
                .Select(_ => Task.Run(async () =>
                {
                    try
                    {
                        start.Wait();
                        Interlocked.Exchange(ref disposeStarted, 1);
                        await browser.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }))
                .ToArray();

            start.Set();
            await Task.WhenAll(producers.Concat(disposers)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(errors, Is.Empty, "Browser dispose/navigate race must not throw unexpected exceptions.");
                Assert.That(successfulNavigations + disposedNavigations, Is.EqualTo(producerCount * attemptsPerProducer));
                Assert.That(disposedNavigations, Is.GreaterThan(0));
            });
        }
        finally
        {
            await browser.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    [Repeat(2)]
    public async Task WindowConcurrentDisposeAndNavigateAllowsOnlySuccessOrObjectDisposed()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        const int producerCount = 8;
        const int attemptsPerProducer = 256;
        const int disposerCount = 8;
        var errors = new ConcurrentQueue<Exception>();
        var successfulNavigations = 0;
        var disposedNavigations = 0;
        var disposeStarted = 0;
        using var start = new ManualResetEventSlim(false);

        var producers = Enumerable.Range(0, producerCount)
            .Select(producerIndex => Task.Run(async () =>
            {
                start.Wait();

                for (var attempt = 0; attempt < attemptsPerProducer; attempt++)
                {
                    try
                    {
                        var sequence = (producerIndex * attemptsPerProducer) + attempt;
                        await window.NavigateAsync(new Uri($"https://127.0.0.1/dispose-window/{sequence}"), new NavigationSettings
                        {
                            Html = $"<html><head><title>Window Dispose {sequence}</title></head><body>{sequence}</body></html>",
                        }).ConfigureAwait(false);
                        Interlocked.Increment(ref successfulNavigations);
                    }
                    catch (ObjectDisposedException)
                    {
                        Interlocked.Increment(ref disposedNavigations);
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }

                    if ((attempt & 15) == 15 || Volatile.Read(ref disposeStarted) != 0)
                    {
                        await Task.Yield();
                    }
                }
            }))
            .ToArray();

        var disposers = Enumerable.Range(0, disposerCount)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    start.Wait();
                    Interlocked.Exchange(ref disposeStarted, 1);
                    await window.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    errors.Enqueue(exception);
                }
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(producers.Concat(disposers)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, "Window dispose/navigate race must not throw unexpected exceptions.");
            Assert.That(successfulNavigations + disposedNavigations, Is.EqualTo(producerCount * attemptsPerProducer));
            Assert.That(disposedNavigations, Is.GreaterThan(0));
        });
    }

    [Test]
    [Repeat(2)]
    public async Task BridgeAndLifecycleFanOutRemainsConsistentUnderConcurrentNavigationStress()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)window.CurrentPage;
        var frame = (Frame)page.MainFrame;
        const int producerCount = 8;
        const int navigationsPerProducer = 32;
        const int subscriberCount = 8;
        var expectedBridgeMessages = producerCount * navigationsPerProducer * BridgeEventsPerNavigation;
        var expectedLifecycleMessages = producerCount * navigationsPerProducer * LifecycleEventsPerNavigation;
        var pageBridgeCounters = new int[subscriberCount];
        var windowBridgeCounters = new int[subscriberCount];
        var browserBridgeCounters = new int[subscriberCount];
        var pageLifecycleCounters = new int[subscriberCount];
        var windowLifecycleCounters = new int[subscriberCount];
        var browserLifecycleCounters = new int[subscriberCount];
        var frameLifecycleCounters = new int[subscriberCount];
        var errors = new ConcurrentQueue<Exception>();
        using var start = new ManualResetEventSlim(false);

        await browser.SetRequestInterceptionAsync(true).ConfigureAwait(false);

        for (var subscriberIndex = 0; subscriberIndex < subscriberCount; subscriberIndex++)
        {
            var index = subscriberIndex;
            page.BridgeEventReceived += _ => Interlocked.Increment(ref pageBridgeCounters[index]);
            window.BridgeEventReceived += _ => Interlocked.Increment(ref windowBridgeCounters[index]);
            browser.BridgeEventReceived += _ => Interlocked.Increment(ref browserBridgeCounters[index]);

            SubscribeLifecycle(page, () => Interlocked.Increment(ref pageLifecycleCounters[index]));
            SubscribeLifecycle(window, () => Interlocked.Increment(ref windowLifecycleCounters[index]));
            SubscribeLifecycle(browser, () => Interlocked.Increment(ref browserLifecycleCounters[index]));
            SubscribeLifecycle(frame, () => Interlocked.Increment(ref frameLifecycleCounters[index]));
        }

        var producers = Enumerable.Range(0, producerCount)
            .Select(producerIndex => Task.Run(async () =>
            {
                start.Wait();

                for (var navigationIndex = 0; navigationIndex < navigationsPerProducer; navigationIndex++)
                {
                    try
                    {
                        var sequence = (producerIndex * navigationsPerProducer) + navigationIndex;
                        await page.NavigateAsync(new Uri($"https://127.0.0.1/fanout/{sequence}"), new NavigationSettings
                        {
                            Html = $"<html><head><title>Fanout {sequence}</title></head><body>{sequence}</body></html>",
                        }).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(producers).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, "Concurrent navigation fan-out must not throw.");
            Assert.That(pageBridgeCounters, Is.All.EqualTo(expectedBridgeMessages));
            Assert.That(windowBridgeCounters, Is.All.EqualTo(expectedBridgeMessages));
            Assert.That(browserBridgeCounters, Is.All.EqualTo(expectedBridgeMessages));
            Assert.That(pageLifecycleCounters, Is.All.EqualTo(expectedLifecycleMessages));
            Assert.That(windowLifecycleCounters, Is.All.EqualTo(expectedLifecycleMessages));
            Assert.That(browserLifecycleCounters, Is.All.EqualTo(expectedLifecycleMessages));
            Assert.That(frameLifecycleCounters, Is.All.EqualTo(expectedLifecycleMessages));
        });
    }

    [Test]
    [Repeat(2)]
    public async Task SubscriberChurnAcrossBridgeAndLifecycleDoesNotBreakConcurrentDelivery()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        const int producerCount = 8;
        const int navigationsPerProducer = 32;
        const int churnerCount = 8;
        const int subscriptionsPerChurner = 128;
        var totalNavigations = producerCount * navigationsPerProducer;
        var errors = new ConcurrentQueue<Exception>();
        var bridgeHits = 0;
        var completedNavigations = 0;
        var lifecycleHits = 0;
        using var start = new ManualResetEventSlim(false);

        var producers = Enumerable.Range(0, producerCount)
            .Select(producerIndex => Task.Run(async () =>
            {
                start.Wait();

                for (var navigationIndex = 0; navigationIndex < navigationsPerProducer; navigationIndex++)
                {
                    try
                    {
                        var sequence = (producerIndex * navigationsPerProducer) + navigationIndex;
                        await page.NavigateAsync(new Uri($"https://127.0.0.1/churn/{sequence}"), new NavigationSettings
                        {
                            Html = $"<html><head><title>Churn {sequence}</title></head><body>{sequence}</body></html>",
                        }).ConfigureAwait(false);
                        Interlocked.Increment(ref completedNavigations);
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }
            }))
            .ToArray();

        var churners = Enumerable.Range(0, churnerCount)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();

                for (var subscriptionIndex = 0; subscriptionIndex < subscriptionsPerChurner; subscriptionIndex++)
                {
                    Action<BridgeMessage> bridgeHandler = _ => Interlocked.Increment(ref bridgeHits);
                    MutableEventHandler<IWebPage, WebLifecycleEventArgs> domHandler = (_, _) => Interlocked.Increment(ref lifecycleHits);
                    MutableEventHandler<IWebPage, WebLifecycleEventArgs> navigationHandler = (_, _) => Interlocked.Increment(ref lifecycleHits);
                    MutableEventHandler<IWebPage, WebLifecycleEventArgs> loadedHandler = (_, _) => Interlocked.Increment(ref lifecycleHits);

                    try
                    {
                        var completedSnapshot = Volatile.Read(ref completedNavigations);
                        page.BridgeEventReceived += bridgeHandler;
                        page.DomContentLoaded += domHandler;
                        page.NavigationCompleted += navigationHandler;
                        page.PageLoaded += loadedHandler;
                        SpinWait.SpinUntil(
                            () => Volatile.Read(ref completedNavigations) > completedSnapshot
                                || Volatile.Read(ref completedNavigations) == totalNavigations,
                            millisecondsTimeout: 50);
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                    finally
                    {
                        page.BridgeEventReceived -= bridgeHandler;
                        page.DomContentLoaded -= domHandler;
                        page.NavigationCompleted -= navigationHandler;
                        page.PageLoaded -= loadedHandler;
                    }
                }
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(producers.Concat(churners)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, "Subscriber churn must not throw during concurrent delivery.");
            Assert.That(bridgeHits, Is.GreaterThan(0));
            Assert.That(lifecycleHits, Is.GreaterThan(0));
        });
    }

    [Test]
    [Repeat(2)]
    public async Task ConcurrentNavigationAndReadPathsRemainConsistentAcrossPageFrameAndElement()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        var frame = page.MainFrame;
        var element = new Element(page);
        const int producerCount = 8;
        const int navigationsPerProducer = 32;
        const int readerCount = 24;
        var expectedUrls = Enumerable.Range(0, producerCount * navigationsPerProducer)
            .Select(index => $"https://127.0.0.1/read/{index}")
            .ToHashSet(StringComparer.Ordinal);
        var expectedTitles = Enumerable.Range(0, producerCount * navigationsPerProducer)
            .Select(index => $"Read {index}")
            .ToHashSet(StringComparer.Ordinal);
        var errors = new ConcurrentQueue<Exception>();
        var observedUrls = 0;
        var observedTitles = 0;
        using var start = new ManualResetEventSlim(false);
        var completedProducers = 0;

        var producers = Enumerable.Range(0, producerCount)
            .Select(producerIndex => Task.Run(async () =>
            {
                start.Wait();

                for (var navigationIndex = 0; navigationIndex < navigationsPerProducer; navigationIndex++)
                {
                    try
                    {
                        var sequence = (producerIndex * navigationsPerProducer) + navigationIndex;
                        await page.NavigateAsync(new Uri($"https://127.0.0.1/read/{sequence}"), new NavigationSettings
                        {
                            Html = $"<html><head><title>Read {sequence}</title></head><body>{sequence}</body></html>",
                        }).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }

                Interlocked.Increment(ref completedProducers);
            }))
            .ToArray();

        var readers = Enumerable.Range(0, readerCount)
            .Select(_ => Task.Run(async () =>
            {
                start.Wait();

                while (Volatile.Read(ref completedProducers) < producerCount)
                {
                    try
                    {
                        var pageUrl = await page.GetUrlAsync().ConfigureAwait(false);
                        var pageTitle = await page.GetTitleAsync().ConfigureAwait(false);
                        var pageContent = await page.GetContentAsync().ConfigureAwait(false);
                        var frameUrl = await frame.GetUrlAsync().ConfigureAwait(false);
                        var frameTitle = await frame.GetTitleAsync().ConfigureAwait(false);
                        var elementTitle = await element.EvaluateAsync<string>("document.title").ConfigureAwait(false);

                        if (pageUrl is not null)
                        {
                            Assert.That(expectedUrls.Contains(pageUrl.ToString()), Is.True);
                            Interlocked.Increment(ref observedUrls);
                        }

                        if (frameUrl is not null)
                        {
                            Assert.That(expectedUrls.Contains(frameUrl.ToString()), Is.True);
                            Interlocked.Increment(ref observedUrls);
                        }

                        if (pageTitle is not null)
                        {
                            Assert.That(expectedTitles.Contains(pageTitle), Is.True);
                            Interlocked.Increment(ref observedTitles);
                        }

                        if (frameTitle is not null)
                        {
                            Assert.That(expectedTitles.Contains(frameTitle), Is.True);
                            Interlocked.Increment(ref observedTitles);
                        }

                        if (elementTitle is not null)
                        {
                            Assert.That(expectedTitles.Contains(elementTitle), Is.True);
                            Interlocked.Increment(ref observedTitles);
                        }

                        if (pageContent is not null)
                        {
                            Assert.That(pageContent, Does.StartWith("<html>"));
                        }
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(producers.Concat(readers)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, "Concurrent navigation/read paths must not throw or produce impossible state.");
            Assert.That(observedUrls, Is.GreaterThan(0));
            Assert.That(observedTitles, Is.GreaterThan(0));
        });
    }

    [Test]
    [Repeat(2)]
    public async Task ConcurrentLookupByTitleAndUrlRemainsConsistentAcrossBrowserPageAndFrame()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        const int producerCount = 4;
        const int windowsPerProducer = 12;
        const int readerCount = 12;
        var producedTargets = new ConcurrentBag<LookupTarget>();
        var errors = new ConcurrentQueue<Exception>();
        var resolvedLookups = 0;
        using var start = new ManualResetEventSlim(false);
        var completedProducers = 0;

        var producers = Enumerable.Range(0, producerCount)
            .Select(producerIndex => Task.Run(async () =>
            {
                start.Wait();

                for (var windowIndex = 0; windowIndex < windowsPerProducer; windowIndex++)
                {
                    try
                    {
                        var sequence = (producerIndex * windowsPerProducer) + windowIndex;
                        var target = new LookupTarget($"Lookup {sequence}", new Uri($"https://127.0.0.1/lookup/{sequence}"));
                        var window = await browser.OpenWindowAsync().ConfigureAwait(false);
                        await window.NavigateAsync(target.Url, new NavigationSettings
                        {
                            Html = $"<html><head><title>{target.Title}</title></head><body>{sequence}</body></html>",
                        }).ConfigureAwait(false);
                        producedTargets.Add(target);
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }

                Interlocked.Increment(ref completedProducers);
            }))
            .ToArray();

        var readers = Enumerable.Range(0, readerCount)
            .Select(_ => Task.Run(async () =>
            {
                start.Wait();

                while (Volatile.Read(ref completedProducers) < producerCount)
                {
                    try
                    {
                        var snapshot = producedTargets.ToArray();
                        if (snapshot.Length == 0)
                        {
                            Thread.Yield();
                            continue;
                        }

                        var target = snapshot[Random.Shared.Next(snapshot.Length)];
                        var resolved = await ValidateLookupAsync(browser, target, requireResolution: false).ConfigureAwait(false);
                        Interlocked.Add(ref resolvedLookups, resolved);
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(producers.Concat(readers)).ConfigureAwait(false);

        foreach (var target in producedTargets)
        {
            try
            {
                var resolved = await ValidateLookupAsync(browser, target, requireResolution: true).ConfigureAwait(false);
                Interlocked.Add(ref resolvedLookups, resolved);
            }
            catch (Exception exception)
            {
                errors.Enqueue(exception);
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, "Concurrent browser/page/frame lookups must not throw or resolve to inconsistent entities.");
            Assert.That(resolvedLookups, Is.GreaterThan(0));
        });
    }

    [Test]
    [Repeat(2)]
    public async Task ConcurrentWindowLookupSeparatesCurrentTitleFromAnyPageUrl()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        const int producerCount = 4;
        const int windowsPerProducer = 8;
        const int pagesPerWindow = 4;
        const int readerCount = 8;
        var pageTargets = new ConcurrentDictionary<string, LookupTarget>(StringComparer.Ordinal);
        var currentWindowTargets = new ConcurrentDictionary<string, LookupTarget>(StringComparer.Ordinal);
        var errors = new ConcurrentQueue<Exception>();
        var resolvedLookups = 0;
        using var start = new ManualResetEventSlim(false);
        var completedProducers = 0;

        var producers = Enumerable.Range(0, producerCount)
            .Select(producerIndex => Task.Run(async () =>
            {
                start.Wait();

                for (var windowIndex = 0; windowIndex < windowsPerProducer; windowIndex++)
                {
                    try
                    {
                        var window = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);

                        for (var pageIndex = 0; pageIndex < pagesPerWindow; pageIndex++)
                        {
                            var page = pageIndex == 0
                                ? (WebPage)window.CurrentPage
                                : (WebPage)await window.OpenPageAsync().ConfigureAwait(false);
                            var sequence = (((producerIndex * windowsPerProducer) + windowIndex) * pagesPerWindow) + pageIndex;
                            var target = new LookupTarget($"Window Lookup {sequence}", new Uri($"https://127.0.0.1/window-lookup/{sequence}"));
                            await page.NavigateAsync(target.Url, new NavigationSettings
                            {
                                Html = $"<html><head><title>{target.Title}</title></head><body>{sequence}</body></html>",
                            }).ConfigureAwait(false);

                            pageTargets[target.Url.AbsoluteUri] = target;
                            currentWindowTargets[window.WindowId] = target;
                        }
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }

                Interlocked.Increment(ref completedProducers);
            }))
            .ToArray();

        var readers = Enumerable.Range(0, readerCount)
            .Select(_ => Task.Run(async () =>
            {
                start.Wait();

                while (Volatile.Read(ref completedProducers) < producerCount || Volatile.Read(ref resolvedLookups) == 0)
                {
                    try
                    {
                        var pageSnapshot = pageTargets.Values.ToArray();
                        if (pageSnapshot.Length > 0)
                        {
                            var target = pageSnapshot[Random.Shared.Next(pageSnapshot.Length)];
                            var windowByUrl = await browser.GetWindowAsync(target.Url).ConfigureAwait(false);
                            if (windowByUrl is not null)
                            {
                                var pageByUrl = await windowByUrl.GetPageAsync(target.Url).ConfigureAwait(false);
                                Assert.That(pageByUrl, Is.Not.Null);
                                Assert.That(await pageByUrl!.GetUrlAsync().ConfigureAwait(false), Is.EqualTo(target.Url));
                                Interlocked.Increment(ref resolvedLookups);
                            }
                        }

                        var currentSnapshot = currentWindowTargets.Values.ToArray();
                        if (currentSnapshot.Length > 0)
                        {
                            var currentTarget = currentSnapshot[Random.Shared.Next(currentSnapshot.Length)];
                            var windowByTitle = await browser.GetWindowAsync(currentTarget.Title).ConfigureAwait(false);
                            if (windowByTitle is not null)
                            {
                                Interlocked.Increment(ref resolvedLookups);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(producers.Concat(readers)).ConfigureAwait(false);

        foreach (var target in pageTargets.Values)
        {
            try
            {
                var resolved = await ValidateLookupAsync(
                    browser,
                    target,
                    requireResolution: true,
                    requireWindowResolution: false).ConfigureAwait(false);
                Interlocked.Add(ref resolvedLookups, resolved);
            }
            catch (Exception exception)
            {
                errors.Enqueue(exception);
            }
        }

        foreach (var target in currentWindowTargets.Values)
        {
            try
            {
                var resolved = await ValidateLookupAsync(
                    browser,
                    target,
                    requireResolution: true,
                    requireWindowResolution: true).ConfigureAwait(false);
                Interlocked.Add(ref resolvedLookups, resolved);
            }
            catch (Exception exception)
            {
                errors.Enqueue(exception);
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, "Concurrent window lookup must preserve current-title and any-page-url semantics.");
            Assert.That(pageTargets.Count, Is.EqualTo(producerCount * windowsPerProducer * pagesPerWindow));
            Assert.That(currentWindowTargets.Count, Is.EqualTo(producerCount * windowsPerProducer));
            Assert.That(resolvedLookups, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task BrowserLookupAfterDisposeFailsFast()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var target = new Uri("https://127.0.0.1/post-dispose-browser");
        var element = new Element((WebPage)browser.CurrentPage);
        await browser.DisposeAsync().ConfigureAwait(false);

        Assert.That(async () => await browser.OpenWindowAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await browser.ClearAllCookiesAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await browser.GetWindowAsync("current").ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await browser.GetWindowAsync(target).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await browser.GetWindowAsync(element).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await browser.GetPageAsync("current").ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await browser.GetPageAsync(target).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await browser.GetPageAsync(element).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
    }

    [Test]
    public async Task BrowserClearAllCookiesSkipsDisposedCurrentWindowAndFansOutAcrossLiveWindows()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var initialWindow = (WebWindow)browser.CurrentWindow;
        var transientCurrentWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        await transientCurrentWindow.DisposeAsync().ConfigureAwait(false);

        Assert.That(async () => await browser.ClearAllCookiesAsync().ConfigureAwait(false), Throws.Nothing);
        Assert.That(browser.CurrentWindow, Is.SameAs(initialWindow));
        Assert.That(browser.Windows, Has.None.SameAs(transientCurrentWindow));
        Assert.That(browser.Windows, Has.Some.SameAs(initialWindow));
        Assert.That(initialWindow.IsDisposed, Is.False);
        Assert.That(transientCurrentWindow.IsDisposed, Is.True);
    }

    [Test]
    public async Task WindowClearAllCookiesSkipsDisposedCurrentPageAndFansOutAcrossLivePages()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var initialPage = (WebPage)window.CurrentPage;
        var transientCurrentPage = (WebPage)await window.OpenPageAsync().ConfigureAwait(false);
        await transientCurrentPage.DisposeAsync().ConfigureAwait(false);

        Assert.That(async () => await window.ClearAllCookiesAsync().ConfigureAwait(false), Throws.Nothing);
        Assert.That(window.CurrentPage, Is.SameAs(initialPage));
        Assert.That(window.Pages, Has.None.SameAs(transientCurrentPage));
        Assert.That(window.Pages, Has.Some.SameAs(initialPage));
        Assert.That(initialPage.IsDisposed, Is.False);
        Assert.That(transientCurrentPage.IsDisposed, Is.True);
    }

    [Test]
    public async Task WindowLookupAndInspectionAfterDisposeFailFast()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var target = new Uri("https://127.0.0.1/post-dispose-window");
        var element = new Element((WebPage)window.CurrentPage);
        await window.DisposeAsync().ConfigureAwait(false);

        Assert.That(async () => await window.OpenPageAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await window.GetPageAsync("current").ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await window.GetPageAsync(target).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await window.GetPageAsync(element).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await window.GetUrlAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await window.GetTitleAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await window.ClearAllCookiesAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await window.GetBoundingBoxAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
    }

    [Test]
    public async Task PropertySurfaceAfterDisposeRemainsReadableAsFinalSnapshot()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var page = (WebPage)window.CurrentPage;
        var frame = (Frame)page.MainFrame;
        var element = new Element(page, frame);
        var shadowRoot = new ShadowRoot(element, page, frame);
        await browser.DisposeAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentWindow, Is.SameAs(window));
            Assert.That(browser.CurrentPage, Is.SameAs(page));
            Assert.That(browser.Windows, Is.Empty);
            Assert.That(browser.Pages, Is.Empty);
            Assert.That(window.Browser, Is.SameAs(browser));
            Assert.That(window.CurrentPage, Is.SameAs(page));
            Assert.That(window.Pages, Is.Empty);
            Assert.That(page.Window, Is.SameAs(window));
            Assert.That(page.MainFrame, Is.SameAs(frame));
            Assert.That(page.Frames, Has.Some.SameAs(frame));
            Assert.That(frame.Page, Is.SameAs(page));
            Assert.That(frame.Host, Is.Null);
            Assert.That(element.Page, Is.SameAs(page));
            Assert.That(element.Frame, Is.SameAs(frame));
            Assert.That(shadowRoot.Host, Is.SameAs(element));
            Assert.That(shadowRoot.Page, Is.SameAs(page));
            Assert.That(shadowRoot.Frame, Is.SameAs(frame));
            Assert.That(shadowRoot.Frames, Is.Empty);
        });
    }

    [Test]
    public async Task LivePropertySurfacePromotesNextWindowAndPageAfterCurrentChildDispose()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var stableWindow = (WebWindow)browser.CurrentWindow;
        var transientWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var stablePage = (WebPage)stableWindow.CurrentPage;
        var transientPage = (WebPage)await stableWindow.OpenPageAsync().ConfigureAwait(false);

        await transientWindow.DisposeAsync().ConfigureAwait(false);
        await transientPage.DisposeAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentWindow, Is.SameAs(stableWindow));
            Assert.That(browser.Windows, Has.None.SameAs(transientWindow));
            Assert.That(browser.Windows, Has.Some.SameAs(stableWindow));
            Assert.That(stableWindow.CurrentPage, Is.SameAs(stablePage));
            Assert.That(stableWindow.Pages, Has.None.SameAs(transientPage));
            Assert.That(stableWindow.Pages, Has.Some.SameAs(stablePage));
        });
    }

    [Test]
    public async Task WindowPromotionAfterCurrentWindowDisposeAllowsSubsequentWindowOpenInSameBrowser()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var promotedWindow = (WebWindow)browser.CurrentWindow;
        var disposedCurrentWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);

        await disposedCurrentWindow.DisposeAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(disposedCurrentWindow.IsDisposed, Is.True);
            Assert.That(browser.CurrentWindow, Is.SameAs(promotedWindow));
            Assert.That(browser.Windows, Has.None.SameAs(disposedCurrentWindow));
            Assert.That(browser.Windows, Has.Some.SameAs(promotedWindow));
        });

        var reopenedWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(reopenedWindow, Is.Not.SameAs(promotedWindow));
            Assert.That(reopenedWindow, Is.Not.SameAs(disposedCurrentWindow));
            Assert.That(reopenedWindow.Browser, Is.SameAs(browser));
            Assert.That(browser.CurrentWindow, Is.SameAs(reopenedWindow));
            Assert.That(browser.Windows, Has.Some.SameAs(promotedWindow));
            Assert.That(browser.Windows, Has.Some.SameAs(reopenedWindow));
            Assert.That(browser.Windows, Has.None.SameAs(disposedCurrentWindow));
        });
    }

    [Test]
    public async Task PagePromotionAfterCurrentPageDisposeAllowsSubsequentPageOpenInSameWindow()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var promotedPage = (WebPage)window.CurrentPage;
        var disposedCurrentPage = (WebPage)await window.OpenPageAsync().ConfigureAwait(false);

        await disposedCurrentPage.DisposeAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(disposedCurrentPage.IsDisposed, Is.True);
            Assert.That(window.CurrentPage, Is.SameAs(promotedPage));
            Assert.That(browser.CurrentPage, Is.SameAs(promotedPage));
            Assert.That(window.Pages, Has.None.SameAs(disposedCurrentPage));
        });

        var reopenedPage = (WebPage)await window.OpenPageAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(reopenedPage, Is.Not.SameAs(promotedPage));
            Assert.That(reopenedPage, Is.Not.SameAs(disposedCurrentPage));
            Assert.That(reopenedPage.Window, Is.SameAs(window));
            Assert.That(window.CurrentPage, Is.SameAs(reopenedPage));
            Assert.That(browser.CurrentPage, Is.SameAs(reopenedPage));
            Assert.That(window.Pages, Has.Some.SameAs(promotedPage));
            Assert.That(window.Pages, Has.Some.SameAs(reopenedPage));
            Assert.That(window.Pages, Has.None.SameAs(disposedCurrentPage));
        });
    }

    [Test]
    public async Task WindowNavigateAsyncUsesPromotedPageAfterCurrentPageDispose()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var promotedPage = (WebPage)window.CurrentPage;
        var disposedCurrentPage = (WebPage)await window.OpenPageAsync().ConfigureAwait(false);
        var target = new Uri("https://127.0.0.1/promoted-page-navigation");

        await disposedCurrentPage.DisposeAsync().ConfigureAwait(false);
        await window.NavigateAsync(target, new NavigationSettings
        {
            Html = "<html><head><title>Promoted Page Navigation</title></head><body>promoted</body></html>",
        }).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(window.CurrentPage, Is.SameAs(promotedPage));
            Assert.That(browser.CurrentPage, Is.SameAs(promotedPage));
            Assert.That(promotedPage.CurrentUrl, Is.EqualTo(target));
            Assert.That(promotedPage.CurrentTitle, Is.EqualTo("Promoted Page Navigation"));
            Assert.That(window.Pages, Has.Some.SameAs(promotedPage));
            Assert.That(window.Pages, Has.None.SameAs(disposedCurrentPage));
        });
    }

    [Test]
    public async Task DirectDisposeOfNonCurrentPageRemovesItFromLiveCollectionsAndLookup()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var disposedNonCurrentPage = (WebPage)window.CurrentPage;
        var target = new Uri("https://127.0.0.1/disposed-noncurrent-page");
        await disposedNonCurrentPage.NavigateAsync(target, new NavigationSettings
        {
            Html = "<html><head><title>Disposed NonCurrent Page</title></head><body>disposed</body></html>",
        }).ConfigureAwait(false);

        var stableCurrentPage = (WebPage)await window.OpenPageAsync().ConfigureAwait(false);
        await disposedNonCurrentPage.DisposeAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(window.CurrentPage, Is.SameAs(stableCurrentPage));
            Assert.That(window.Pages, Has.None.SameAs(disposedNonCurrentPage));
            Assert.That(window.Pages, Has.Some.SameAs(stableCurrentPage));
            Assert.That(browser.Pages, Has.None.SameAs(disposedNonCurrentPage));
            Assert.That(browser.Pages, Has.Some.SameAs(stableCurrentPage));
        });

        Assert.That(await window.GetPageAsync(target).ConfigureAwait(false), Is.Null);
        Assert.That(await window.GetPageAsync("Disposed NonCurrent Page").ConfigureAwait(false), Is.Null);
        Assert.That(await browser.GetPageAsync(target).ConfigureAwait(false), Is.Null);
        Assert.That(await browser.GetPageAsync("Disposed NonCurrent Page").ConfigureAwait(false), Is.Null);
    }

    [Test]
    public async Task DirectDisposeOfLastPageKeepsLastSnapshotUntilReplacementPageOpens()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var lastPage = (WebPage)window.CurrentPage;
        var target = new Uri("https://127.0.0.1/last-page-dispose");
        await lastPage.NavigateAsync(target, new NavigationSettings
        {
            Html = "<html><head><title>Last Page Snapshot</title></head><body>last</body></html>",
        }).ConfigureAwait(false);

        await lastPage.DisposeAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(window.CurrentPage, Is.SameAs(lastPage));
            Assert.That(window.Pages, Is.Empty);
            Assert.That(browser.CurrentPage, Is.SameAs(lastPage));
            Assert.That(browser.Pages, Is.Empty);
        });

        Assert.That(await window.GetPageAsync("current").ConfigureAwait(false), Is.SameAs(lastPage));
        Assert.That(await browser.GetPageAsync("current").ConfigureAwait(false), Is.SameAs(lastPage));
        Assert.That(async () => await window.NavigateAsync(target).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await window.ReloadAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await window.GetUrlAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await window.GetTitleAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());

        var replacementPage = (WebPage)await window.OpenPageAsync().ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(window.CurrentPage, Is.SameAs(replacementPage));
            Assert.That(window.Pages, Has.Some.SameAs(replacementPage));
            Assert.That(browser.CurrentPage, Is.SameAs(replacementPage));
            Assert.That(browser.Pages, Has.Some.SameAs(replacementPage));
        });
    }

    [Test]
    public async Task DirectCloseOfLastWindowKeepsLastSnapshotUntilReplacementWindowOpens()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var lastWindow = (WebWindow)browser.CurrentWindow;
        var lastPage = (WebPage)lastWindow.CurrentPage;
        var target = new Uri("https://127.0.0.1/last-window-close");
        await lastPage.NavigateAsync(target, new NavigationSettings
        {
            Html = "<html><head><title>Last Window Snapshot</title></head><body>last-window</body></html>",
        }).ConfigureAwait(false);

        await lastWindow.CloseAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(lastWindow.IsDisposed, Is.True);
            Assert.That(lastPage.IsDisposed, Is.True);
            Assert.That(browser.CurrentWindow, Is.SameAs(lastWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(lastPage));
            Assert.That(browser.Windows, Is.Empty);
            Assert.That(browser.Pages, Is.Empty);
        });

        Assert.That(await browser.GetWindowAsync("current").ConfigureAwait(false), Is.SameAs(lastWindow));
        Assert.That(await browser.GetPageAsync("current").ConfigureAwait(false), Is.SameAs(lastPage));
        Assert.That(await browser.GetWindowAsync("Last Window Snapshot").ConfigureAwait(false), Is.Null);
        Assert.That(await browser.GetPageAsync(target).ConfigureAwait(false), Is.Null);
        Assert.That(async () => await lastWindow.OpenPageAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await lastWindow.GetPageAsync("current").ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());

        var replacementWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var replacementPage = (WebPage)replacementWindow.CurrentPage;

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentWindow, Is.SameAs(replacementWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(replacementPage));
            Assert.That(browser.Windows, Has.Some.SameAs(replacementWindow));
            Assert.That(browser.Pages, Has.Some.SameAs(replacementPage));
            Assert.That(browser.Windows, Has.None.SameAs(lastWindow));
            Assert.That(browser.Pages, Has.None.SameAs(lastPage));
        });
    }

    [Test]
    public async Task BrowserLookupSkipsWindowWhoseRetainedCurrentPageIsAlreadyDisposed()
    {
        using var cameraOverride = VirtualCamera.PushBackendFactoryOverride(static () => new FakeVirtualCameraBackend());
        using var microphoneOverride = VirtualMicrophone.PushBackendFactoryOverride(static () => new FakeVirtualMicrophoneBackend());
        await using var camera = await VirtualCamera.CreateAsync(new VirtualCameraSettings
        {
            Width = 1,
            Height = 1,
        }).ConfigureAwait(false);
        await using var microphone = await VirtualMicrophone.CreateAsync(new VirtualMicrophoneSettings()).ConfigureAwait(false);
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var liveWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var livePage = (WebPage)liveWindow.CurrentPage;
        await livePage.NavigateAsync(new Uri("https://127.0.0.1/live-window"), new NavigationSettings
        {
            Html = "<html><head><title>Live Window</title></head><body>live</body></html>",
        }).ConfigureAwait(false);

        var retainedWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var retainedPage = (WebPage)retainedWindow.CurrentPage;
        await retainedPage.NavigateAsync(new Uri("https://127.0.0.1/retained-current-page"), new NavigationSettings
        {
            Html = "<html><head><title>Disposed Current Page</title></head><body>disposed</body></html>",
        }).ConfigureAwait(false);
        await retainedPage.DisposeAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentWindow, Is.SameAs(retainedWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(retainedPage));
            Assert.That(browser.CurrentPage.Window, Is.SameAs(browser.CurrentWindow));
            Assert.That(browser.Pages, Has.None.SameAs(retainedPage));
            Assert.That(browser.Pages, Has.Some.SameAs(livePage));
        });

        Assert.That(await browser.GetWindowAsync("current").ConfigureAwait(false), Is.SameAs(retainedWindow));
        Assert.That(await browser.GetPageAsync("current").ConfigureAwait(false), Is.SameAs(retainedPage));
        Assert.That(await browser.GetWindowAsync("Disposed Current Page").ConfigureAwait(false), Is.Null);
        Assert.That(await browser.GetWindowAsync("Live Window").ConfigureAwait(false), Is.SameAs(liveWindow));
        Assert.That(await browser.GetPageAsync(new Uri("https://127.0.0.1/live-window")).ConfigureAwait(false), Is.SameAs(livePage));
        Assert.That(await browser.GetPageAsync("Live Window").ConfigureAwait(false), Is.SameAs(livePage));
        Assert.That(async () => await browser.NavigateAsync(new Uri("https://127.0.0.1/browser-current-navigation")).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await browser.ReloadAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await browser.AttachVirtualCameraAsync(camera).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await browser.AttachVirtualMicrophoneAsync(microphone).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
    }

    [Test]
    public async Task CurrentKeywordLookupTakesPrecedenceOverLiteralCurrentTitleMatches()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var literalCurrentWindow = (WebWindow)browser.CurrentWindow;
        var literalCurrentWindowPage = (WebPage)literalCurrentWindow.CurrentPage;
        await literalCurrentWindowPage.NavigateAsync(new Uri("https://127.0.0.1/window-title-current"), new NavigationSettings
        {
            Html = "<html><head><title>current</title></head><body>window</body></html>",
        }).ConfigureAwait(false);

        var actualCurrentWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var literalCurrentPage = (WebPage)actualCurrentWindow.CurrentPage;
        await literalCurrentPage.NavigateAsync(new Uri("https://127.0.0.1/page-title-current"), new NavigationSettings
        {
            Html = "<html><head><title>current</title></head><body>page</body></html>",
        }).ConfigureAwait(false);

        var actualCurrentPage = (WebPage)await actualCurrentWindow.OpenPageAsync().ConfigureAwait(false);
        await actualCurrentPage.NavigateAsync(new Uri("https://127.0.0.1/actual-current-page"), new NavigationSettings
        {
            Html = "<html><head><title>Actual Current Page</title></head><body>actual</body></html>",
        }).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentWindow, Is.SameAs(actualCurrentWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(actualCurrentPage));
            Assert.That(actualCurrentWindow.CurrentPage, Is.SameAs(actualCurrentPage));
        });

        Assert.That(await browser.GetWindowAsync("current").ConfigureAwait(false), Is.SameAs(actualCurrentWindow));
        Assert.That(await browser.GetPageAsync("current").ConfigureAwait(false), Is.SameAs(actualCurrentPage));
        Assert.That(await actualCurrentWindow.GetPageAsync("current").ConfigureAwait(false), Is.SameAs(actualCurrentPage));
        Assert.That(await browser.GetWindowAsync(new Uri("https://127.0.0.1/window-title-current")).ConfigureAwait(false), Is.SameAs(literalCurrentWindow));
        Assert.That(await browser.GetPageAsync(new Uri("https://127.0.0.1/page-title-current")).ConfigureAwait(false), Is.SameAs(literalCurrentPage));
        Assert.That(await actualCurrentWindow.GetPageAsync(new Uri("https://127.0.0.1/page-title-current")).ConfigureAwait(false), Is.SameAs(literalCurrentPage));
    }

    [Test]
    public async Task DuplicateUrlLookupRemainsStableForUnchangedLiveSnapshot()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var sharedUrl = new Uri("https://127.0.0.1/shared-lookup-target");

        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstWindowPage = (WebPage)firstWindow.CurrentPage;
        await firstWindowPage.NavigateAsync(sharedUrl, new NavigationSettings
        {
            Html = "<html><head><title>Shared A</title></head><body>a</body></html>",
        }).ConfigureAwait(false);

        var secondPageInFirstWindow = (WebPage)await firstWindow.OpenPageAsync().ConfigureAwait(false);
        await secondPageInFirstWindow.NavigateAsync(sharedUrl, new NavigationSettings
        {
            Html = "<html><head><title>Shared B</title></head><body>b</body></html>",
        }).ConfigureAwait(false);

        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var secondWindowPage = (WebPage)secondWindow.CurrentPage;
        await secondWindowPage.NavigateAsync(sharedUrl, new NavigationSettings
        {
            Html = "<html><head><title>Shared C</title></head><body>c</body></html>",
        }).ConfigureAwait(false);

        var windowLookup1 = await browser.GetWindowAsync(sharedUrl).ConfigureAwait(false);
        var windowLookup2 = await browser.GetWindowAsync(sharedUrl).ConfigureAwait(false);
        var browserPageLookup1 = await browser.GetPageAsync(sharedUrl).ConfigureAwait(false);
        var browserPageLookup2 = await browser.GetPageAsync(sharedUrl).ConfigureAwait(false);
        var windowPageLookup1 = await firstWindow.GetPageAsync(sharedUrl).ConfigureAwait(false);
        var windowPageLookup2 = await firstWindow.GetPageAsync(sharedUrl).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(windowLookup1, Is.SameAs(windowLookup2));
            Assert.That(windowLookup1, Is.AnyOf(firstWindow, secondWindow));
            Assert.That(browserPageLookup1, Is.SameAs(browserPageLookup2));
            Assert.That(browserPageLookup1, Is.AnyOf(firstWindowPage, secondPageInFirstWindow, secondWindowPage));
            Assert.That(windowPageLookup1, Is.SameAs(windowPageLookup2));
            Assert.That(windowPageLookup1, Is.AnyOf(firstWindowPage, secondPageInFirstWindow));
        });
    }

    [Test]
    public async Task MainFrameLookupRemainsStableAcrossNameUrlAndElementPaths()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        var frame = (Frame)page.MainFrame;
        var url = new Uri("https://127.0.0.1/main-frame-lookup");
        await page.NavigateAsync(url, new NavigationSettings
        {
            Html = "<html><head><title>Main Frame Lookup</title></head><body>frame</body></html>",
        }).ConfigureAwait(false);
        var element = new Element(page, frame);

        var frameByName = await page.GetFrameAsync("MainFrame").ConfigureAwait(false);
        var frameByCaseVariantName = await page.GetFrameAsync("mainframe").ConfigureAwait(false);
        var frameByUrl = await page.GetFrameAsync(url).ConfigureAwait(false);
        var frameByElement = await page.GetFrameAsync(element).ConfigureAwait(false);
        var frameName = await frame.GetNameAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(frameByName, Is.SameAs(frame));
            Assert.That(frameByCaseVariantName, Is.SameAs(frame));
            Assert.That(frameByUrl, Is.SameAs(frame));
            Assert.That(frameByElement, Is.SameAs(frame));
            Assert.That(frameName, Is.EqualTo(nameof(IWebPage.MainFrame)));
        });
    }

    [Test]
    public async Task ChildFrameHostSurfacePreservesParentNameAndHandle()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        var mainFrame = (Frame)page.MainFrame;
        var hostState = HtmlFallbackElementState.CreateResolved(
            "iframe",
            string.Empty,
            string.Empty,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = "embedded-frame",
            },
            "/html[1]/body[1]/iframe[1]");
        var hostElement = new Element(page, mainFrame, bridgeElementId: "iframe-host-element", fallbackState: hostState);
        var childFrame = new Frame(page, mainFrame, hostElement);

        var frameElement = await childFrame.GetFrameElementAsync().ConfigureAwait(false);
        var frameElementHandle = await childFrame.GetFrameElementHandleAsync().ConfigureAwait(false);
        var frameName = await childFrame.GetNameAsync().ConfigureAwait(false);
        var parentFrame = await childFrame.GetParentFrameAsync().ConfigureAwait(false);
        var childFrames = (await mainFrame.GetChildFramesAsync().ConfigureAwait(false)).ToArray();
        var contentFrame = await childFrame.GetContentFrameAsync().ConfigureAwait(false);
        var hostChildFrames = (await hostElement.GetChildFramesAsync().ConfigureAwait(false)).ToArray();
        var frameByName = await page.GetFrameAsync("embedded-frame").ConfigureAwait(false);
        var pageFrames = page.Frames.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(frameElement, Is.SameAs(hostElement));
            Assert.That(frameElementHandle, Is.EqualTo("iframe-host-element"));
            Assert.That(frameName, Is.EqualTo("embedded-frame"));
            Assert.That(parentFrame, Is.SameAs(mainFrame));
            Assert.That(childFrames, Has.Length.EqualTo(1));
            Assert.That(childFrames[0], Is.SameAs(childFrame));
            Assert.That(contentFrame, Is.SameAs(childFrame));
            Assert.That(hostChildFrames, Has.Length.EqualTo(1));
            Assert.That(hostChildFrames[0], Is.SameAs(childFrame));
            Assert.That(frameByName, Is.SameAs(childFrame));
            Assert.That(pageFrames, Has.Length.EqualTo(2));
            Assert.That(pageFrames[0], Is.SameAs(mainFrame));
            Assert.That(pageFrames[1], Is.SameAs(childFrame));
        });
    }

    [Test]
    public async Task ElementLookupResolvesOwningWindowAndPageAcrossNonCurrentWindowTab()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());

        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstWindowInitialPage = (WebPage)firstWindow.CurrentPage;
        await firstWindowInitialPage.NavigateAsync(new Uri("https://127.0.0.1/lookup/first-window/initial"), new NavigationSettings
        {
            Html = "<html><head><title>First Window Initial</title></head><body>initial</body></html>",
        }).ConfigureAwait(false);

        var firstWindowCurrentPage = (WebPage)await firstWindow.OpenPageAsync().ConfigureAwait(false);
        await firstWindowCurrentPage.NavigateAsync(new Uri("https://127.0.0.1/lookup/first-window/current"), new NavigationSettings
        {
            Html = "<html><head><title>First Window Current</title></head><body>current</body></html>",
        }).ConfigureAwait(false);

        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var secondWindowPage = (WebPage)secondWindow.CurrentPage;
        await secondWindowPage.NavigateAsync(new Uri("https://127.0.0.1/lookup/second-window/current"), new NavigationSettings
        {
            Html = "<html><head><title>Second Window Current</title></head><body>second</body></html>",
        }).ConfigureAwait(false);

        var elementFromNonCurrentPage = new Element(firstWindowInitialPage, firstWindowInitialPage.MainFrame);

        var browserWindowByElement = await browser.GetWindowAsync(elementFromNonCurrentPage).ConfigureAwait(false);
        var browserPageByElement = await browser.GetPageAsync(elementFromNonCurrentPage).ConfigureAwait(false);
        var firstWindowPageByElement = await firstWindow.GetPageAsync(elementFromNonCurrentPage).ConfigureAwait(false);
        var secondWindowPageByElement = await secondWindow.GetPageAsync(elementFromNonCurrentPage).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(secondWindowPage));
            Assert.That(firstWindow.CurrentPage, Is.SameAs(firstWindowCurrentPage));
            Assert.That(browserWindowByElement, Is.SameAs(firstWindow));
            Assert.That(browserPageByElement, Is.SameAs(firstWindowInitialPage));
            Assert.That(firstWindowPageByElement, Is.SameAs(firstWindowInitialPage));
            Assert.That(secondWindowPageByElement, Is.Null);
        });
    }

    [Test]
    public async Task OpenPageAsyncInNonCurrentWindowPreservesBrowserCurrentBoundaryAndCollections()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());

        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstWindowInitialPage = (WebPage)firstWindow.CurrentPage;
        await firstWindowInitialPage.NavigateAsync(new Uri("https://127.0.0.1/open-page/first-window/initial"), new NavigationSettings
        {
            Html = "<html><head><title>First Window Initial Page</title></head><body>first</body></html>",
        }).ConfigureAwait(false);

        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var secondWindowCurrentPage = (WebPage)secondWindow.CurrentPage;
        await secondWindowCurrentPage.NavigateAsync(new Uri("https://127.0.0.1/open-page/second-window/current"), new NavigationSettings
        {
            Html = "<html><head><title>Second Window Current Page</title></head><body>second</body></html>",
        }).ConfigureAwait(false);

        var openedPageInNonCurrentWindow = (WebPage)await firstWindow.OpenPageAsync(new WebPageSettings()).ConfigureAwait(false);
        var openedPageUrl = new Uri("https://127.0.0.1/open-page/first-window/opened");
        await openedPageInNonCurrentWindow.NavigateAsync(openedPageUrl, new NavigationSettings
        {
            Html = "<html><head><title>Opened In First Window</title></head><body>opened</body></html>",
        }).ConfigureAwait(false);

        var browserWindowByOpenedPageUrl = await browser.GetWindowAsync(openedPageUrl).ConfigureAwait(false);
        var browserPageByOpenedPageUrl = await browser.GetPageAsync(openedPageUrl).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(secondWindowCurrentPage));
            Assert.That(firstWindow.CurrentPage, Is.SameAs(openedPageInNonCurrentWindow));
            Assert.That(firstWindow.Pages.Count(), Is.EqualTo(2));
            Assert.That(firstWindow.Pages, Has.Member(firstWindowInitialPage));
            Assert.That(firstWindow.Pages, Has.Member(openedPageInNonCurrentWindow));
            Assert.That(secondWindow.Pages.Count(), Is.EqualTo(1));
            Assert.That(secondWindow.Pages, Has.Member(secondWindowCurrentPage));
            Assert.That(browser.Pages.Count(), Is.EqualTo(3));
            Assert.That(browserWindowByOpenedPageUrl, Is.SameAs(firstWindow));
            Assert.That(browserPageByOpenedPageUrl, Is.SameAs(openedPageInNonCurrentWindow));
        });
    }

    [Test]
    public async Task BrowserWindowTitleLookupUsesOnlyCurrentPageWhilePageLookupSeesNonCurrentTab()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());

        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstWindowNonCurrentPage = (WebPage)firstWindow.CurrentPage;
        await firstWindowNonCurrentPage.NavigateAsync(new Uri("https://127.0.0.1/title-lookup/first-window/non-current"), new NavigationSettings
        {
            Html = "<html><head><title>First Window Non Current Title</title></head><body>non-current</body></html>",
        }).ConfigureAwait(false);

        var firstWindowCurrentPage = (WebPage)await firstWindow.OpenPageAsync().ConfigureAwait(false);
        await firstWindowCurrentPage.NavigateAsync(new Uri("https://127.0.0.1/title-lookup/first-window/current"), new NavigationSettings
        {
            Html = "<html><head><title>First Window Current Title</title></head><body>current</body></html>",
        }).ConfigureAwait(false);

        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var secondWindowCurrentPage = (WebPage)secondWindow.CurrentPage;
        await secondWindowCurrentPage.NavigateAsync(new Uri("https://127.0.0.1/title-lookup/second-window/current"), new NavigationSettings
        {
            Html = "<html><head><title>Second Window Current Title</title></head><body>second</body></html>",
        }).ConfigureAwait(false);

        var windowByNonCurrentTitle = await browser.GetWindowAsync("First Window Non Current Title").ConfigureAwait(false);
        var windowByCurrentTitle = await browser.GetWindowAsync("First Window Current Title").ConfigureAwait(false);
        var browserPageByNonCurrentTitle = await browser.GetPageAsync("First Window Non Current Title").ConfigureAwait(false);
        var browserPageByCurrentTitle = await browser.GetPageAsync("First Window Current Title").ConfigureAwait(false);
        var firstWindowPageByNonCurrentTitle = await firstWindow.GetPageAsync("First Window Non Current Title").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(secondWindowCurrentPage));
            Assert.That(windowByNonCurrentTitle, Is.Null);
            Assert.That(windowByCurrentTitle, Is.SameAs(firstWindow));
            Assert.That(browserPageByNonCurrentTitle, Is.SameAs(firstWindowNonCurrentPage));
            Assert.That(browserPageByCurrentTitle, Is.SameAs(firstWindowCurrentPage));
            Assert.That(firstWindowPageByNonCurrentTitle, Is.SameAs(firstWindowNonCurrentPage));
        });
    }

    [Test]
    public async Task UrlLookupFindsNonCurrentTabAcrossWindowsWhileWindowScopeStaysLocal()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());

        var firstWindow = (WebWindow)browser.CurrentWindow;
        var firstWindowTargetPage = (WebPage)firstWindow.CurrentPage;
        var targetUrl = new Uri("https://127.0.0.1/url-lookup/first-window/non-current");
        await firstWindowTargetPage.NavigateAsync(targetUrl, new NavigationSettings
        {
            Html = "<html><head><title>First Window Url Target</title></head><body>target</body></html>",
        }).ConfigureAwait(false);

        var firstWindowCurrentPage = (WebPage)await firstWindow.OpenPageAsync().ConfigureAwait(false);
        await firstWindowCurrentPage.NavigateAsync(new Uri("https://127.0.0.1/url-lookup/first-window/current"), new NavigationSettings
        {
            Html = "<html><head><title>First Window Current Url Page</title></head><body>current</body></html>",
        }).ConfigureAwait(false);

        var secondWindow = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var secondWindowCurrentPage = (WebPage)secondWindow.CurrentPage;
        await secondWindowCurrentPage.NavigateAsync(new Uri("https://127.0.0.1/url-lookup/second-window/current"), new NavigationSettings
        {
            Html = "<html><head><title>Second Window Current Url Page</title></head><body>second</body></html>",
        }).ConfigureAwait(false);

        var browserWindowByUrl = await browser.GetWindowAsync(targetUrl).ConfigureAwait(false);
        var browserPageByUrl = await browser.GetPageAsync(targetUrl).ConfigureAwait(false);
        var firstWindowPageByUrl = await firstWindow.GetPageAsync(targetUrl).ConfigureAwait(false);
        var secondWindowPageByUrl = await secondWindow.GetPageAsync(targetUrl).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(browser.CurrentWindow, Is.SameAs(secondWindow));
            Assert.That(browser.CurrentPage, Is.SameAs(secondWindowCurrentPage));
            Assert.That(firstWindow.CurrentPage, Is.SameAs(firstWindowCurrentPage));
            Assert.That(browserWindowByUrl, Is.SameAs(firstWindow));
            Assert.That(browserPageByUrl, Is.SameAs(firstWindowTargetPage));
            Assert.That(firstWindowPageByUrl, Is.SameAs(firstWindowTargetPage));
            Assert.That(secondWindowPageByUrl, Is.Null);
        });
    }

    [Test]
    public async Task PageDomSurfaceRemainsAlignedWithMainFrameAfterNavigation()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        var frame = (Frame)page.MainFrame;
        var url = new Uri("https://127.0.0.1/page-frame-alignment");
        const string title = "Page Frame Alignment";
        const string html = "<html><head><title>Page Frame Alignment</title></head><body>aligned</body></html>";

        await page.NavigateAsync(url, new NavigationSettings
        {
            Html = html,
        }).ConfigureAwait(false);

        var pageUrl = await page.GetUrlAsync().ConfigureAwait(false);
        var frameUrl = await frame.GetUrlAsync().ConfigureAwait(false);
        var pageTitle = await page.GetTitleAsync().ConfigureAwait(false);
        var frameTitle = await frame.GetTitleAsync().ConfigureAwait(false);
        var pageContent = await page.GetContentAsync().ConfigureAwait(false);
        var frameContent = await frame.GetContentAsync().ConfigureAwait(false);
        var pageEvaluatedTitle = await page.EvaluateAsync<string>("document.title").ConfigureAwait(false);
        var frameEvaluatedTitle = await frame.EvaluateAsync<string>("document.title").ConfigureAwait(false);
        var viewport = await page.GetViewportSizeAsync().ConfigureAwait(false);
        var frameBounds = await frame.GetBoundingBoxAsync().ConfigureAwait(false);
        var pageVisible = await page.IsVisibleAsync().ConfigureAwait(false);
        var frameVisible = await frame.IsVisibleAsync().ConfigureAwait(false);
        var pageScreenshot = await page.GetScreenshotAsync().ConfigureAwait(false);
        var frameScreenshot = await frame.GetScreenshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(pageUrl, Is.EqualTo(url));
            Assert.That(frameUrl, Is.EqualTo(url));
            Assert.That(pageTitle, Is.EqualTo(title));
            Assert.That(frameTitle, Is.EqualTo(title));
            Assert.That(pageContent, Is.EqualTo(html));
            Assert.That(frameContent, Is.EqualTo(html));
            Assert.That(pageEvaluatedTitle, Is.EqualTo(title));
            Assert.That(frameEvaluatedTitle, Is.EqualTo(title));
            Assert.That(pageVisible, Is.EqualTo(frameVisible));
            Assert.That(pageScreenshot.Length, Is.Zero);
            Assert.That(frameScreenshot.Length, Is.Zero);
        });

        if (viewport is Size viewportSize)
        {
            Assert.That(frameBounds, Is.EqualTo(new Rectangle(Point.Empty, viewportSize)));
            Assert.That(pageVisible, Is.EqualTo(!viewportSize.IsEmpty));
        }
        else
        {
            Assert.That(frameBounds, Is.Null);
            Assert.That(pageVisible, Is.False);
        }
    }

    [Test]
    public async Task CookieSurfacePersistsAndClearsAcrossPageWindowAndBrowser()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)window.CurrentPage;
        var cookies = new[]
        {
            new Cookie("session", "abc", "/", LocalCookieDomain),
            new Cookie("preferences", "dark", "/", LocalCookieDomain),
        };

        Assert.That(await page.GetAllCookiesAsync().ConfigureAwait(false), Is.Empty);

        Assert.That(async () => await page.SetCookiesAsync(cookies).ConfigureAwait(false), Throws.Nothing);

        var pageCookiesAfterSet = (await page.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(pageCookiesAfterSet, Has.Length.EqualTo(2));
            Assert.That(pageCookiesAfterSet.Select(static cookie => cookie.Name), Is.EquivalentTo(ExpectedCookieNames));
        });

        Assert.That(async () => await page.ClearAllCookiesAsync().ConfigureAwait(false), Throws.Nothing);
        Assert.That(await page.GetAllCookiesAsync().ConfigureAwait(false), Is.Empty);

        Assert.That(async () => await page.SetCookiesAsync(cookies).ConfigureAwait(false), Throws.Nothing);
        Assert.That(async () => await window.ClearAllCookiesAsync().ConfigureAwait(false), Throws.Nothing);
        Assert.That(await page.GetAllCookiesAsync().ConfigureAwait(false), Is.Empty);

        Assert.That(async () => await page.SetCookiesAsync(cookies).ConfigureAwait(false), Throws.Nothing);
        Assert.That(async () => await browser.ClearAllCookiesAsync().ConfigureAwait(false), Throws.Nothing);
        Assert.That(await page.GetAllCookiesAsync().ConfigureAwait(false), Is.Empty);
    }

    [Test]
    [Repeat(2)]
    public async Task ConcurrentCookieMutationsAndReadsRemainConsistentOnSinglePage()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        const int setterCount = 4;
        const int clearerCount = 4;
        const int readerCount = 8;
        const int iterationsPerMutator = 64;
        const int iterationsPerReader = 128;
        var errors = new ConcurrentQueue<Exception>();
        using var start = new ManualResetEventSlim(false);
        var completedMutators = 0;
        var inconsistentSnapshots = 0;
        var observedSnapshots = 0;

        var setters = Enumerable.Range(0, setterCount)
            .Select(setterIndex => Task.Run(async () =>
            {
                start.Wait();

                for (var iteration = 0; iteration < iterationsPerMutator; iteration++)
                {
                    try
                    {
                        await page.SetCookiesAsync(
                        [
                            new Cookie("session", $"alpha-{setterIndex}", "/", LocalCookieDomain),
                            new Cookie("preferences", $"beta-{setterIndex}", "/", LocalCookieDomain),
                        ]).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }

                Interlocked.Increment(ref completedMutators);
            }))
            .ToArray();

        var clearers = Enumerable.Range(0, clearerCount)
            .Select(_ => Task.Run(async () =>
            {
                start.Wait();

                for (var iteration = 0; iteration < iterationsPerMutator; iteration++)
                {
                    try
                    {
                        await page.ClearAllCookiesAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }

                Interlocked.Increment(ref completedMutators);
            }))
            .ToArray();

        var readers = Enumerable.Range(0, readerCount)
            .Select(_ => Task.Run(async () =>
            {
                start.Wait();

                for (var iteration = 0; iteration < iterationsPerReader; iteration++)
                {
                    try
                    {
                        var snapshot = (await page.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();
                        var names = snapshot.Select(static cookie => cookie.Name).ToArray();

                        if (names.Length != names.Distinct(StringComparer.Ordinal).Count()
                            || names.Any(name => !ExpectedCookieNames.Contains(name, StringComparer.Ordinal)))
                        {
                            Interlocked.Increment(ref inconsistentSnapshots);
                        }

                        Interlocked.Increment(ref observedSnapshots);

                        if (Volatile.Read(ref completedMutators) == setterCount + clearerCount && iteration >= 32)
                        {
                            break;
                        }
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(setters.Concat(clearers).Concat(readers)).ConfigureAwait(false);

        var finalSnapshot = (await page.GetAllCookiesAsync().ConfigureAwait(false)).ToArray();
        var finalNames = finalSnapshot.Select(static cookie => cookie.Name).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, "Concurrent cookie mutations and reads must not throw.");
            Assert.That(observedSnapshots, Is.GreaterThan(0));
            Assert.That(inconsistentSnapshots, Is.Zero, "Cookie snapshots must contain only known names without duplicates.");
            Assert.That(finalNames.Length, Is.EqualTo(finalNames.Distinct(StringComparer.Ordinal).Count()));
            Assert.That(finalNames.All(name => ExpectedCookieNames.Contains(name, StringComparer.Ordinal)), Is.True);
        });
    }

    [Test]
    public async Task ScreenshotSurfaceRemainsEmptyAcrossPageFrameAndElement()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var page = (WebPage)browser.CurrentPage;
        var frame = (Frame)page.MainFrame;
        var element = new Element(page, frame);

        await page.NavigateAsync(new Uri("https://127.0.0.1/stub-screenshot"), new NavigationSettings
        {
            Html = "<html><head><title>Stub Screenshot</title></head><body>stub</body></html>",
        }).ConfigureAwait(false);

        var pageScreenshot = await page.GetScreenshotAsync().ConfigureAwait(false);
        var frameScreenshot = await frame.GetScreenshotAsync().ConfigureAwait(false);
        var elementScreenshot = await element.GetScreenshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(pageScreenshot.Length, Is.Zero);
            Assert.That(frameScreenshot.Length, Is.Zero);
            Assert.That(elementScreenshot.Length, Is.Zero);
        });
    }

    [Test]
    public async Task LifecycleEventSurfaceAfterDisposeRemainsInertAndNonThrowing()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var page = (WebPage)window.CurrentPage;
        var frame = (Frame)page.MainFrame;
        await browser.DisposeAsync().ConfigureAwait(false);

        MutableEventHandler<IWebBrowser, WebLifecycleEventArgs> browserHandler = static (_, _) => { };
        MutableEventHandler<IWebWindow, WebLifecycleEventArgs> windowHandler = static (_, _) => { };
        MutableEventHandler<IWebPage, WebLifecycleEventArgs> pageHandler = static (_, _) => { };
        MutableEventHandler<IFrame, WebLifecycleEventArgs> frameHandler = static (_, _) => { };

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => browser.DomContentLoaded += browserHandler);
            Assert.DoesNotThrow(() => browser.DomContentLoaded -= browserHandler);
            Assert.DoesNotThrow(() => window.NavigationCompleted += windowHandler);
            Assert.DoesNotThrow(() => window.NavigationCompleted -= windowHandler);
            Assert.DoesNotThrow(() => page.PageLoaded += pageHandler);
            Assert.DoesNotThrow(() => page.PageLoaded -= pageHandler);
            Assert.DoesNotThrow(() => frame.DomContentLoaded += frameHandler);
            Assert.DoesNotThrow(() => frame.DomContentLoaded -= frameHandler);
        });
    }

    [Test]
    public async Task CallbackAndInterceptionEventSurfaceAfterDisposeRemainsInertAndNonThrowing()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var page = (WebPage)window.CurrentPage;
        await window.DisposeAsync().ConfigureAwait(false);

        AsyncEventHandler<IWebPage, InterceptedRequestEventArgs> requestHandler = static (_, _) => ValueTask.CompletedTask;
        AsyncEventHandler<IWebPage, InterceptedResponseEventArgs> responseHandler = static (_, _) => ValueTask.CompletedTask;
        AsyncEventHandler<IWebPage, CallbackEventArgs> callbackHandler = static (_, _) => ValueTask.CompletedTask;
        MutableEventHandler<IWebPage, CallbackFinalizedEventArgs> finalizedHandler = static (_, _) => { };

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => page.Request += requestHandler);
            Assert.DoesNotThrow(() => page.Request -= requestHandler);
            Assert.DoesNotThrow(() => page.Response += responseHandler);
            Assert.DoesNotThrow(() => page.Response -= responseHandler);
            Assert.DoesNotThrow(() => page.Callback += callbackHandler);
            Assert.DoesNotThrow(() => page.Callback -= callbackHandler);
            Assert.DoesNotThrow(() => page.CallbackFinalized += finalizedHandler);
            Assert.DoesNotThrow(() => page.CallbackFinalized -= finalizedHandler);
        });
    }

    [Test]
    public async Task PageAndMainFrameAfterWindowDisposeFailFast()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var page = (WebPage)window.CurrentPage;
        var frame = (Frame)page.MainFrame;
        var target = new Uri("https://127.0.0.1/post-dispose-page");
        await window.DisposeAsync().ConfigureAwait(false);

        Assert.That(async () => await page.GetUrlAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await page.GetTitleAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await page.GetContentAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await page.GetAllCookiesAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await page.SetCookiesAsync([new Cookie("session", "abc", "/", LocalCookieDomain)]).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await page.ClearAllCookiesAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await page.NavigateAsync(target).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await page.GetFrameAsync(nameof(IWebPage.MainFrame)).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await frame.GetUrlAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await frame.EvaluateAsync("document.title").ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await frame.WaitForElementAsync("#host", TimeSpan.FromMilliseconds(1)).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await frame.WaitForElementAsync(new WaitForElementSettings
        {
            Selector = ElementSelector.Css("#host"),
        }).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await frame.GetElementAsync("#host").ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await frame.GetElementsAsync(new CssSelector("#host")).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await frame.GetShadowRootAsync("#host").ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await frame.GetChildFramesAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
    }

    [Test]
    public async Task MediaBackendFactoryOverrideRestoresAfterScope()
    {
        using (VirtualCamera.PushBackendFactoryOverride(() => new FakeVirtualCameraBackend(deviceIdentifier: "camera-a")))
        {
            await using var camera = await VirtualCamera.CreateAsync(new VirtualCameraSettings
            {
                Width = 1,
                Height = 1,
            }).ConfigureAwait(false);
            Assert.That(camera.DeviceIdentifier, Is.EqualTo("camera-a"));
        }

        using (VirtualCamera.PushBackendFactoryOverride(() => new FakeVirtualCameraBackend(deviceIdentifier: "camera-b")))
        {
            await using var camera = await VirtualCamera.CreateAsync(new VirtualCameraSettings
            {
                Width = 1,
                Height = 1,
            }).ConfigureAwait(false);
            Assert.That(camera.DeviceIdentifier, Is.EqualTo("camera-b"));
        }

        using (VirtualMicrophone.PushBackendFactoryOverride(() => new FakeVirtualMicrophoneBackend(deviceIdentifier: "microphone-a")))
        {
            await using var microphone = await VirtualMicrophone.CreateAsync(new VirtualMicrophoneSettings()).ConfigureAwait(false);
            Assert.That(microphone.DeviceIdentifier, Is.EqualTo("microphone-a"));
        }

        using (VirtualMicrophone.PushBackendFactoryOverride(() => new FakeVirtualMicrophoneBackend(deviceIdentifier: "microphone-b")))
        {
            await using var microphone = await VirtualMicrophone.CreateAsync(new VirtualMicrophoneSettings()).ConfigureAwait(false);
            Assert.That(microphone.DeviceIdentifier, Is.EqualTo("microphone-b"));
        }
    }

    [Test]
    public void MediaBackendFactoryOverrideRejectsNestedScopes()
    {
        using var cameraOverride = VirtualCamera.PushBackendFactoryOverride(() => new FakeVirtualCameraBackend());
        Assert.That(() => VirtualCamera.PushBackendFactoryOverride(() => new FakeVirtualCameraBackend()), Throws.InstanceOf<InvalidOperationException>());

        using var microphoneOverride = VirtualMicrophone.PushBackendFactoryOverride(() => new FakeVirtualMicrophoneBackend());
        Assert.That(() => VirtualMicrophone.PushBackendFactoryOverride(() => new FakeVirtualMicrophoneBackend()), Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public async Task MediaCreateAsyncDisposesBackendWhenInitializeFails()
    {
        var cameraDisposed = false;
        using (VirtualCamera.PushBackendFactoryOverride(() => new FakeVirtualCameraBackend(
            initializeOverride: static (_, _) => ValueTask.FromException(new InvalidOperationException("camera-init")),
            disposeOverride: () =>
            {
                cameraDisposed = true;
                return ValueTask.CompletedTask;
            })))
        {
            Assert.That(async () => await VirtualCamera.CreateAsync(new VirtualCameraSettings
            {
                Width = 1,
                Height = 1,
            }).ConfigureAwait(false), Throws.InstanceOf<InvalidOperationException>());
        }

        var microphoneDisposed = false;
        using (VirtualMicrophone.PushBackendFactoryOverride(() => new FakeVirtualMicrophoneBackend(
            initializeOverride: static (_, _) => ValueTask.FromException(new InvalidOperationException("microphone-init")),
            disposeOverride: () =>
            {
                microphoneDisposed = true;
                return ValueTask.CompletedTask;
            })))
        {
            Assert.That(async () => await VirtualMicrophone.CreateAsync(new VirtualMicrophoneSettings()).ConfigureAwait(false), Throws.InstanceOf<InvalidOperationException>());
        }

        Assert.Multiple(() =>
        {
            Assert.That(cameraDisposed, Is.True);
            Assert.That(microphoneDisposed, Is.True);
        });
    }

    [Test]
    public async Task BrowserAttachMediaAfterDisposeFailsFast()
    {
        using var cameraOverride = VirtualCamera.PushBackendFactoryOverride(static () => new FakeVirtualCameraBackend());
        using var microphoneOverride = VirtualMicrophone.PushBackendFactoryOverride(static () => new FakeVirtualMicrophoneBackend());
        await using var camera = await VirtualCamera.CreateAsync(new VirtualCameraSettings
        {
            Width = 1,
            Height = 1,
        }).ConfigureAwait(false);
        await using var microphone = await VirtualMicrophone.CreateAsync(new VirtualMicrophoneSettings()).ConfigureAwait(false);
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        await browser.DisposeAsync().ConfigureAwait(false);

        Assert.That(async () => await browser.AttachVirtualCameraAsync(camera).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await browser.AttachVirtualMicrophoneAsync(microphone).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
    }

    [Test]
    public async Task WindowAndPageAttachMediaAfterWindowDisposeFailFast()
    {
        using var cameraOverride = VirtualCamera.PushBackendFactoryOverride(static () => new FakeVirtualCameraBackend());
        using var microphoneOverride = VirtualMicrophone.PushBackendFactoryOverride(static () => new FakeVirtualMicrophoneBackend());
        await using var camera = await VirtualCamera.CreateAsync(new VirtualCameraSettings
        {
            Width = 1,
            Height = 1,
        }).ConfigureAwait(false);
        await using var microphone = await VirtualMicrophone.CreateAsync(new VirtualMicrophoneSettings()).ConfigureAwait(false);
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var page = (WebPage)window.CurrentPage;
        await window.DisposeAsync().ConfigureAwait(false);

        Assert.That(async () => await window.AttachVirtualCameraAsync(camera).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await window.AttachVirtualMicrophoneAsync(microphone).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await page.AttachVirtualCameraAsync(camera).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await page.AttachVirtualMicrophoneAsync(microphone).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
    }

    [Test]
    public async Task ElementAndShadowRootAfterWindowDisposeFailFast()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        var page = (WebPage)window.CurrentPage;
        var element = new Element(page);
        var shadowRoot = new ShadowRoot(element, page);
        await window.DisposeAsync().ConfigureAwait(false);

        Assert.That(async () => await element.ClickAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await element.GetInnerTextAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await element.EvaluateAsync("document.title").ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await element.GetFrameAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await element.ClickAsync(new ClickSettings()).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await element.SetValueAsync("x").ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await element.GetChildElementsAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await element.GetChildFramesAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await element.GetPropertyAsync("id").ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await element.IsContentEditableAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await element.IsDraggableAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await element.IsEditableAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await element.IsSelectedAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await element.GetParentFrameAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await element.IsAnimatingAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await element.GetDropTargetAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await shadowRoot.GetUrlAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await shadowRoot.GetContentAsync().ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await shadowRoot.EvaluateAsync("document.title").ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await shadowRoot.GetElementAsync("#host").ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await shadowRoot.GetElementAsync(new CssSelector("#host")).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await shadowRoot.GetElementsAsync(ElementSelector.Css("#host")).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await shadowRoot.WaitForElementAsync("#host", TimeSpan.FromMilliseconds(1)).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await shadowRoot.WaitForElementAsync(new WaitForElementSettings
        {
            Selector = ElementSelector.Css("#host"),
        }).ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(async () => await shadowRoot.GetShadowRootAsync("#host").ConfigureAwait(false), Throws.InstanceOf<ObjectDisposedException>());
    }

    [Test]
    public async Task IsDisposedReflectsOwnerLifecycleAcrossBrowserDomAndElementContracts()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)window.CurrentPage;
        var frame = (Frame)page.MainFrame;
        var element = new Element(page);
        var shadowRoot = new ShadowRoot(element, page);

        Assert.Multiple(() =>
        {
            Assert.That(((IWebBrowser)browser).IsDisposed, Is.False);
            Assert.That(((IWebWindow)window).IsDisposed, Is.False);
            Assert.That(((IWebPage)page).IsDisposed, Is.False);
            Assert.That(((IDomContext)frame).IsDisposed, Is.False);
            Assert.That(((IElement)element).IsDisposed, Is.False);
            Assert.That(((IShadowRoot)shadowRoot).IsDisposed, Is.False);
        });

        await window.DisposeAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(((IWebBrowser)browser).IsDisposed, Is.False);
            Assert.That(((IWebWindow)window).IsDisposed, Is.True);
            Assert.That(((IWebPage)page).IsDisposed, Is.True);
            Assert.That(((IDomContext)frame).IsDisposed, Is.True);
            Assert.That(((IElement)element).IsDisposed, Is.True);
            Assert.That(((IShadowRoot)shadowRoot).IsDisposed, Is.True);
        });

        await browser.DisposeAsync().ConfigureAwait(false);

        Assert.That(((IWebBrowser)browser).IsDisposed, Is.True);
    }

    [Test]
    [Repeat(2)]
    public async Task ConcurrentIsDisposedReadsStayNonThrowingAndObserveDisposedState()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)browser.CurrentWindow;
        var page = (WebPage)window.CurrentPage;
        var frame = (Frame)page.MainFrame;
        var element = new Element(page);
        var shadowRoot = new ShadowRoot(element, page);
        var errors = new ConcurrentQueue<Exception>();
        using var start = new ManualResetEventSlim(false);
        var disposeCompleted = 0;
        var browserDisposedReads = 0;
        var windowDisposedReads = 0;
        var pageDisposedReads = 0;
        var frameDisposedReads = 0;
        var elementDisposedReads = 0;
        var shadowRootDisposedReads = 0;

        var readers = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() =>
            {
                try
                {
                    start.Wait();
                    var postDisposeReads = 0;

                    while (Volatile.Read(ref disposeCompleted) == 0 || postDisposeReads < 64)
                    {
                        if (((IWebBrowser)browser).IsDisposed)
                            Interlocked.Increment(ref browserDisposedReads);

                        if (((IWebWindow)window).IsDisposed)
                            Interlocked.Increment(ref windowDisposedReads);

                        if (((IWebPage)page).IsDisposed)
                            Interlocked.Increment(ref pageDisposedReads);

                        if (((IDomContext)frame).IsDisposed)
                            Interlocked.Increment(ref frameDisposedReads);

                        if (((IElement)element).IsDisposed)
                            Interlocked.Increment(ref elementDisposedReads);

                        if (((IShadowRoot)shadowRoot).IsDisposed)
                            Interlocked.Increment(ref shadowRootDisposedReads);

                        if (Volatile.Read(ref disposeCompleted) != 0)
                            postDisposeReads++;

                        Thread.Yield();
                    }
                }
                catch (Exception exception)
                {
                    errors.Enqueue(exception);
                }
            }))
            .ToArray();

        var disposer = Task.Run(async () =>
        {
            start.Wait();
            await browser.DisposeAsync().ConfigureAwait(false);
            Volatile.Write(ref disposeCompleted, 1);
        });

        start.Set();
        await Task.WhenAll(readers.Append(disposer)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty);
            Assert.That(browserDisposedReads, Is.GreaterThan(0));
            Assert.That(windowDisposedReads, Is.GreaterThan(0));
            Assert.That(pageDisposedReads, Is.GreaterThan(0));
            Assert.That(frameDisposedReads, Is.GreaterThan(0));
            Assert.That(elementDisposedReads, Is.GreaterThan(0));
            Assert.That(shadowRootDisposedReads, Is.GreaterThan(0));
        });
    }

    [Test]
    [Repeat(2)]
    public async Task ConcurrentBrowserDisposeAndLookupEitherResolveConsistentlyOrFailClosed()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        const int windowCount = 8;
        const int pagesPerWindow = 3;
        const int readerCount = 8;
        const int iterationsPerReader = 256;
        var targets = new ConcurrentBag<LookupTarget>();
        var errors = new ConcurrentQueue<Exception>();
        var consistentResolutions = 0;
        var disposedSignals = 0;
        using var start = new ManualResetEventSlim(false);

        for (var windowIndex = 0; windowIndex < windowCount; windowIndex++)
        {
            var window = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
            for (var pageIndex = 0; pageIndex < pagesPerWindow; pageIndex++)
            {
                var page = pageIndex == 0
                    ? (WebPage)window.CurrentPage
                    : (WebPage)await window.OpenPageAsync().ConfigureAwait(false);
                var sequence = (windowIndex * pagesPerWindow) + pageIndex;
                var target = new LookupTarget($"Dispose Lookup {sequence}", new Uri($"https://127.0.0.1/dispose-lookup/{sequence}"));
                await page.NavigateAsync(target.Url, new NavigationSettings
                {
                    Html = $"<html><head><title>{target.Title}</title></head><body>{sequence}</body></html>",
                }).ConfigureAwait(false);
                targets.Add(target);
            }
        }

        var readers = Enumerable.Range(0, readerCount)
            .Select(_ => Task.Run(async () =>
            {
                start.Wait();

                for (var iteration = 0; iteration < iterationsPerReader; iteration++)
                {
                    try
                    {
                        var snapshot = targets.ToArray();
                        var target = snapshot[Random.Shared.Next(snapshot.Length)];
                        var windowByUrl = await browser.GetWindowAsync(target.Url).ConfigureAwait(false);
                        if (windowByUrl is not null)
                        {
                            var pageInWindow = await windowByUrl.GetPageAsync(target.Url).ConfigureAwait(false);
                            if (pageInWindow is not null)
                            {
                                Assert.That(await pageInWindow.GetUrlAsync().ConfigureAwait(false), Is.EqualTo(target.Url));
                                Interlocked.Increment(ref consistentResolutions);
                            }
                        }

                        var pageByUrl = await browser.GetPageAsync(target.Url).ConfigureAwait(false);
                        if (pageByUrl is not null)
                        {
                            Assert.That(await pageByUrl.GetUrlAsync().ConfigureAwait(false), Is.EqualTo(target.Url));
                            Interlocked.Increment(ref consistentResolutions);
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        Interlocked.Increment(ref disposedSignals);
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }
            }))
            .ToArray();

        var disposer = Task.Run(async () =>
        {
            start.Wait();
            await Task.Delay(25).ConfigureAwait(false);

            try
            {
                await browser.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                errors.Enqueue(exception);
            }
        });

        start.Set();
        await Task.WhenAll(readers.Append(disposer)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, "Mixed browser dispose and lookup stress must not produce inconsistent matches or unexpected exceptions.");
            Assert.That(consistentResolutions, Is.GreaterThan(0));
            Assert.That(consistentResolutions + disposedSignals, Is.GreaterThan(0));
        });
    }

    [Test]
    [Repeat(2)]
    public async Task ConcurrentWindowDisposeAndLookupEitherResolveConsistentlyOrFailClosed()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var window = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
        const int pageCount = 8;
        const int readerCount = 8;
        const int iterationsPerReader = 256;
        var targets = new ConcurrentBag<LookupTarget>();
        var errors = new ConcurrentQueue<Exception>();
        var consistentResolutions = 0;
        var disposedSignals = 0;
        using var start = new ManualResetEventSlim(false);

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var page = pageIndex == 0
                ? (WebPage)window.CurrentPage
                : (WebPage)await window.OpenPageAsync().ConfigureAwait(false);
            var target = new LookupTarget($"Window Dispose Lookup {pageIndex}", new Uri($"https://127.0.0.1/window-dispose-lookup/{pageIndex}"));
            await page.NavigateAsync(target.Url, new NavigationSettings
            {
                Html = $"<html><head><title>{target.Title}</title></head><body>{pageIndex}</body></html>",
            }).ConfigureAwait(false);
            targets.Add(target);
        }

        var readers = Enumerable.Range(0, readerCount)
            .Select(_ => Task.Run(async () =>
            {
                start.Wait();

                for (var iteration = 0; iteration < iterationsPerReader; iteration++)
                {
                    try
                    {
                        var snapshot = targets.ToArray();
                        var target = snapshot[Random.Shared.Next(snapshot.Length)];
                        var pageByUrl = await window.GetPageAsync(target.Url).ConfigureAwait(false);
                        if (pageByUrl is not null)
                        {
                            Assert.That(await pageByUrl.GetUrlAsync().ConfigureAwait(false), Is.EqualTo(target.Url));
                            Interlocked.Increment(ref consistentResolutions);
                        }

                        await window.GetUrlAsync().ConfigureAwait(false);
                        await window.GetTitleAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        Interlocked.Increment(ref disposedSignals);
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }
            }))
            .ToArray();

        var disposer = Task.Run(async () =>
        {
            start.Wait();
            await Task.Delay(25).ConfigureAwait(false);

            try
            {
                await window.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                errors.Enqueue(exception);
            }
        });

        start.Set();
        await Task.WhenAll(readers.Append(disposer)).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, "Mixed window dispose and page lookup stress must not produce inconsistent matches or unexpected exceptions.");
            Assert.That(consistentResolutions, Is.GreaterThan(0));
            Assert.That(consistentResolutions + disposedSignals, Is.GreaterThan(0));
        });
    }

    [Test]
    [Repeat(2)]
    public async Task CombinedRuntimeChaosStressKeepsBrowserGraphConsistent()
    {
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings());
        var initialWindow = (WebWindow)browser.CurrentWindow;
        var initialPage = (WebPage)browser.CurrentPage;
        var windows = new ConcurrentBag<WebWindow>([initialWindow]);
        var pages = new ConcurrentBag<WebPage>([initialPage]);
        var targets = new ConcurrentBag<LookupTarget>();
        var errors = new ConcurrentQueue<Exception>();
        var resolvedLookups = 0;
        var observedReads = 0;
        using var start = new ManualResetEventSlim(false);
        var completedActors = 0;
        const int actorGroups = 4;

        Task[] actors =
        [
            .. Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                start.Wait();

                for (var index = 0; index < 16; index++)
                {
                    try
                    {
                        var window = (WebWindow)await browser.OpenWindowAsync().ConfigureAwait(false);
                        windows.Add(window);
                        pages.Add((WebPage)window.CurrentPage);
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }

                Interlocked.Increment(ref completedActors);
            })),

            .. Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                start.Wait();

                for (var index = 0; index < 16; index++)
                {
                    try
                    {
                        var snapshot = windows.ToArray();
                        var window = snapshot.Length == 0 ? initialWindow : snapshot[Random.Shared.Next(snapshot.Length)];
                        var page = (WebPage)await window.OpenPageAsync().ConfigureAwait(false);
                        pages.Add(page);
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }

                Interlocked.Increment(ref completedActors);
            })),

            .. Enumerable.Range(0, 8).Select(actorIndex => Task.Run(async () =>
            {
                start.Wait();

                for (var navigationIndex = 0; navigationIndex < 24; navigationIndex++)
                {
                    try
                    {
                        var snapshot = pages.ToArray();
                        var page = snapshot.Length == 0 ? initialPage : snapshot[Random.Shared.Next(snapshot.Length)];
                        var sequence = (actorIndex * 24) + navigationIndex;
                        var target = new LookupTarget($"Chaos {sequence}", new Uri($"https://127.0.0.1/chaos/{sequence}"));
                        await page.NavigateAsync(target.Url, new NavigationSettings
                        {
                            Html = $"<html><head><title>{target.Title}</title></head><body>{sequence}</body></html>",
                        }).ConfigureAwait(false);
                        targets.Add(target);
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }

                Interlocked.Increment(ref completedActors);
            })),

            .. Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
            {
                start.Wait();

                while (Volatile.Read(ref completedActors) < actorGroups * 4 || Volatile.Read(ref observedReads) == 0)
                {
                    try
                    {
                        var pageSnapshot = pages.ToArray();
                        var page = pageSnapshot.Length == 0 ? initialPage : pageSnapshot[Random.Shared.Next(pageSnapshot.Length)];
                        var frame = page.MainFrame;
                        var element = new Element(page);
                        await page.GetUrlAsync().ConfigureAwait(false);
                        await page.GetTitleAsync().ConfigureAwait(false);
                        await frame.GetUrlAsync().ConfigureAwait(false);
                        await frame.GetTitleAsync().ConfigureAwait(false);
                        await element.EvaluateAsync<string>("document.title").ConfigureAwait(false);
                        Interlocked.Increment(ref observedReads);

                        var targetSnapshot = targets.ToArray();
                        if (targetSnapshot.Length > 0)
                        {
                            var target = targetSnapshot[Random.Shared.Next(targetSnapshot.Length)];
                            var resolved = await ValidateLookupAsync(browser, target, requireResolution: false).ConfigureAwait(false);
                            Interlocked.Add(ref resolvedLookups, resolved);
                        }
                    }
                    catch (Exception exception)
                    {
                        errors.Enqueue(exception);
                    }
                }
            }))
        ];

        start.Set();
        await Task.WhenAll(actors).ConfigureAwait(false);

        var finalTargets = new List<(LookupTarget Target, bool RequireWindowResolution)>();
        foreach (var page in pages.Distinct())
        {
            try
            {
                var title = await page.GetTitleAsync().ConfigureAwait(false);
                var url = await page.GetUrlAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(title) && url is not null)
                {
                    finalTargets.Add((new LookupTarget(title, url), ReferenceEquals(page.Window.CurrentPage, page)));
                }
            }
            catch (Exception exception)
            {
                errors.Enqueue(exception);
            }
        }

        foreach (var finalTarget in finalTargets)
        {
            try
            {
                var resolved = await ValidateLookupAsync(
                    browser,
                    finalTarget.Target,
                    requireResolution: true,
                    requireWindowResolution: finalTarget.RequireWindowResolution).ConfigureAwait(false);
                Interlocked.Add(ref resolvedLookups, resolved);
            }
            catch (Exception exception)
            {
                errors.Enqueue(exception);
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty, "Combined runtime chaos stress must not throw or resolve inconsistent graph state.");
            Assert.That(windows.Count, Is.GreaterThan(1));
            Assert.That(pages.Count, Is.GreaterThan(1));
            Assert.That(targets.Count, Is.GreaterThan(0));
            Assert.That(finalTargets.Count, Is.GreaterThan(0));
            Assert.That(observedReads, Is.GreaterThan(0));
            Assert.That(resolvedLookups, Is.GreaterThan(0));
        });
    }

    private static async Task<IReadOnlyList<BridgeMessage>> DrainMessagesConcurrentlyAsync(TryDequeueBridgeMessage tryDequeue, int expectedCount, int consumerCount = 16)
    {
        ArgumentNullException.ThrowIfNull(tryDequeue);

        var messages = new ConcurrentBag<BridgeMessage>();
        var errors = new ConcurrentQueue<Exception>();
        using var start = new ManualResetEventSlim(false);
        var observedCount = 0;
        var tasks = Enumerable.Range(0, consumerCount)
            .Select(_ => Task.Run(() =>
            {
                try
                {
                    start.Wait();
                    var emptyReads = 0;

                    while (Volatile.Read(ref observedCount) < expectedCount && emptyReads < 4096)
                    {
                        if (tryDequeue(out var message))
                        {
                            emptyReads = 0;
                            if (message is not null)
                            {
                                messages.Add(message);
                            }

                            Interlocked.Increment(ref observedCount);
                            continue;
                        }

                        emptyReads++;
                        Thread.Yield();
                    }
                }
                catch (Exception exception)
                {
                    errors.Enqueue(exception);
                }
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        Assert.That(errors, Is.Empty, "Concurrent drain must not throw.");
        return messages.ToArray();
    }

    private static void AssertNavigationEnvelope(IReadOnlyList<BridgeMessage> messages, int expectedNavigationCount, string expectedWindowId, string expectedTabId)
    {
        var expectedCount = expectedNavigationCount * BridgeEventsPerNavigation;
        Assert.Multiple(() =>
        {
            Assert.That(messages, Has.Count.EqualTo(expectedCount));
            Assert.That(messages.Select(static message => message.Id).Distinct().Count(), Is.EqualTo(expectedCount));
            Assert.That(messages.All(static message => message.Type == BridgeMessageType.Event), Is.True);
            Assert.That(messages.All(message => string.Equals(message.WindowId, expectedWindowId, StringComparison.Ordinal)), Is.True);
            Assert.That(messages.All(message => string.Equals(message.TabId, expectedTabId, StringComparison.Ordinal)), Is.True);
            Assert.That(messages.Count(message => message.Event == BridgeEvent.RequestIntercepted), Is.EqualTo(expectedNavigationCount));
            Assert.That(messages.Count(message => message.Event == BridgeEvent.ResponseReceived), Is.EqualTo(expectedNavigationCount));
            Assert.That(messages.Count(message => message.Event == BridgeEvent.DomContentLoaded), Is.EqualTo(expectedNavigationCount));
            Assert.That(messages.Count(message => message.Event == BridgeEvent.NavigationCompleted), Is.EqualTo(expectedNavigationCount));
            Assert.That(messages.Count(message => message.Event == BridgeEvent.PageLoaded), Is.EqualTo(expectedNavigationCount));
        });
    }

    private static void SubscribeLifecycle(IWebPage page, Action handler)
    {
        page.DomContentLoaded += (_, _) => handler();
        page.NavigationCompleted += (_, _) => handler();
        page.PageLoaded += (_, _) => handler();
    }

    private static void SubscribeLifecycle(IWebWindow window, Action handler)
    {
        window.DomContentLoaded += (_, _) => handler();
        window.NavigationCompleted += (_, _) => handler();
        window.PageLoaded += (_, _) => handler();
    }

    private static void SubscribeLifecycle(IWebBrowser browser, Action handler)
    {
        browser.DomContentLoaded += (_, _) => handler();
        browser.NavigationCompleted += (_, _) => handler();
        browser.PageLoaded += (_, _) => handler();
    }

    private static void SubscribeLifecycle(IFrame frame, Action handler)
    {
        frame.DomContentLoaded += (_, _) => handler();
        frame.NavigationCompleted += (_, _) => handler();
        frame.PageLoaded += (_, _) => handler();
    }

    private static async Task<int> ValidateLookupAsync(RuntimeWebBrowser browser, LookupTarget target, bool requireResolution, bool? requireWindowResolution = null)
    {
        var mustResolveWindow = requireWindowResolution ?? requireResolution;
        var resolved = 0;
        var windowByTitle = await browser.GetWindowAsync(target.Title).ConfigureAwait(false);
        var windowByUrl = await browser.GetWindowAsync(target.Url).ConfigureAwait(false);
        var pageByTitle = await browser.GetPageAsync(target.Title).ConfigureAwait(false);
        var pageByUrl = await browser.GetPageAsync(target.Url).ConfigureAwait(false);

        if (requireResolution)
        {
            Assert.That(pageByTitle, Is.Not.Null);
            Assert.That(pageByUrl, Is.Not.Null);
        }

        if (mustResolveWindow)
        {
            Assert.That(windowByTitle, Is.Not.Null);
            Assert.That(windowByUrl, Is.Not.Null);
        }

        if (windowByTitle is not null)
        {
            if (requireResolution)
            {
                Assert.That(await windowByTitle.GetTitleAsync().ConfigureAwait(false), Is.EqualTo(target.Title));
            }

            resolved++;
        }

        if (windowByUrl is not null)
        {
            if (requireResolution)
            {
                var pageInWindowByUrl = await windowByUrl.GetPageAsync(target.Url).ConfigureAwait(false);
                Assert.That(pageInWindowByUrl, Is.Not.Null);
                Assert.That(await pageInWindowByUrl!.GetUrlAsync().ConfigureAwait(false), Is.EqualTo(target.Url));
            }

            resolved++;
        }

        if (pageByTitle is not null)
        {
            if (requireResolution)
            {
                Assert.That(await pageByTitle.GetTitleAsync().ConfigureAwait(false), Is.EqualTo(target.Title));
                var frameByName = await pageByTitle.GetFrameAsync(nameof(IWebPage.MainFrame)).ConfigureAwait(false);
                Assert.That(frameByName, Is.Not.Null);
            }

            resolved++;
        }

        if (pageByUrl is not null)
        {
            if (requireResolution)
            {
                Assert.That(await pageByUrl.GetUrlAsync().ConfigureAwait(false), Is.EqualTo(target.Url));
                var frameByUrl = await pageByUrl.GetFrameAsync(target.Url).ConfigureAwait(false);
                Assert.That(frameByUrl, Is.Not.Null);
                Assert.That(await frameByUrl!.GetUrlAsync().ConfigureAwait(false), Is.EqualTo(target.Url));
            }

            resolved++;
        }

        return resolved;
    }

    private sealed class FakeVirtualCameraBackend : IVirtualCameraBackend
    {
        private readonly Func<VirtualCameraSettings, CancellationToken, ValueTask>? initializeOverride;
        private readonly Func<ValueTask>? disposeOverride;

        public FakeVirtualCameraBackend(
            string deviceIdentifier = "fake-camera",
            Func<VirtualCameraSettings, CancellationToken, ValueTask>? initializeOverride = null,
            Func<ValueTask>? disposeOverride = null)
        {
            DeviceIdentifier = deviceIdentifier;
            this.initializeOverride = initializeOverride;
            this.disposeOverride = disposeOverride;
        }

        public string DeviceIdentifier { get; }
        public bool IsCapturing { get; private set; }

        public event EventHandler<CameraControlChangedEventArgs>? ControlChanged;

        public ValueTask InitializeAsync(VirtualCameraSettings settings, CancellationToken cancellationToken)
        {
            if (initializeOverride is not null)
            {
                return initializeOverride(settings, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask StartCaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsCapturing = true;
            return ValueTask.CompletedTask;
        }

        public void WriteFrame(ReadOnlySpan<byte> frameData)
        {
        }

        public ValueTask StopCaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsCapturing = false;
            return ValueTask.CompletedTask;
        }

        public void SetControl(CameraControlType control, float value)
        {
            ControlChanged?.Invoke(this, new CameraControlChangedEventArgs
            {
                Control = control,
                Value = value,
            });
        }

        public float GetControl(CameraControlType control) => 0.0f;

        public CameraControlRange? GetControlRange(CameraControlType control) => null;

        public ValueTask DisposeAsync() => disposeOverride is null ? ValueTask.CompletedTask : disposeOverride();
    }

    private sealed class FakeVirtualMicrophoneBackend : IVirtualMicrophoneBackend
    {
        private readonly Func<VirtualMicrophoneSettings, CancellationToken, ValueTask>? initializeOverride;
        private readonly Func<ValueTask>? disposeOverride;

        public FakeVirtualMicrophoneBackend(
            string deviceIdentifier = "fake-microphone",
            Func<VirtualMicrophoneSettings, CancellationToken, ValueTask>? initializeOverride = null,
            Func<ValueTask>? disposeOverride = null)
        {
            DeviceIdentifier = deviceIdentifier;
            this.initializeOverride = initializeOverride;
            this.disposeOverride = disposeOverride;
        }

        public string DeviceIdentifier { get; }
        public bool IsCapturing { get; private set; }

        public event EventHandler<MicrophoneControlChangedEventArgs>? ControlChanged;

        public ValueTask InitializeAsync(VirtualMicrophoneSettings settings, CancellationToken cancellationToken)
        {
            if (initializeOverride is not null)
            {
                return initializeOverride(settings, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask StartCaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsCapturing = true;
            return ValueTask.CompletedTask;
        }

        public void WriteSamples(ReadOnlySpan<byte> sampleData)
        {
        }

        public ValueTask StopCaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsCapturing = false;
            return ValueTask.CompletedTask;
        }

        public void SetControl(MicrophoneControlType control, float value)
        {
            ControlChanged?.Invoke(this, new MicrophoneControlChangedEventArgs
            {
                Control = control,
                Value = value,
            });
        }

        public float GetControl(MicrophoneControlType control) => 0.0f;

        public MicrophoneControlRange? GetControlRange(MicrophoneControlType control) => null;

        public ValueTask DisposeAsync() => disposeOverride is null ? ValueTask.CompletedTask : disposeOverride();
    }
}