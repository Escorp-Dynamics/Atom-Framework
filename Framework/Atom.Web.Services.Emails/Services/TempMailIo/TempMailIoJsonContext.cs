using System.Text.Json.Serialization;

namespace Atom.Web.Emails.Services;

[JsonSerializable(typeof(TempMailIoCreateMailboxRequest))]
[JsonSerializable(typeof(TempMailIoCreateMailboxResponse))]
[JsonSerializable(typeof(TempMailIoMessageResponse[]))]
internal sealed partial class TempMailIoJsonContext : JsonSerializerContext;