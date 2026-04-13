using System.Text.Json.Serialization;

namespace Atom.Web.Emails.Services;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GuerrillaMailSetEmailAddressResponse))]
[JsonSerializable(typeof(GuerrillaMailInboxResponse))]
[JsonSerializable(typeof(GuerrillaMailMessageSummaryResponse[]))]
[JsonSerializable(typeof(GuerrillaMailMessageSummaryResponse))]
[JsonSerializable(typeof(GuerrillaMailMessageDetailResponse))]
internal sealed partial class GuerrillaMailJsonContext : JsonSerializerContext;