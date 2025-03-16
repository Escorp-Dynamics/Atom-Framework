namespace Atom.Web.Browsing.BiDi.TestUtilities;

using System.Text.Json.Serialization;

public class TestEventArgs : BiDiEventArgs
{
    [JsonRequired]
    public string ParamName { get; set; } = "paramValue";
}