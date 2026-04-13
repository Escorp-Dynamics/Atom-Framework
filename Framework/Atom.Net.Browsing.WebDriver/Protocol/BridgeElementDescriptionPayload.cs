using System.Drawing;

namespace Atom.Net.Browsing.WebDriver.Protocol;

internal sealed record BridgeElementDescriptionPayload(
    string TagName,
    bool Checked,
    int SelectedIndex,
    bool IsActive,
    bool IsConnected,
    bool IsVisible,
    string? AssociatedControlId,
    RectangleF BoundingBox,
    IReadOnlyDictionary<string, string> ComputedStyle,
    BridgeElementOptionPayload[] Options);