using System.Text.Json.Serialization;

namespace Atom.Web.Emails.Services;

[JsonSerializable(typeof(EmailnatorCreateMailboxRequest))]
[JsonSerializable(typeof(EmailnatorCreateMailboxResponse))]
[JsonSerializable(typeof(EmailnatorMessageListRequest))]
[JsonSerializable(typeof(EmailnatorMessageListResponse))]
[JsonSerializable(typeof(EmailnatorMessageDetailRequest))]
[JsonSerializable(typeof(EmailnatorMessageDetailResponse))]
[JsonSerializable(typeof(EmailnatorDeleteMessageRequest))]
internal sealed partial class EmailnatorJsonContext : JsonSerializerContext;