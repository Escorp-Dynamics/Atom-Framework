using System.Text.Json.Serialization;

namespace Atom.Web.Emails.Services;

[JsonSerializable(typeof(DropMailGraphQLRequest))]
[JsonSerializable(typeof(DropMailIntroduceSessionEnvelope))]
[JsonSerializable(typeof(DropMailSessionEnvelope))]
internal sealed partial class DropMailJsonContext : JsonSerializerContext;