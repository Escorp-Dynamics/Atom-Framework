using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.Protocol;
using Atom.Web.Browsing.BiDi.Tests;
using Atom.Web.Browsing.BiDi.TestUtilities;

namespace Atom.Web.Browsing.BiDi.JsonConverters.Tests;


[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    WriteIndented = true
)]
[JsonSerializable(typeof(OnlyOverflowData))]
[JsonSerializable(typeof(EventMessage<TestEventArgs>))]
[JsonSerializable(typeof(TestCommandResult))]
[JsonSerializable(typeof(BasicEnum))]
[JsonSerializable(typeof(EnumWithDefault))]
[JsonSerializable(typeof(TestCommandParameters))]
[JsonSerializable(typeof(CommandResponseMessage<TestCommandResult>))]
public partial class JsonTestContext : JsonSerializerContext;