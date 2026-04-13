using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Atom.Net.Browsing.WebDriver;

[SupportedOSPlatform("linux")]
internal static partial class LinuxX11WindowDiscovery
{
    internal static Rectangle? TryGetTopLevelWindowBounds(string displayName, int processId, Size? expectedSize = null, string? windowTitle = null)
        => ResolveTopLevelWindow(displayName, processId, expectedSize, windowTitle).Bounds;

    internal static Resolution ResolveTopLevelWindow(string displayName, int processId, Size? expectedSize = null, string? windowTitle = null)
    {
        if (!OperatingSystem.IsLinux())
            return new(Bounds: null, Diagnostics: "strategy=unsupported-os");

        if (string.IsNullOrWhiteSpace(displayName) || processId <= 0)
            return CreateInvalidInputResolution(displayName, processId);

        var display = XOpenDisplay(displayName);
        if (display == nint.Zero)
            return CreateOpenDisplayFailureResolution(displayName, processId);

        try
        {
            return ResolveTopLevelWindow(display, displayName, processId, expectedSize, windowTitle);
        }
        finally
        {
            _ = XCloseDisplay(display);
        }
    }

    private static Resolution CreateInvalidInputResolution(string displayName, int processId)
        => new(
            Bounds: null,
            Diagnostics: string.Concat(
                "strategy=invalid-input;display=",
                Sanitize(displayName),
                ";processId=",
                Invariant(processId)));

    private static Resolution CreateOpenDisplayFailureResolution(string displayName, int processId)
        => new(
            Bounds: null,
            Diagnostics: string.Concat(
                "strategy=open-display-failed;display=",
                Sanitize(displayName),
                ";processId=",
                Invariant(processId)));

    private static Resolution ResolveTopLevelWindow(nint display, string displayName, int processId, Size? expectedSize, string? windowTitle)
    {
        var screenNumber = XDefaultScreen(display);
        var rootWindow = XRootWindow(display, screenNumber);
        var pidAtom = XInternAtom(display, "_NET_WM_PID", onlyIfExists: true);
        var cardinalAtom = XInternAtom(display, "CARDINAL", onlyIfExists: true);
        var netWmNameAtom = XInternAtom(display, "_NET_WM_NAME", onlyIfExists: true);
        var utf8StringAtom = XInternAtom(display, "UTF8_STRING", onlyIfExists: true);
        var candidates = QueryTopLevelCandidates(
            display,
            rootWindow,
            pidAtom,
            cardinalAtom,
            netWmNameAtom,
            utf8StringAtom,
            processId,
            expectedSize,
            windowTitle);
        var selectedCandidate = SelectTopLevelCandidate(candidates, expectedSize, windowTitle, out var strategy);

        return new(
            Bounds: selectedCandidate?.Bounds,
            Diagnostics: BuildDiagnostics(
                displayName,
                processId,
                expectedSize,
                windowTitle,
                pidAtom != nint.Zero && cardinalAtom != nint.Zero,
                netWmNameAtom != nint.Zero && utf8StringAtom != nint.Zero,
                candidates,
                strategy,
                selectedCandidate));
    }

    private static TopLevelWindowCandidate[] QueryTopLevelCandidates(
        nint display,
        nint rootWindow,
        nint pidAtom,
        nint cardinalAtom,
        nint netWmNameAtom,
        nint utf8StringAtom,
        int processId,
        Size? expectedSize,
        string? windowTitle)
    {
        if (!TryQueryChildren(display, rootWindow, out var childrenPointer, out var childCount))
            return [];

        try
        {
            var candidates = new TopLevelWindowCandidate[checked((int)childCount)];

            for (nuint index = 0; index < childCount; index++)
            {
                var childWindow = Marshal.ReadIntPtr(childrenPointer, checked((int)(index * (nuint)IntPtr.Size)));
                if (childWindow == nint.Zero)
                    continue;
                candidates[index] = CreateTopLevelWindowCandidate(
                    display,
                    childWindow,
                    pidAtom,
                    cardinalAtom,
                    netWmNameAtom,
                    utf8StringAtom,
                    processId,
                    expectedSize,
                    windowTitle);
            }

            return candidates;
        }
        finally
        {
            if (childrenPointer != nint.Zero)
                _ = XFree(childrenPointer);
        }
    }

