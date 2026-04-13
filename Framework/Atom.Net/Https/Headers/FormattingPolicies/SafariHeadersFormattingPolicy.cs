using System.Diagnostics.CodeAnalysis;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Политика форматирования заголовков для Apple Safari.
/// </summary>
[SuppressMessage("Major Code Smell", "S2094:Classes should not be empty", Justification = "Marker type for Safari-specific header policy selection.")]
public class SafariHeadersFormattingPolicy : ChromeHeadersFormattingPolicy;