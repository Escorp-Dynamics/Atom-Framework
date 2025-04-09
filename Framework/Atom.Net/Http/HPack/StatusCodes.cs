using System.Globalization;
using System.Net;
using System.Text;

namespace Atom.Net.Http.HPack;

internal static class StatusCodes
{
    public static ReadOnlySpan<byte> ToStatusBytes(int statusCode)
    {
        return statusCode switch
        {
            (int)HttpStatusCode.Continue => "100"u8,
            (int)HttpStatusCode.SwitchingProtocols => "101"u8,
            (int)HttpStatusCode.Processing => "102"u8,
            (int)HttpStatusCode.OK => "200"u8,
            (int)HttpStatusCode.Created => "201"u8,
            (int)HttpStatusCode.Accepted => "202"u8,
            (int)HttpStatusCode.NonAuthoritativeInformation => "203"u8,
            (int)HttpStatusCode.NoContent => "204"u8,
            (int)HttpStatusCode.ResetContent => "205"u8,
            (int)HttpStatusCode.PartialContent => "206"u8,
            (int)HttpStatusCode.MultiStatus => "207"u8,
            (int)HttpStatusCode.AlreadyReported => "208"u8,
            (int)HttpStatusCode.IMUsed => "226"u8,
            (int)HttpStatusCode.MultipleChoices => "300"u8,
            (int)HttpStatusCode.MovedPermanently => "301"u8,
            (int)HttpStatusCode.Found => "302"u8,
            (int)HttpStatusCode.SeeOther => "303"u8,
            (int)HttpStatusCode.NotModified => "304"u8,
            (int)HttpStatusCode.UseProxy => "305"u8,
            (int)HttpStatusCode.Unused => "306"u8,
            (int)HttpStatusCode.TemporaryRedirect => "307"u8,
            (int)HttpStatusCode.PermanentRedirect => "308"u8,
            (int)HttpStatusCode.BadRequest => "400"u8,
            (int)HttpStatusCode.Unauthorized => "401"u8,
            (int)HttpStatusCode.PaymentRequired => "402"u8,
            (int)HttpStatusCode.Forbidden => "403"u8,
            (int)HttpStatusCode.NotFound => "404"u8,
            (int)HttpStatusCode.MethodNotAllowed => "405"u8,
            (int)HttpStatusCode.NotAcceptable => "406"u8,
            (int)HttpStatusCode.ProxyAuthenticationRequired => "407"u8,
            (int)HttpStatusCode.RequestTimeout => "408"u8,
            (int)HttpStatusCode.Conflict => "409"u8,
            (int)HttpStatusCode.Gone => "410"u8,
            (int)HttpStatusCode.LengthRequired => "411"u8,
            (int)HttpStatusCode.PreconditionFailed => "412"u8,
            (int)HttpStatusCode.RequestEntityTooLarge => "413"u8,
            (int)HttpStatusCode.RequestUriTooLong => "414"u8,
            (int)HttpStatusCode.UnsupportedMediaType => "415"u8,
            (int)HttpStatusCode.RequestedRangeNotSatisfiable => "416"u8,
            (int)HttpStatusCode.ExpectationFailed => "417"u8,
            418 => "418"u8,
            419 => "419"u8,
            (int)HttpStatusCode.MisdirectedRequest => "421"u8,
            (int)HttpStatusCode.UnprocessableEntity => "422"u8,
            (int)HttpStatusCode.Locked => "423"u8,
            (int)HttpStatusCode.FailedDependency => "424"u8,
            (int)HttpStatusCode.UpgradeRequired => "426"u8,
            (int)HttpStatusCode.PreconditionRequired => "428"u8,
            (int)HttpStatusCode.TooManyRequests => "429"u8,
            (int)HttpStatusCode.RequestHeaderFieldsTooLarge => "431"u8,
            (int)HttpStatusCode.UnavailableForLegalReasons => "451"u8,
            (int)HttpStatusCode.InternalServerError => "500"u8,
            (int)HttpStatusCode.NotImplemented => "501"u8,
            (int)HttpStatusCode.BadGateway => "502"u8,
            (int)HttpStatusCode.ServiceUnavailable => "503"u8,
            (int)HttpStatusCode.GatewayTimeout => "504"u8,
            (int)HttpStatusCode.HttpVersionNotSupported => "505"u8,
            (int)HttpStatusCode.VariantAlsoNegotiates => "506"u8,
            (int)HttpStatusCode.InsufficientStorage => "507"u8,
            (int)HttpStatusCode.LoopDetected => "508"u8,
            (int)HttpStatusCode.NotExtended => "510"u8,
            (int)HttpStatusCode.NetworkAuthenticationRequired => "511"u8,
            _ => (ReadOnlySpan<byte>)Encoding.ASCII.GetBytes(statusCode.ToString(CultureInfo.InvariantCulture)),
        };
    }
}