    private static TopLevelWindowCandidate CreateTopLevelWindowCandidate(
        nint display,
        nint childWindow,
        nint pidAtom,
        nint cardinalAtom,
        nint netWmNameAtom,
        nint utf8StringAtom,
        int processId,
        Size? expectedSize,
        string? windowTitle)
    {
        var bounds = TryGetWindowGeometry(display, childWindow, out var geometry)
            ? geometry
            : (Rectangle?)null;
        var directProcessId = TryGetDirectWindowProcessId(display, childWindow, pidAtom, cardinalAtom);
        var directTitle = TryGetDirectWindowTitle(display, childWindow, netWmNameAtom, utf8StringAtom);
        var treeContainsProcess = pidAtom != nint.Zero
            && cardinalAtom != nint.Zero
            && WindowTreeContainsProcess(display, childWindow, pidAtom, cardinalAtom, processId);
        var treeContainsTitle = !string.IsNullOrWhiteSpace(windowTitle)
            && WindowTreeContainsTitle(display, childWindow, netWmNameAtom, utf8StringAtom, windowTitle);
        var sizeScore = ComputeSizeScore(bounds, expectedSize);

        return new(
            childWindow,
            bounds,
            directProcessId,
            treeContainsProcess,
            directTitle,
            treeContainsTitle,
            sizeScore);
    }

    private static int? TryGetDirectWindowProcessId(nint display, nint window, nint pidAtom, nint cardinalAtom)
        => pidAtom != nint.Zero
            && cardinalAtom != nint.Zero
            && TryGetWindowProcessId(display, window, pidAtom, cardinalAtom, out var currentProcessId)
                ? currentProcessId
                : null;

    private static string? TryGetDirectWindowTitle(nint display, nint window, nint netWmNameAtom, nint utf8StringAtom)
        => TryGetWindowTitle(display, window, netWmNameAtom, utf8StringAtom, out var title)
            ? title
            : null;

    private static long? ComputeSizeScore(Rectangle? bounds, Size? expectedSize)
    {
        if (bounds is not Rectangle candidateBounds || expectedSize is not { Width: > 0, Height: > 0 })
            return null;

        return Math.Abs(candidateBounds.Width - expectedSize.Value.Width)
            + Math.Abs(candidateBounds.Height - expectedSize.Value.Height);
    }

    private static bool TrySelectPidAndTitleCandidate(TopLevelWindowCandidate[] candidates, out TopLevelWindowCandidate selectedCandidate)
    {
        return TrySelectLargestAreaCandidate(
            candidates,
            static candidate => candidate.Bounds is not null && candidate.TreeContainsProcess && candidate.TreeContainsTitle,
            out selectedCandidate);
    }

    private static TopLevelWindowCandidate? SelectTopLevelCandidate(TopLevelWindowCandidate[] candidates, Size? expectedSize, string? windowTitle, out string strategy)
    {
        strategy = "none";

        if (!string.IsNullOrWhiteSpace(windowTitle)
            && TrySelectPidAndTitleCandidate(candidates, out var pidAndTitleCandidate))
        {
            strategy = "pid+title";
            return pidAndTitleCandidate;
        }

        if (TrySelectPidCandidate(candidates, out var pidCandidate))
        {
            strategy = "pid";
            return pidCandidate;
        }

        if (!string.IsNullOrWhiteSpace(windowTitle)
            && TrySelectTitleCandidate(candidates, out var titleCandidate))
        {
            strategy = "title";
            return titleCandidate;
        }

        if (expectedSize is { Width: > 0, Height: > 0 }
            && TrySelectSizeCandidate(candidates, out var sizeCandidate))
        {
            strategy = "size";
            return sizeCandidate;
        }

        return null;
    }

    private static bool TrySelectPidCandidate(TopLevelWindowCandidate[] candidates, out TopLevelWindowCandidate selectedCandidate)
        => TrySelectLargestAreaCandidate(
            candidates,
            static candidate => candidate.Bounds is not null && candidate.TreeContainsProcess,
            out selectedCandidate);

