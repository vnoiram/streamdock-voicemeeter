namespace StreamDockVoicemeeter;

public sealed record VoicemeeterOverviewState(
    string ChannelKey,
    string ShortLabel,
    double? GainDb,
    bool? Muted,
    string? Error)
{
    public bool Ok => Error == null;

    public string ValueText
    {
        get
        {
            if (Error != null) return "ERR";
            if (Muted == true) return "M";
            var rounded = Math.Round(GainDb ?? 0);
            return rounded > 0 ? $"+{rounded:0}" : $"{rounded:0}";
        }
    }
}
