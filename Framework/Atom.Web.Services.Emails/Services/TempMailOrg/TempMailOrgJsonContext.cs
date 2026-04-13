using System.Text.Json.Serialization;

namespace Atom.Web.Emails.Services;

[JsonSerializable(typeof(TempMailOrgMessageResponse[]))]
internal sealed partial class TempMailOrgJsonContext : JsonSerializerContext;