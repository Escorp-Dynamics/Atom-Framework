using System.Text.Json.Serialization;

namespace Atom.Web.Analytics.Tests;

public class Test2
{
    [JsonConverter(typeof(CountryJsonConverter<ushort>))]
    public Country? Currency { get; set; }
}

public class CountryTests
{

}