using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// The timings for a fetch operation.
/// </summary>
public class FetchTimingInfo
{

    /// <summary>
    /// Initializes a new instance of the <see cref="FetchTimingInfo"/> class.
    /// </summary>
    [JsonConstructor]
    internal FetchTimingInfo() { }

    /// <summary>
    /// Gets the time origin of the fetch request.
    /// </summary>
    [JsonPropertyName("timeOrigin")]
    [JsonRequired]
    [JsonInclude]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double TimeOrigin { get; internal set; } = 0;

    /// <summary>
    /// Gets the request time of the fetch request.
    /// </summary>
    [JsonPropertyName("requestTime")]
    [JsonRequired]
    [JsonInclude]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double RequestTime { get; internal set; } = 0;

    /// <summary>
    /// Gets the redirect start time offset from the time origin of the fetch request.
    /// </summary>
    [JsonPropertyName("redirectStart")]
    [JsonRequired]
    [JsonInclude]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double RedirectStart { get; internal set; } = 0;

    /// <summary>
    /// Gets the redirect end time offset from the time origin of the fetch request.
    /// </summary>
    [JsonPropertyName("redirectEnd")]
    [JsonRequired]
    [JsonInclude]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double RedirectEnd { get; internal set; } = 0;

    /// <summary>
    /// Gets the fetch start time offset from the time origin of the fetch request.
    /// </summary>
    [JsonPropertyName("fetchStart")]
    [JsonRequired]
    [JsonInclude]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double FetchStart { get; internal set; } = 0;

    /// <summary>
    /// Gets the DNS start time offset from the time origin of the fetch request.
    /// </summary>
    [JsonPropertyName("dnsStart")]
    [JsonRequired]
    [JsonInclude]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double DnsStart { get; internal set; } = 0;

    /// <summary>
    /// Gets the DNS end time offset from the time origin of the fetch request.
    /// </summary>
    [JsonPropertyName("dnsEnd")]
    [JsonRequired]
    [JsonInclude]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double DnsEnd { get; internal set; } = 0;

    /// <summary>
    /// Gets the connect start time offset from the time origin of the fetch request.
    /// </summary>
    [JsonPropertyName("connectStart")]
    [JsonRequired]
    [JsonInclude]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double ConnectStart { get; internal set; } = 0;

    /// <summary>
    /// Gets the connect end time offset from the time origin of the fetch request.
    /// </summary>
    [JsonPropertyName("connectEnd")]
    [JsonRequired]
    [JsonInclude]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double ConnectEnd { get; internal set; } = 0;

    /// <summary>
    /// Gets the TLS start time offset from the time origin of the fetch request.
    /// </summary>
    [JsonPropertyName("tlsStart")]
    [JsonRequired]
    [JsonInclude]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double TlsStart { get; internal set; } = 0;

    /// <summary>
    /// Gets the request start time offset from the time origin of the fetch request.
    /// </summary>
    [JsonPropertyName("requestStart")]
    [JsonRequired]
    [JsonInclude]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double RequestStart { get; internal set; } = 0;

    /// <summary>
    /// Gets the response start time offset from the time origin of the fetch request.
    /// </summary>
    [JsonPropertyName("responseStart")]
    [JsonRequired]
    [JsonInclude]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double ResponseStart { get; internal set; } = 0;

    /// <summary>
    /// Gets the response end time offset from the time origin of the fetch request.
    /// </summary>
    [JsonPropertyName("responseEnd")]
    [JsonRequired]
    [JsonInclude]
    [JsonConverter(typeof(FixedDoubleJsonConverter))]
    public double ResponseEnd { get; internal set; } = 0;
}