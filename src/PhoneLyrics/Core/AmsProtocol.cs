using System.Globalization;

namespace PhoneLyrics;

internal sealed record AmsPlayback(int State, double Rate, double Elapsed)
{
    public static AmsPlayback? Parse(string text)
    {
        var parts = text.Split(',');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var state) || state is < 0 or > 3 ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var elapsed) ||
            !double.IsFinite(rate) || !double.IsFinite(elapsed) || elapsed < 0) return null;
        return new(state, rate, elapsed);
    }

    public double? Position(double ageSeconds)
    {
        if (!double.IsFinite(ageSeconds) || ageSeconds < 0) return null;
        if (State == 0) return Elapsed;
        if (State != 1 || Rate <= 0) return null;
        var position = Elapsed + ageSeconds * Rate;
        return double.IsFinite(position) ? position : null;
    }
}

internal sealed record AmsSnapshot(string Device, bool Connected, string Player, string PrimaryText,
    string Artist, string Album, double? Duration, string? QueueIndex, AmsPlayback? Playback,
    double? AnchorMonotonicSeconds, DateTimeOffset? AnchorReceivedUtc, int[] SupportedCommands,
    string? UnavailableReason)
{
    public double? PositionAt(double monotonicSeconds)
    {
        if (!Connected || Playback == null || AnchorMonotonicSeconds == null) return null;
        var value = Playback.Position(monotonicSeconds - AnchorMonotonicSeconds.Value);
        return value == null ? null : Duration is > 0 ? Math.Clamp(value.Value, 0, Duration.Value) : value;
    }
}

internal static class AmsProtocol
{
    public static AmsSnapshot Apply(AmsSnapshot snapshot, byte entity, byte attr, string text, bool truncated,
        double monotonic, DateTimeOffset received)
    {
        AmsSnapshot Invalidate(AmsSnapshot state, string reason) => state with
        { Playback = null, AnchorMonotonicSeconds = null, AnchorReceivedUtc = null, UnavailableReason = reason };
        if (entity == 0 && attr == 0)
        {
            if (snapshot.Player.Length > 0 && snapshot.Player != text) snapshot = Invalidate(snapshot, "播放器变化，等待新的位置锚点");
            snapshot = snapshot with { Player = text };
        }
        if (entity == 0 && attr == 1)
        {
            var playback = truncated ? null : AmsPlayback.Parse(text);
            snapshot = snapshot with { Playback = playback, AnchorMonotonicSeconds = playback == null ? null : monotonic,
                AnchorReceivedUtc = playback == null ? null : received, UnavailableReason = playback == null ? "手机未提供有效播放位置" : null };
        }
        if (entity == 1 && attr == 0 && !truncated)
        {
            if (snapshot.QueueIndex != null && snapshot.QueueIndex != text) snapshot = Invalidate(snapshot, "曲目变化，等待新的位置锚点");
            snapshot = snapshot with { QueueIndex = text };
        }
        if (entity == 2)
        {
            // Title can be a changing lock-screen lyric. Never use it as a stable track identity.
            // When Queue.Index is absent, other metadata changes conservatively discard the old position anchor.
            if (attr == 0 && !truncated && snapshot.Artist.Length > 0 && !snapshot.Artist.EndsWith('…') && snapshot.Artist != text)
                snapshot = Invalidate(snapshot, "曲目元数据变化，等待新的位置锚点");
            if (attr == 1 && !truncated && snapshot.Album.Length > 0 && !snapshot.Album.EndsWith('…') && snapshot.Album != text)
                snapshot = Invalidate(snapshot, "曲目元数据变化，等待新的位置锚点");
            if (attr == 0) snapshot = snapshot with { Artist = text + (truncated ? "…" : "") };
            if (attr == 1) snapshot = snapshot with { Album = text + (truncated ? "…" : "") };
            if (attr == 2) snapshot = snapshot with { PrimaryText = text + (truncated ? "…" : "") };
            if (attr == 3)
            {
                double? duration = !truncated && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    && double.IsFinite(parsed) && parsed > 0 ? parsed : null;
                if (snapshot.Duration != null && duration != snapshot.Duration) snapshot = Invalidate(snapshot, "曲目时长变化，等待新的位置锚点");
                snapshot = snapshot with { Duration = duration };
            }
        }
        return snapshot;
    }
}