    private static bool TrySelectTitleCandidate(TopLevelWindowCandidate[] candidates, out TopLevelWindowCandidate selectedCandidate)
        => TrySelectLargestAreaCandidate(
            candidates,
            static candidate => candidate.Bounds is not null && candidate.TreeContainsTitle,
            out selectedCandidate);

    private static bool TrySelectLargestAreaCandidate(TopLevelWindowCandidate[] candidates, Predicate<TopLevelWindowCandidate> predicate, out TopLevelWindowCandidate selectedCandidate)
    {
        selectedCandidate = default;
        var found = false;
        var bestArea = -1L;

        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            if (!predicate(candidate))
                continue;

            var area = candidate.Area;
            if (found && area <= bestArea)
                continue;

            selectedCandidate = candidate;
            bestArea = area;
            found = true;
        }

        return found;
    }

    private static bool TrySelectSizeCandidate(TopLevelWindowCandidate[] candidates, out TopLevelWindowCandidate selectedCandidate)
    {
        selectedCandidate = default;
        var found = false;
        var bestScore = long.MaxValue;
        var bestHasProcess = false;
        var bestHasTitle = false;
        var bestArea = -1L;

        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            if (candidate.Bounds is null || candidate.SizeScore is not long score)
                continue;

            if (!IsBetterSizeCandidate(candidate, found, bestScore, bestHasProcess, bestHasTitle, bestArea, score))
                continue;

            selectedCandidate = candidate;
            bestScore = score;
            bestHasProcess = candidate.TreeContainsProcess;
            bestHasTitle = candidate.TreeContainsTitle;
            bestArea = candidate.Area;
            found = true;
        }

        return found;
    }

    private static bool IsBetterSizeCandidate(
        TopLevelWindowCandidate candidate,
        bool found,
        long bestScore,
        bool bestHasProcess,
        bool bestHasTitle,
        long bestArea,
        long score)
    {
        return !found
            || score < bestScore
            || (score == bestScore && candidate.TreeContainsProcess && !bestHasProcess)
            || (score == bestScore && candidate.TreeContainsProcess == bestHasProcess && candidate.TreeContainsTitle && !bestHasTitle)
            || (score == bestScore && candidate.TreeContainsProcess == bestHasProcess && candidate.TreeContainsTitle == bestHasTitle && candidate.Area > bestArea);
    }

    private static bool WindowTreeContainsProcess(nint display, nint window, nint pidAtom, nint cardinalAtom, int processId)
    {
        if (TryGetWindowProcessId(display, window, pidAtom, cardinalAtom, out var windowProcessId)
            && windowProcessId == processId)
        {
            return true;
        }

        if (!TryQueryChildren(display, window, out var childrenPointer, out var childCount))
            return false;

        try
        {
            for (nuint index = 0; index < childCount; index++)
            {
                var childWindow = Marshal.ReadIntPtr(childrenPointer, checked((int)(index * (nuint)IntPtr.Size)));
                if (childWindow != nint.Zero
                    && WindowTreeContainsProcess(display, childWindow, pidAtom, cardinalAtom, processId))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (childrenPointer != nint.Zero)
                _ = XFree(childrenPointer);
        }
    }

    private static bool WindowTreeContainsTitle(nint display, nint window, nint netWmNameAtom, nint utf8StringAtom, string windowTitle)
    {
        if (TryGetWindowTitle(display, window, netWmNameAtom, utf8StringAtom, out var currentTitle)
            && currentTitle.Contains(windowTitle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TryQueryChildren(display, window, out var childrenPointer, out var childCount))
            return false;

        try
        {
            for (nuint index = 0; index < childCount; index++)
            {
                var childWindow = Marshal.ReadIntPtr(childrenPointer, checked((int)(index * (nuint)IntPtr.Size)));
                if (childWindow != nint.Zero && WindowTreeContainsTitle(display, childWindow, netWmNameAtom, utf8StringAtom, windowTitle))
                    return true;
            }

            return false;
        }
        finally
        {
            if (childrenPointer != nint.Zero)
                _ = XFree(childrenPointer);
        }
    }

    private static bool TryQueryChildren(nint display, nint window, out nint childrenPointer, out nuint childCount)
        => XQueryTree(
            display,
            window,
            out _,
            out _,
            out childrenPointer,
            out childCount) != 0;

    private static bool TryGetWindowGeometry(nint display, nint window, out Rectangle bounds)
    {
        bounds = default;

        if (XGetGeometry(
            display,
            window,
            out _,
            out var x,
            out var y,
            out var width,
            out var height,
            out _,
            out _) == 0)
        {
            return false;
        }

        if (width == 0 || height == 0)
            return false;

        bounds = new Rectangle(x, y, checked((int)width), checked((int)height));
        return true;
    }

    private static bool TryGetWindowProcessId(nint display, nint window, nint pidAtom, nint cardinalAtom, out int processId)
    {
        processId = 0;

        var status = XGetWindowProperty(
            display,
            window,
            pidAtom,
            longOffset: nint.Zero,
            longLength: 1,
            delete: 0,
            cardinalAtom,
            out var actualType,
            out var actualFormat,
            out var itemCount,
            out _,
            out var propertyPointer);

        try
        {
            if (status != 0
                || actualType == nint.Zero
                || actualFormat != 32
                || itemCount == 0
                || propertyPointer == nint.Zero)
            {
                return false;
            }

            processId = Marshal.ReadInt32(propertyPointer);
            return processId > 0;
        }
        finally
        {
            if (propertyPointer != nint.Zero)
                _ = XFree(propertyPointer);
        }
    }

    private static bool TryGetWindowTitle(nint display, nint window, nint netWmNameAtom, nint utf8StringAtom, out string title)
    {
        title = string.Empty;

        if (netWmNameAtom != nint.Zero
            && utf8StringAtom != nint.Zero
            && TryGetUtf8WindowProperty(display, window, netWmNameAtom, utf8StringAtom, out title))
        {
            return true;
        }

        if (XFetchName(display, window, out var titlePointer) == 0 || titlePointer == nint.Zero)
            return false;

        try
        {
            title = Marshal.PtrToStringAnsi(titlePointer) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(title);
        }
        finally
        {
            _ = XFree(titlePointer);
        }
    }

    private static bool TryGetUtf8WindowProperty(nint display, nint window, nint propertyAtom, nint utf8StringAtom, out string value)
    {
        value = string.Empty;

        var status = XGetWindowProperty(
            display,
            window,
            propertyAtom,
            longOffset: nint.Zero,
            longLength: 1024,
            delete: 0,
            utf8StringAtom,
            out var actualType,
            out var actualFormat,
            out var itemCount,
            out _,
            out var propertyPointer);

        try
        {
            if (status != 0
                || actualType == nint.Zero
                || actualType != utf8StringAtom
                || actualFormat != 8
                || itemCount == 0
                || propertyPointer == nint.Zero)
            {
                return false;
            }

            value = Marshal.PtrToStringUTF8(propertyPointer, checked((int)itemCount)) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }
        finally
        {
            if (propertyPointer != nint.Zero)
                _ = XFree(propertyPointer);
        }
    }

    private static string BuildDiagnostics(
        string displayName,
        int processId,
        Size? expectedSize,
        string? windowTitle,
        bool pidLookupAvailable,
        bool titleLookupAvailable,
        IReadOnlyList<TopLevelWindowCandidate> candidates,
        string strategy,
        TopLevelWindowCandidate? selectedCandidate)
    {
        var pidCandidateCount = CountCandidates(candidates, static candidate => candidate.TreeContainsProcess);
        var titleCandidateCount = CountCandidates(candidates, static candidate => candidate.TreeContainsTitle);
        var sizeCandidateCount = CountCandidates(candidates, static candidate => candidate.SizeScore is not null);

        return string.Concat(
            "strategy=", strategy,
            ";display=", Sanitize(displayName),
            ";processId=", Invariant(processId),
            ";expectedSize=", FormatSize(expectedSize),
            ";windowTitle=", Sanitize(windowTitle),
            ";pidLookup=", pidLookupAvailable ? "true" : "false",
            ";titleLookup=", titleLookupAvailable ? "true" : "false",
            ";rootChildCount=", Invariant(candidates.Count),
            ";pidCandidates=", Invariant(pidCandidateCount),
            ";titleCandidates=", Invariant(titleCandidateCount),
            ";sizeCandidates=", Invariant(sizeCandidateCount),
            ";selectedWindow=", selectedCandidate is { } candidate ? FormatWindowId(candidate.WindowId) : "<null>",
            ";selectedBounds=", selectedCandidate is { Bounds: Rectangle bounds } ? FormatRectangle(bounds) : "<null>");
    }

    private static int CountCandidates(IReadOnlyList<TopLevelWindowCandidate> candidates, Predicate<TopLevelWindowCandidate> predicate)
    {
        var count = 0;

        for (var index = 0; index < candidates.Count; index++)
        {
            if (predicate(candidates[index]))
                count++;
        }

        return count;
    }

    private static string FormatSize(Size? size)
        => size is { Width: > 0, Height: > 0 } currentSize
            ? string.Concat(Invariant(currentSize.Width), "x", Invariant(currentSize.Height))
            : "<null>";

    private static string FormatWindowId(nint window)
        => window == nint.Zero
            ? "<null>"
            : string.Concat("0x", unchecked((ulong)window.ToInt64()).ToString("x", CultureInfo.InvariantCulture));

    private static string FormatRectangle(Rectangle bounds)
        => string.Concat(
            Invariant(bounds.X),
            ",",
            Invariant(bounds.Y),
            ",",
            Invariant(bounds.Width),
            "x",
            Invariant(bounds.Height));

    private static string Invariant(int value)
        => value.ToString(CultureInfo.InvariantCulture);

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "<null>";

        var sanitized = value.Trim()
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace(';', '/')
            .Replace('|', '/');

        return sanitized.Length <= 96 ? sanitized : string.Concat(sanitized[..93], "...");
    }

    internal readonly record struct Resolution(Rectangle? Bounds, string Diagnostics);

    private readonly record struct TopLevelWindowCandidate(
        nint WindowId,
        Rectangle? Bounds,
        int? DirectProcessId,
        bool TreeContainsProcess,
        string? DirectTitle,
        bool TreeContainsTitle,
        long? SizeScore)
    {
        internal long Area => Bounds is Rectangle bounds
            ? (long)bounds.Width * bounds.Height
            : -1;
    }

    [LibraryImport("libX11.so.6", EntryPoint = "XOpenDisplay", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial nint XOpenDisplay(string? displayName);

    [LibraryImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport("libX11.so.6", EntryPoint = "XDefaultScreen")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XDefaultScreen(nint display);

    [LibraryImport("libX11.so.6", EntryPoint = "XRootWindow")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial nint XRootWindow(nint display, int screenNumber);

    [LibraryImport("libX11.so.6", EntryPoint = "XInternAtom", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial nint XInternAtom(nint display, string atomName, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [LibraryImport("libX11.so.6", EntryPoint = "XQueryTree")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XQueryTree(
        nint display,
        nint window,
        out nint rootReturn,
        out nint parentReturn,
        out nint childrenReturn,
        out nuint childCountReturn);

    [LibraryImport("libX11.so.6", EntryPoint = "XGetGeometry")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XGetGeometry(
        nint display,
        nint drawable,
        out nint rootReturn,
        out int xReturn,
        out int yReturn,
        out uint widthReturn,
        out uint heightReturn,
        out uint borderWidthReturn,
        out uint depthReturn);

    [LibraryImport("libX11.so.6", EntryPoint = "XGetWindowProperty")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XGetWindowProperty(
        nint display,
        nint window,
        nint property,
        nint longOffset,
        nint longLength,
        int delete,
        nint requestedType,
        out nint actualTypeReturn,
        out int actualFormatReturn,
        out nuint itemCountReturn,
        out nuint bytesAfterReturn,
        out nint propertyReturn);

    [LibraryImport("libX11.so.6", EntryPoint = "XFetchName")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XFetchName(nint display, nint window, out nint windowNameReturn);

    [LibraryImport("libX11.so.6", EntryPoint = "XFree")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XFree(nint data);
}