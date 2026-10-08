using PhoneLyrics;

var passed = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new Exception("FAIL " + name);
    Console.WriteLine("PASS " + name);
    passed++;
}

void Rejects(string name, Action parse)
{
    try { parse(); }
    catch (ArgumentException) { Check(name, true); return; }
    throw new Exception("FAIL " + name);
}

Rejects("legacy control command is rejected before Bluetooth access", () => CommandOptions.Parse(["phone-control", "play"]));
Rejects("AMS control requires an explicit player", () => CommandOptions.Parse(["ams-control", "play"]));
Rejects("absolute seek is rejected before Bluetooth access", () => CommandOptions.Parse(["ams-control", "seek", "--expect-player", "test player"]));
Rejects("missing device name is rejected before startup", () => DesktopOptions.Parse(["overlay", "--device-name"]));
Rejects("invalid observation duration is rejected before Bluetooth access", () => CommandOptions.Parse(["ams-watch", "--seconds", "0"]));
Rejects("invalid display mode is rejected before startup", () => DesktopOptions.Parse(["overlay", "--mode", "invalid"]));

var anchor = AmsPlayback.Parse("1,1.0,185.197");
Check("AMS playback tuple parsed", anchor is { State: 1, Rate: 1, Elapsed: 185.197 });
Check("AMS elapsed time extrapolates at reported rate", Math.Abs(anchor!.Position(3)!.Value - 188.197) < .00001);
Check("AMS paused position never advances", AmsPlayback.Parse("0,0,185.197")!.Position(90) == 185.197);
Check("AMS unknown state rejected", AmsPlayback.Parse("4,1,10") == null);
Check("AMS NaN rejected", AmsPlayback.Parse("1,NaN,10") == null);
Check("AMS missing elapsed rejected", AmsPlayback.Parse("1,1") == null);
Check("AMS negative elapsed rejected", AmsPlayback.Parse("1,1,-1") == null);
Check("AMS reverse mode not extrapolated", AmsPlayback.Parse("2,-1,10")!.Position(1) == null);
Check("AMS overflow not displayed", new AmsPlayback(1, 1e300, 1).Position(1e300) == null);
var state = new AmsSnapshot("test phone", true, "test player", "line 1", "test artist", "test album", 262, "0", anchor, 100, DateTimeOffset.UtcNow, [0, 1], null);
Check("AMS disconnected clock is unavailable", (state with { Connected = false }).PositionAt(110) == null);
Check("AMS negative monotonic age is unavailable", state.PositionAt(99) == null);
Check("AMS position stops at duration", state.PositionAt(1000) == 262);
var lyric = AmsProtocol.Apply(state, 2, 2, "line 2", false, 110, DateTimeOffset.UtcNow);
Check("AMS lyric update preserves elapsed anchor", lyric.PrimaryText == "line 2" && lyric.Playback == anchor && lyric.AnchorMonotonicSeconds == 100);
Check("AMS queue change invalidates position", AmsProtocol.Apply(state, 1, 0, "1", false, 110, DateTimeOffset.UtcNow).PositionAt(110) == null);
Check("AMS artist change invalidates position", AmsProtocol.Apply(state, 2, 0, "other artist", false, 110, DateTimeOffset.UtcNow).PositionAt(110) == null);
Check("AMS player change invalidates position", AmsProtocol.Apply(state, 0, 0, "other player", false, 110, DateTimeOffset.UtcNow).PositionAt(110) == null);
Check("AMS malformed new anchor clears old position", AmsProtocol.Apply(state, 0, 1, "garbage", false, 110, DateTimeOffset.UtcNow).PositionAt(110) == null);
Check("AMS truncated tuple is not accepted", AmsProtocol.Apply(state, 0, 1, "1,1,10", true, 110, DateTimeOffset.UtcNow).PositionAt(110) == null);
var gap = TaskbarGeometry.ChooseGap(1000, [new(400, 600), new(800, 1000)], 300, 220);
Check("taskbar placement avoids centered icons and tray", gap is { Start: 50, End: 350 });
Check("taskbar placement handles overlapping occupied ranges", TaskbarGeometry.ChooseGap(1000, [new(0, 300), new(200, 600), new(800, 1000)], 300, 220) == null);
Check("taskbar placement refuses insufficient free space", TaskbarGeometry.ChooseGap(300, [new(50, 250)], 300, 220) == null);
Check("taskbar placement clips offscreen occupied geometry", TaskbarGeometry.ChooseGap(1000, [new(-200, 100), new(900, 1100)], 400, 220) is { Start: 300, End: 700 });
Check("taskbar placement shrinks only to available free space", TaskbarGeometry.ChooseGap(600, [new(0, 100), new(500, 600)], 600, 220) is { Start: 112, End: 488 });
Console.WriteLine($"{passed} checks passed. Pure logic only; no Bluetooth or phone control assertions.");
