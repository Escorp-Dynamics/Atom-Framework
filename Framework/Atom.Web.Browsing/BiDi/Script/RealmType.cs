using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// The type of realm.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<RealmType>))]
public enum RealmType
{
    /// <summary>
    /// A window realm.
    /// </summary>
    [JsonEnumValue("window")]
    Window,

    /// <summary>
    /// A dedicated worker realm.
    /// </summary>
    [JsonEnumValue("dedicated-worker")]
    DedicatedWorker,

    /// <summary>
    /// A shared worker realm.
    /// </summary>
    [JsonEnumValue("shared-worker")]
    SharedWorker,

    /// <summary>
    /// A service worker realm.
    /// </summary>
    [JsonEnumValue("service-worker")]
    ServiceWorker,

    /// <summary>
    /// A worker realm.
    /// </summary>
    [JsonEnumValue("worker")]
    Worker,

    /// <summary>
    /// A paint worklet realm.
    /// </summary>
    [JsonEnumValue("paint-worklet")]
    PaintWorklet,

    /// <summary>
    /// An audio worklet realm.
    /// </summary>
    [JsonEnumValue("audio-worklet")]
    AudioWorklet,

    /// <summary>
    /// A worklet realm.
    /// </summary>
    [JsonEnumValue("worklet")]
    Worklet,
}