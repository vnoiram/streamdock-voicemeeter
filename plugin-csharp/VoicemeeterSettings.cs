using System.Text.Json;

namespace StreamDockVoicemeeter;

public sealed record VoicemeeterSettings(
    string ChannelKind,
    int ChannelIndex,
    double Step,
    string DeviceId,
    int MacroButtonIndex,
    string AppCommand,
    IReadOnlyList<string> OverviewTargets,
    string BalancePrimaryKind,
    int BalancePrimaryIndex,
    string BalanceSecondaryKind,
    int BalanceSecondaryIndex,
    double BalanceStep,
    string? TitleLabel,
    bool InvertKnob)
{
    public const int MaxChannelIndex = 7;

    public static VoicemeeterSettings FromDictionary(Dictionary<string, object>? settings)
    {
        var channelKind = NormalizeChannelKind(ReadString(settings, "channelKind"));
        var channelIndex = Math.Clamp(ReadInt(settings, "channelIndex") ?? 0, 0, MaxChannelIndex);
        var step = Math.Clamp(ReadDouble(settings, "step") ?? 3.0, 0.1, 24.0);
        var deviceId = ReadString(settings, "deviceId") ?? "";
        var macroButtonIndex = Math.Clamp(ReadInt(settings, "macroButtonIndex") ?? 0, 0, 79);
        var appCommand = NormalizeAppCommand(ReadString(settings, "appCommand"));
        var overviewTargets = NormalizeOverviewTargets(ReadStringList(settings, "overviewTargets"), channelKind, channelIndex);
        var balancePrimaryKind = NormalizeChannelKind(ReadString(settings, "balancePrimaryKind"));
        var balancePrimaryIndex = Math.Clamp(ReadInt(settings, "balancePrimaryIndex") ?? 0, 0, MaxChannelIndex);
        var balanceSecondaryKind = NormalizeChannelKind(ReadString(settings, "balanceSecondaryKind"));
        var balanceSecondaryIndex = Math.Clamp(ReadInt(settings, "balanceSecondaryIndex") ?? 1, 0, MaxChannelIndex);
        var balanceStep = Math.Clamp(ReadDouble(settings, "balanceStep") ?? 1.0, 0.1, 12.0);
        var titleLabel = ReadString(settings, "titleLabel");
        var invertKnob = ReadBool(settings, "invert") ?? ReadBool(settings, "invertKnob") ?? false;

        return new VoicemeeterSettings(
            channelKind,
            channelIndex,
            step,
            deviceId,
            macroButtonIndex,
            appCommand,
            overviewTargets,
            balancePrimaryKind,
            balancePrimaryIndex,
            balanceSecondaryKind,
            balanceSecondaryIndex,
            balanceStep,
            titleLabel,
            invertKnob);
    }

    public string ChannelKey => BuildChannelKey(ChannelKind, ChannelIndex);

    public static string BuildChannelKey(string channelKind, int channelIndex) => $"{channelKind}:{channelIndex}";

    public string DisplayName => TitleLabel ?? DisplayNameFor(ChannelKind, ChannelIndex);

    public static string DisplayNameFor(string channelKind, int channelIndex)
    {
        var kindLabel = string.Equals(channelKind, "bus", StringComparison.OrdinalIgnoreCase) ? "Bus" : "Strip";
        return $"{kindLabel} {channelIndex}";
    }

    public static string ShortLabelFor(string channelKind, int channelIndex)
    {
        var prefix = string.Equals(channelKind, "bus", StringComparison.OrdinalIgnoreCase) ? "B" : "S";
        return $"{prefix}{channelIndex}";
    }

    private static string NormalizeChannelKind(string? kind)
    {
        return string.Equals(kind, "bus", StringComparison.OrdinalIgnoreCase) ? "bus" : "strip";
    }

    private static string NormalizeAppCommand(string? command)
    {
        return command switch
        {
            "restart" => "restart",
            "shutdown" => "shutdown",
            _ => "show"
        };
    }

    private static IReadOnlyList<string> NormalizeOverviewTargets(IEnumerable<string>? targets, string fallbackKind, int fallbackIndex)
    {
        if (targets == null) return [BuildChannelKey(fallbackKind, fallbackIndex)];
        var normalized = targets
            .Select(ParseChannelKey)
            .Where(pair => pair != null)
            .Select(pair => pair!.Value)
            .Select(pair => BuildChannelKey(pair.Kind, pair.Index))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
        return normalized.Length == 0 ? [BuildChannelKey(fallbackKind, fallbackIndex)] : normalized;
    }

    public static (string Kind, int Index)? ParseChannelKey(string channelKey)
    {
        var parts = channelKey.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return null;
        if (!int.TryParse(parts[1], out var index)) return null;
        return (NormalizeChannelKind(parts[0]), Math.Clamp(index, 0, MaxChannelIndex));
    }

    private static string? ReadString(Dictionary<string, object>? settings, string key)
    {
        if (settings == null || !settings.TryGetValue(key, out var value)) return null;
        if (value is string text) return string.IsNullOrWhiteSpace(text) ? null : text;
        if (value is JsonElement { ValueKind: JsonValueKind.String } element)
        {
            var textValue = element.GetString();
            return string.IsNullOrWhiteSpace(textValue) ? null : textValue;
        }

        return Convert.ToString(value);
    }

    private static IEnumerable<string>? ReadStringList(Dictionary<string, object>? settings, string key)
    {
        if (settings == null || !settings.TryGetValue(key, out var value)) return null;
        if (value is IEnumerable<string> stringValues) return stringValues;
        if (value is JsonElement { ValueKind: JsonValueKind.Array } element)
        {
            return element.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? "");
        }

        return null;
    }

    private static int? ReadInt(Dictionary<string, object>? settings, string key)
    {
        if (settings == null || !settings.TryGetValue(key, out var value)) return null;
        try
        {
            if (value is int intValue) return intValue;
            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var jsonInt)) return jsonInt;
                if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed)) return parsed;
            }

            return Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    private static double? ReadDouble(Dictionary<string, object>? settings, string key)
    {
        if (settings == null || !settings.TryGetValue(key, out var value)) return null;
        try
        {
            if (value is double doubleValue) return doubleValue;
            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var jsonDouble)) return jsonDouble;
                if (element.ValueKind == JsonValueKind.String && double.TryParse(element.GetString(), out var parsed)) return parsed;
            }

            return Convert.ToDouble(value);
        }
        catch
        {
            return null;
        }
    }

    private static bool? ReadBool(Dictionary<string, object>? settings, string key)
    {
        if (settings == null || !settings.TryGetValue(key, out var value)) return null;
        try
        {
            if (value is bool boolValue) return boolValue;
            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.True) return true;
                if (element.ValueKind == JsonValueKind.False) return false;
                if (element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var parsed)) return parsed;
            }

            return Convert.ToBoolean(value);
        }
        catch
        {
            return null;
        }
    }
}
