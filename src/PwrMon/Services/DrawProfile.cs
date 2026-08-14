using System.IO;
using System.Text.Json;

namespace PwrMon.Services;

/// <summary>
/// This machine's own discharge-draw distribution, learned by watching it.
///
/// <para><b>Why.</b> The tray icon used to turn red above a flat 60 W. That number is 0.86C on
/// the reference machine's 70 Wh pack — "about an hour of runtime left", worked out once by
/// hand and then hard-coded as watts. It fails in both directions on anything else: a
/// tablet-class machine peaking at 15 W never reaches it, so red never fires; a gaming laptop
/// idling at 30 W and loading to 150 W sits red permanently. No single constant survives that
/// spread, and no constant can be derived from the hardware either — nothing predicts what a
/// machine *can* draw without watching it draw.</para>
///
/// <para><b>What this is.</b> A time-weighted histogram of observed discharge, in 1 W bins.
/// A high percentile of it answers "is this draw unusual <i>for this machine</i>" from
/// evidence rather than assumption. Time-weighted rather than sample-counted so that changing
/// the sampling interval doesn't reweight history — a bin holds seconds spent at that draw,
/// which is the quantity that actually means something.</para>
///
/// <para><b>What it deliberately isn't.</b> Not a runtime warning. "You have 20 minutes left"
/// is a different signal, it comes from capacity rather than history, and conflating the two
/// into one icon colour would make the colour mean neither. See ROADMAP.md.</para>
/// </summary>
public sealed class DrawProfile
{
    /// <summary>1 W bins covering 0–199 W. Anything above lands in the top bin: a draw that
    /// high is already past every percentile we ask about, so its exact value can't change an
    /// answer.</summary>
    public const int BinCount = 200;

    /// <summary>Battery-seconds of observation before the learned percentiles are trusted.
    /// 30 minutes is enough for a distribution with a usable shoulder; below it a single
    /// compile or video call is the entire history and the percentile is just that spike.</summary>
    public const double MinSecondsToTrust = 1800;

    /// <summary>Total observation is halved on reaching this, giving the profile a rolling
    /// ~100-battery-hour half-life. Usage patterns change and packs age; a profile that
    /// averages in last year's behaviour forever would answer a question nobody asked.</summary>
    public const double DecayAtSeconds = 360_000;

    private readonly double[] _binSeconds = new double[BinCount];
    private double _totalSeconds;
    private DateTime _lastSaved = DateTime.UtcNow;
    private bool _dirty;

    /// <summary>Battery-seconds observed so far.</summary>
    public double TotalSeconds => _totalSeconds;

    /// <summary>True once there's enough observation to prefer the learned thresholds over the
    /// capacity-derived fallback.</summary>
    public bool IsLearned => _totalSeconds >= MinSecondsToTrust;

    /// <summary>Record <paramref name="seconds"/> spent drawing <paramref name="watts"/>.
    /// Caller is responsible for only feeding genuine off-AC discharge — draining while plugged
    /// in is a lower bound on an abnormal state, not a sample of normal behaviour.</summary>
    public void Add(double watts, double seconds)
    {
        if (seconds <= 0 || double.IsNaN(watts) || double.IsInfinity(watts) || watts < 0) return;

        _binSeconds[BinOf(watts)] += seconds;
        _totalSeconds += seconds;
        _dirty = true;

        if (_totalSeconds >= DecayAtSeconds) Decay();
    }

    /// <summary>Bin index for a draw, saturating at the top bin.</summary>
    public static int BinOf(double watts) => Math.Clamp((int)watts, 0, BinCount - 1);

    /// <summary>The draw this machine stays below for <paramref name="q"/> of its battery time,
    /// or null before there's anything to say. Bin upper edge, so the answer is the watts the
    /// machine has been observed to reach rather than the floor it started from.</summary>
    public double? Percentile(double q) => PercentileOf(_binSeconds, _totalSeconds, q);

    /// <summary>Pure percentile over a time-weighted histogram, split out to be testable
    /// without an instance or a file.</summary>
    public static double? PercentileOf(IReadOnlyList<double> binSeconds, double totalSeconds, double q)
    {
        if (totalSeconds <= 0) return null;

        var target = totalSeconds * Math.Clamp(q, 0, 1);
        double cumulative = 0;
        for (var i = 0; i < binSeconds.Count; i++)
        {
            cumulative += binSeconds[i];
            if (cumulative >= target) return i + 1;
        }
        return binSeconds.Count;
    }

    /// <summary>Halve everything, preserving shape while making room for newer observation.</summary>
    private void Decay()
    {
        for (var i = 0; i < _binSeconds.Length; i++) _binSeconds[i] *= 0.5;
        _totalSeconds *= 0.5;
        Log.Info($"draw profile decayed, now {_totalSeconds:F0} battery-seconds");
    }

    // ---- persistence ----
    //
    // Its own file rather than a corner of settings.json: this is observed data, not a
    // preference, and 200 bins would drown the file a user is invited to hand-edit. Stored
    // sparse (only non-empty bins) because a machine occupies a few dozen of them.

    private static string FilePath => Path.Combine(Models.AppSettings.Dir, "drawprofile.json");

    private sealed class Dto
    {
        public int Version { get; set; } = 1;
        public double TotalSeconds { get; set; }
        public Dictionary<string, double> BinSeconds { get; set; } = new();
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(FilePath));
            if (dto is null || dto.Version != 1) return;

            Array.Clear(_binSeconds);
            _totalSeconds = 0;
            foreach (var (key, seconds) in dto.BinSeconds)
            {
                if (!int.TryParse(key, out var bin) || bin < 0 || bin >= BinCount) continue;
                if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) continue;
                _binSeconds[bin] += seconds;
                _totalSeconds += seconds;
            }
            Log.Info($"draw profile loaded: {_totalSeconds:F0} battery-seconds, learned={IsLearned}");
        }
        catch (Exception ex)
        {
            // a corrupt profile is worth nothing and costs nothing — start over rather than
            // failing a startup over a statistics cache
            Log.Error($"draw profile load failed, starting fresh: {ex.Message}");
            Array.Clear(_binSeconds);
            _totalSeconds = 0;
        }
    }

    /// <summary>Write at most every two minutes, matching the learned-baseline cadence — this
    /// updates every tick and is worth nothing if lost.</summary>
    public void SaveIfDue()
    {
        if (!_dirty || (DateTime.UtcNow - _lastSaved).TotalSeconds < 120) return;
        Save();
    }

    public void Save()
    {
        if (!_dirty) return;
        try
        {
            Directory.CreateDirectory(Models.AppSettings.Dir);
            var dto = new Dto { TotalSeconds = _totalSeconds };
            for (var i = 0; i < _binSeconds.Length; i++)
                if (_binSeconds[i] > 0) dto.BinSeconds[i.ToString()] = Math.Round(_binSeconds[i], 2);

            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, overwrite: true);
            _lastSaved = DateTime.UtcNow;
            _dirty = false;
        }
        catch (Exception ex)
        {
            Log.Error($"draw profile save failed: {ex.Message}");
        }
    }
}
