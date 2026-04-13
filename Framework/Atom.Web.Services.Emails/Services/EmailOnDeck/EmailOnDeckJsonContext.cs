using System.Text.Json.Serialization;

namespace Atom.Web.Emails.Services;

[JsonSerializable(typeof(EmailOnDeckCreateMailboxResponse))]
[JsonSerializable(typeof(EmailOnDeckMessageResponse[]))]
internal sealed partial class EmailOnDeckJsonContext : JsonSerializerContext;