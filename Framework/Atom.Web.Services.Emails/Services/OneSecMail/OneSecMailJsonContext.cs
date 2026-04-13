using System.Text.Json.Serialization;

namespace Atom.Web.Emails.Services;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(OneSecMailMessageSummaryResponse[]))]
[JsonSerializable(typeof(OneSecMailMessageSummaryResponse))]
[JsonSerializable(typeof(OneSecMailMessageDetailResponse))]
internal sealed partial class OneSecMailJsonContext : JsonSerializerContext;