using Atom.Architect.Reactive;
using Atom.Web.Browsing.DOM;
using Microsoft.ClearScript;

namespace Atom.Web.Browsing.BOM;

/// <summary>
/// Представляет локацию страницы.
/// </summary>
public partial class Location : ILocation
{
    /// <inheritdoc/>
    [Reactively(Attributes = [typeof(ScriptMemberAttribute)])]
    private Uri href;

    /// <inheritdoc/>
    [Reactively(Attributes = [typeof(ScriptMemberAttribute)])]
    private string protocol;

    /// <inheritdoc/>
    [Reactively(Attributes = [typeof(ScriptMemberAttribute)])]
    private string host;

    /// <inheritdoc/>
    [Reactively(Attributes = [typeof(ScriptMemberAttribute)])]
    private string hostName;

    /// <inheritdoc/>
    [Reactively(Attributes = [typeof(ScriptMemberAttribute)])]
    private string port;

    /// <inheritdoc/>
    [Reactively(Attributes = [typeof(ScriptMemberAttribute)])]
    private string path;

    /// <inheritdoc/>
    [Reactively(Attributes = [typeof(ScriptMemberAttribute)])]
    private string search;

    /// <inheritdoc/>
    [Reactively(Attributes = [typeof(ScriptMemberAttribute)])]
    private string hash;

    /// <inheritdoc/>
    [ScriptMember]
    public Uri Origin => new($"{protocol}://{hostName}");

    /// <inheritdoc/>
    [ScriptMember]
    public IDOMStringList AncestorOrigins { get; } = new DOMStringList();

    internal Location(Uri url)
    {
        href = url;
        protocol = url.Scheme;
        host = url.Host;
        hostName = url.Host;
        port = url.Port.ToString();
        path = url.AbsolutePath;
        search = url.Query;
        hash = url.Fragment;
    }

    /*[ScriptMember(ScriptAccess.None)]
    private void ParseHref(Uri url)
    {
        protocol = url.Scheme;
        host = url.Host;
        hostName = url.Host;
        port = url.Port.ToString();
        path = url.AbsolutePath;
        search = url.Query;
        hash = url.Fragment;
    }*/

    /*[ScriptMember(ScriptAccess.None)]
    private void UpdateHref() => href = new Uri($"{protocol}//{host}{path}{search}{hash}");*/

    /// <inheritdoc/>
    [ScriptMember]
    public void Assign(Uri url) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void Reload() => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember]
    public void Replace(Uri url) => throw new NotImplementedException();
}