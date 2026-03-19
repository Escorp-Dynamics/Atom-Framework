using System.Text.Json;

namespace Atom.Tests;

public sealed class PipeWireNodeSnapshot
{
    public int NodeId { get; init; }
    public string? NodeName { get; init; }
    public string? NodeDescription { get; init; }
    public string? NodeLatency { get; init; }
    public string? NodeGroup { get; init; }
    public string? MediaType { get; init; }
    public string? MediaCategory { get; init; }
    public string? MediaRole { get; init; }
    public string? MediaClass { get; init; }
    public string? NodeVirtual { get; init; }
    public string? FormatMediaType { get; init; }
    public string? FormatMediaSubtype { get; init; }
    public string? FormatName { get; init; }
    public int? FormatRate { get; init; }
    public int? FormatChannels { get; init; }
    public string? DeviceVendor { get; init; }
    public string? DeviceVendorId { get; init; }
    public string? DeviceProduct { get; init; }
    public string? DeviceProductId { get; init; }
    public string? DeviceSerial { get; init; }
    public string? DeviceId { get; init; }
}

public static class PipeWireSnapshotHelpers
{
    public static async Task<List<PipeWireNodeSnapshot>> GetNodesAsync()
    {
        var output = await ProcessCommandHelpers.RunPwDumpAsync();
        return ParseNodes(output);
    }

    public static List<PipeWireNodeSnapshot> ParseNodes(string json)
    {
        var result = new List<PipeWireNodeSnapshot>();

        using var doc = JsonDocument.Parse(json);

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("type", out var typeEl)
                || typeEl.GetString() != "PipeWire:Interface:Node")
            {
                continue;
            }

            if (!element.TryGetProperty("info", out var info)
                || !info.TryGetProperty("props", out var props))
            {
                continue;
            }

            result.Add(new PipeWireNodeSnapshot
            {
                NodeId = element.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0,
                NodeName = GetStringProp(props, "node.name"),
                NodeDescription = GetStringProp(props, "node.description"),
                NodeLatency = GetStringProp(props, "node.latency"),
                NodeGroup = GetStringProp(props, "node.group"),
                MediaType = GetStringProp(props, "media.type"),
                MediaCategory = GetStringProp(props, "media.category"),
                MediaRole = GetStringProp(props, "media.role"),
                MediaClass = GetStringProp(props, "media.class"),
                NodeVirtual = GetStringProp(props, "node.virtual"),
                FormatMediaType = GetEnumFormatProp(info, "mediaType"),
                FormatMediaSubtype = GetEnumFormatProp(info, "mediaSubtype"),
                FormatName = GetEnumFormatProp(info, "format"),
                FormatRate = GetEnumFormatInt(info, "rate"),
                FormatChannels = GetEnumFormatInt(info, "channels"),
                DeviceVendor = GetStringProp(props, "device.vendor.name"),
                DeviceVendorId = GetStringProp(props, "device.vendor.id"),
                DeviceProduct = GetStringProp(props, "device.product.name"),
                DeviceProductId = GetStringProp(props, "device.product.id"),
                DeviceSerial = GetStringProp(props, "device.serial"),
                DeviceId = GetStringProp(props, "device.id"),
            });
        }

        return result;
    }

    private static string? GetStringProp(JsonElement props, string key)
    {
        if (!props.TryGetProperty(key, out var val))
        {
            return null;
        }

        return val.ValueKind switch
        {
            JsonValueKind.String => val.GetString(),
            JsonValueKind.Number => val.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static string? GetEnumFormatProp(JsonElement info, string key)
    {
        if (!info.TryGetProperty("params", out var pars)
            || !pars.TryGetProperty("EnumFormat", out var enumFormat)
            || enumFormat.GetArrayLength() == 0)
        {
            return null;
        }

        var first = enumFormat[0];
        if (!first.TryGetProperty(key, out var val))
        {
            return null;
        }

        return val.ValueKind == JsonValueKind.String ? val.GetString() : val.GetRawText();
    }

    private static int? GetEnumFormatInt(JsonElement info, string key)
    {
        var raw = GetEnumFormatProp(info, key);
        return raw is not null && int.TryParse(raw, out var n) ? n : null;
    }
}