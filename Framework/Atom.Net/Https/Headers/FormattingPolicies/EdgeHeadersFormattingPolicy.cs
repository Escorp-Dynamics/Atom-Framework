using System.Diagnostics.CodeAnalysis;

namespace Atom.Net.Https.Headers;

/// <summary>
///
/// </summary>
[SuppressMessage("Major Code Smell", "S2094:Classes should not be empty", Justification = "Marker type for Edge-specific header policy selection.")]
public class EdgeHeadersFormattingPolicy : ChromeHeadersFormattingPolicy;