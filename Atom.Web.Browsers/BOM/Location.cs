using Atom.Architect.Reactive;
using Atom.Web.Browsers.DOM;
using Microsoft.ClearScript;

namespace Atom.Web.Browsers.BOM;

/// <summary>
/// Представляет локацию страницы.
/// </summary>
public class Location : Reactively, ILocation
{
    private Uri href;
    private string protocol;
    private string host;
    private string hostName;
    private string port;
    private string path;
    private string search;
    private string hash;

    /// <inheritdoc/>
    [ScriptMember]
    public Uri Href
    {
        get => href;

        set
        {
            SetProperty(ref href, value);
            if (value is not null) ParseHref(value);
        }
    }

    /// <inheritdoc/>
    [ScriptMember]
    public Uri Origin => new($"{protocol}//{hostName}");

    /// <inheritdoc/>
    [ScriptMember]
    public string Protocol
    {
        get => protocol;

        set
        {
            SetProperty(ref protocol, value);
            UpdateHref();
        }
    }

    /// <inheritdoc/>
    [ScriptMember]
    public string Host
    {
        get => host;

        set
        {
            SetProperty(ref host, value);
            UpdateHref();
        }
    }

    /// <inheritdoc/>
    [ScriptMember]
    public string HostName
    {
        get => hostName;

        set
        {
            SetProperty(ref hostName, value);
            UpdateHref();
        }
    }

    /// <inheritdoc/>
    [ScriptMember]
    public string Port
    {
        get => port;

        set
        {
            SetProperty(ref port, value);
            UpdateHref();
        }
    }

    /// <inheritdoc/>
    [ScriptMember]
    public string Path
    {
        get => path;

        set
        {
            SetProperty(ref path, value);
            UpdateHref();
        }
    }

    /// <inheritdoc/>
    [ScriptMember]
    public string Search
    {
        get => search;

        set
        {
            SetProperty(ref search, value);
            UpdateHref();
        }
    }

    /// <inheritdoc/>
    [ScriptMember]
    public string Hash
    {
        get => hash;

        set
        {
            SetProperty(ref hash, value);
            UpdateHref();
        }
    }

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

    [ScriptMember(ScriptAccess.None)]
    private void ParseHref(Uri url)
    {
        protocol = url.Scheme;
        host = url.Host;
        hostName = url.Host;
        port = url.Port.ToString();
        path = url.AbsolutePath;
        search = url.Query;
        hash = url.Fragment;
    }

    [ScriptMember(ScriptAccess.None)]
    private void UpdateHref() => href = new Uri($"{protocol}//{host}{path}{search}{hash}");

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