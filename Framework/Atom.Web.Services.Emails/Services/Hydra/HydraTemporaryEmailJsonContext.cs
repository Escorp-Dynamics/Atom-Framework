using System.Text.Json.Serialization;

namespace Atom.Web.Emails.Services;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HydraDomainCollectionResponse))]
[JsonSerializable(typeof(HydraDomainResponse))]
[JsonSerializable(typeof(HydraCreateAccountRequest))]
[JsonSerializable(typeof(HydraAccountResponse))]
[JsonSerializable(typeof(HydraTokenRequest))]
[JsonSerializable(typeof(HydraTokenResponse))]
[JsonSerializable(typeof(HydraMessageCollectionResponse))]
[JsonSerializable(typeof(HydraMessageSummaryResponse))]
[JsonSerializable(typeof(HydraMessageDetailResponse))]
[JsonSerializable(typeof(HydraAddressResponse))]
[JsonSerializable(typeof(HydraMarkSeenRequest))]
internal sealed partial class HydraTemporaryEmailJsonContext : JsonSerializerContext;