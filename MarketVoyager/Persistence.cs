using System.Text.Json;
using System.Text.Json.Nodes;

namespace MarketVoyager;

// One synchronous writer, no timers/tasks. The framework owns the 60-second cadence.
internal sealed class Persistence : IDisposable
{
    private readonly object gate = new();
    private readonly Func<DateTimeOffset> clock;
    private readonly Action<Exception> report;
    private readonly Action<string, string>? beforeCommit;
    private readonly string settingsPath, safetyPath;
    private byte[]? savedSettings, savedSafety;
    private long revision, savedRevision;
    private DateTimeOffset nextSave;
    private bool disposed, writing;
    private bool unreadableSafety;
    public Configuration Config { get; }
    public bool Blocked { get; private set; }
    public bool CanRetry => Blocked && !unreadableSafety && !disposed;
    public string Status { get; private set; } = "";
    public string? RecoveryPath { get; private set; }
    public long Writes { get; private set; }
    public long WriteAttempts { get; private set; }
    public long PayloadBytesWritten { get; private set; }
    public long LogicalBytes { get; private set; }
    public string DirectoryPath { get; }

    public Persistence(string directory, Func<Configuration> legacy, Action<Exception> report,
        Func<DateTimeOffset>? clock = null, Action<string, string>? beforeCommit = null)
    {
        DirectoryPath = Path.GetFullPath(directory);
        settingsPath = Path.Combine(DirectoryPath, "settings-v2.json");
        safetyPath = Path.Combine(DirectoryPath, "safety-v2.json");
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.report = report; this.beforeCommit = beforeCommit;
        nextSave = this.clock().AddSeconds(60);
        try
        {
            Config = LoadSettings(legacy);
            if (File.Exists(safetyPath))
            {
                try
                {
                    savedSafety = File.ReadAllBytes(safetyPath);
                    ApplySafety(DecodeSafety(savedSafety));
                }
                catch (Exception ex) { unreadableSafety = Blocked = true; Status = "SafetyUnreadable"; report(ex); }
            }
            else if (savedSettings != null)
            {
                // A v2 settings file can only have been committed after its safety checkpoint.
                unreadableSafety = Blocked = true; Status = "SafetyUnreadable";
            }
            RecoveryPath = Directory.Exists(DirectoryPath)
                ? Directory.EnumerateFiles(DirectoryPath, "recovery-*.json").OrderDescending().FirstOrDefault()
                : null;
            if (RecoveryPath != null && Status.Length == 0) Status = "RecoveryAvailable";
            if (savedSettings == null) revision++;
        }
        catch (Exception ex)
        {
            Config = new(); unreadableSafety = Blocked = true; Status = "StorageUnreadable"; report(ex);
        }
    }

    private Configuration LoadSettings(Func<Configuration> legacy)
    {
        if (!File.Exists(settingsPath) && !Backups().Any()) return legacy();
        try
        {
            savedSettings = File.ReadAllBytes(settingsPath);
            return DecodeSettings(savedSettings);
        }
        catch (Exception ex)
        {
            report(ex); savedSettings = null;
            foreach (var path in Backups().OrderDescending())
                try { var result = DecodeSettings(File.ReadAllBytes(path)); Status = "BackupRecovered"; return result; }
                catch (Exception) { /* An invalid generation is never promoted. */ }
            throw new InvalidDataException("No valid settings generation");
        }
    }

    public void Changed() { lock (gate) { if (!disposed) revision++; } }
    public void Tick()
    {
        lock (gate)
        {
            if (disposed || Blocked || clock() < nextSave) return;
            nextSave = clock().AddSeconds(60); // Consume the schedule BEFORE attempting any I/O.
            if (revision != savedRevision) FlushCore();
        }
    }
    public bool Flush()
    {
        lock (gate) { return !disposed && !Blocked && (revision == savedRevision || FlushCore()); }
    }
    public void Durable()
    {
        lock (gate)
        {
            if (disposed || Blocked || writing) throw new IOException("Persistence unavailable; transaction must stop");
            revision++;
            if (!FlushCore(settingsAllowed: false)) throw new IOException("Transaction checkpoint not committed; do not repeat purchase");
        }
    }
    public bool Retry()
    {
        lock (gate)
        {
            if (disposed || unreadableSafety || writing) return false;
            Blocked = false; revision++;
            bool ok = FlushCore();
            if (ok) Status = "SaveRecovered";
            return ok;
        }
    }
    public bool RestoreCandidate()
    {
        lock (gate)
        {
            if (disposed || Blocked || writing || unreadableSafety || RecoveryPath == null) return false;
            try
            {
                var candidate = JsonSerializer.Deserialize<Recovery>(File.ReadAllBytes(RecoveryPath))
                    ?? throw new InvalidDataException("Empty recovery snapshot");
                var restored = DecodeSettings(candidate.Settings);
                // A fallback must not replace a newer, durable transaction checkpoint.
                var safety = savedSafety == null ? DecodeSafety(candidate.Safety) : DecodeSafety(savedSafety);
                ApplySafety(safety, restored);
                Config.Language = restored.Language; Config.PurchaseDelayMs = restored.PurchaseDelayMs;
                Config.Lists = restored.Lists; Config.Selected = restored.Selected;
                Config.Pending = restored.Pending; Config.PendingSale = restored.PendingSale; Config.DetachedRules = restored.DetachedRules;
                Blocked = false; revision++;
                bool applied = FlushCore();
                if (applied) Status = "SaveRecovered";
                return applied;
            }
            catch (Exception ex) { report(ex); Status = "RecoveryInvalid"; return false; }
        }
    }

    private bool FlushCore(bool settingsAllowed = true)
    {
        if (writing) return false;
        writing = true;
        byte[]? settings = null, safety = null;
        long capturedRevision = revision;
        try
        {
            settings = SettingsBytes(Config); safety = JsonSerializer.SerializeToUtf8Bytes(CaptureSafety());
            DecodeSettings(settings); DecodeSafety(safety);
            // Safety first: old settings restored after a crash still use current counters,
            // membership and pending reservations. No UI transaction is dispatched before both succeed.
            if (savedSafety == null || !safety.AsSpan().SequenceEqual(savedSafety))
            { Write(safetyPath, safety, false); savedSafety = safety; }
            if (settingsAllowed && (savedSettings == null || !settings.AsSpan().SequenceEqual(savedSettings)))
            { Write(settingsPath, settings, true); savedSettings = settings; }
            if (settingsAllowed) savedRevision = capturedRevision;
            return true;
        }
        catch (Exception ex)
        {
            Blocked = true; report(ex); Status = "SaveFailedBoth";
            // One alternate-name attempt, identical captured data. No automatic retry, even on exit.
            if (settings != null && safety != null)
                try
                {
                    var path = Path.Combine(DirectoryPath, $"recovery-{clock():yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.json");
                    Write(path, JsonSerializer.SerializeToUtf8Bytes(new Recovery(settings, safety)), false);
                    RecoveryPath = path; Status = "SaveFailedFallback";
                }
                catch (Exception alternate) { report(alternate); }
            return false;
        }
        finally { writing = false; }
    }

    private IEnumerable<string> Backups() => Directory.Exists(DirectoryPath)
        ? Directory.EnumerateFiles(DirectoryPath, "settings-backup-*.json") : [];
    private void Write(string path, byte[] bytes, bool backup)
    {
        WriteAttempts++;
        Directory.CreateDirectory(DirectoryPath);
        string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            beforeCommit?.Invoke(path, "write");
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); PayloadBytesWritten += bytes.Length; stream.Flush(true); }
            var verified = File.ReadAllBytes(temp);
            if (!verified.AsSpan().SequenceEqual(bytes)) throw new IOException("Temporary payload mismatch");
            using (JsonDocument.Parse(verified)) { }
            beforeCommit?.Invoke(path, "replace");
            string? previous = null;
            if (File.Exists(path))
            {
                if (backup)
                {
                    bool valid;
                    try { DecodeSettings(File.ReadAllBytes(path)); valid = true; } catch { valid = false; }
                    long sequence = Math.Max(clock().UtcTicks, Backups().Select(x => long.TryParse(Path.GetFileName(x).Split('-')[2].Split('.')[0], out var n) ? n + 1 : 0).DefaultIfEmpty().Max());
                    previous = Path.Combine(DirectoryPath, valid ? $"settings-backup-{sequence:D20}.json" : $"settings-corrupt-{Guid.NewGuid():N}.json");
                }
                File.Replace(temp, path, previous);
            }
            else File.Move(temp, path);
            Writes++; LogicalBytes += bytes.Length;
            // Rotation only after a successful primary commit. Never copy all generations.
            if (backup)
            {
                foreach (var old in Backups().ToArray())
                {
                    bool valid;
                    try { DecodeSettings(File.ReadAllBytes(old)); valid = true; } catch { valid = false; }
                    if (!valid) File.Move(old, Path.Combine(DirectoryPath, $"settings-corrupt-{Guid.NewGuid():N}.json"));
                }
                foreach (var old in Backups().OrderDescending().Skip(3)) File.Delete(old);
            }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static byte[] SettingsBytes(Configuration config)
    {
        var node = JsonSerializer.SerializeToNode(config)!.AsObject();
        node.Remove("Pending"); node.Remove("PendingSale"); node.Remove("DetachedRules");
        foreach (var list in node["Lists"]!.AsArray())
        {
            list!.AsObject().Remove("WorldHistory");
            foreach (var rule in list["Items"]!.AsArray())
            { rule!.AsObject().Remove("Purchased"); rule.AsObject().Remove("Stock"); }
        }
        return JsonSerializer.SerializeToUtf8Bytes(node);
    }
    private static Configuration DecodeSettings(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        if (!doc.RootElement.TryGetProperty("Lists", out _) || !doc.RootElement.TryGetProperty("Version", out _)) throw new InvalidDataException("Settings schema missing");
        var c = JsonSerializer.Deserialize<Configuration>(bytes) ?? throw new InvalidDataException("Empty settings");
        if (c.Lists == null || c.Lists.Count == 0 || c.Lists.Count > 50 || c.Lists.Any(x => x.Items == null || x.Items.Count > 1000)) throw new InvalidDataException("Invalid lists");
        if (c.Lists.Select(x => x.Id).Distinct().Count() != c.Lists.Count || c.Lists.SelectMany(x => x.Items).GroupBy(x => x.Id).Any(x => x.Count() > 1)) throw new InvalidDataException("Duplicate IDs");
        c.Selected = Math.Clamp(c.Selected, 0, c.Lists.Count - 1);
        return c;
    }
    private Safety CaptureSafety() => new()
    {
        RuleIds = Config.Lists.SelectMany(x => x.Items).Select(x => x.Id).Order().ToArray(),
        Rules = Config.Lists.SelectMany(x => x.Items).Concat(Config.DetachedRules)
            .Where(x => x.Purchased != 0 || x.Stock.Count != 0 || Config.HasPending(x.Id))
            .OrderBy(x => x.Id).Select(x => new Ledger(x.Id, x.ItemId, x.Purchased, x.Stock)).ToArray(),
        Pending = Config.Pending, PendingSale = Config.PendingSale,
        Histories = Config.Lists.Where(x => x.WorldHistory.Count != 0).OrderBy(x => x.Id).ToDictionary(x => x.Id, x => x.WorldHistory)
    };
    private static Safety DecodeSafety(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (!document.RootElement.TryGetProperty("Schema", out _) || !document.RootElement.TryGetProperty("RuleIds", out _) ||
            !document.RootElement.TryGetProperty("Rules", out _) || !document.RootElement.TryGetProperty("Histories", out _))
            throw new InvalidDataException("Safety schema missing");
        var s = JsonSerializer.Deserialize<Safety>(bytes) ?? throw new InvalidDataException("Empty safety checkpoint");
        if (s.Schema != 1 || s.RuleIds == null || s.Rules == null || s.Histories == null || s.RuleIds.Length > 50000 || s.Rules.Length > 50002 ||
            s.RuleIds.Distinct().Count() != s.RuleIds.Length || s.Rules.Select(x => x.Id).Distinct().Count() != s.Rules.Length ||
            s.Rules.Any(x => x.Purchased < 0 || x.Stock == null || x.Stock.Any(y => y.Remaining < 0)) ||
            s.Histories.Count > 50 || s.Histories.Values.Any(x => x == null || x.Count > 512)) throw new InvalidDataException("Invalid safety checkpoint");
        if (s.Pending != null && !s.Rules.Any(x => x.Id == s.Pending.RuleId) || s.PendingSale != null && !s.Rules.Any(x => x.Id == s.PendingSale.RuleId)) throw new InvalidDataException("Pending ledger missing");
        return s;
    }
    private void ApplySafety(Safety safety, Configuration? target = null)
    {
        var config = target ?? Config;
        var ids = safety.RuleIds.ToHashSet(); var ledgers = safety.Rules.ToDictionary(x => x.Id);
        config.Pending = safety.Pending; config.PendingSale = safety.PendingSale; config.DetachedRules = [];
        foreach (var list in config.Lists)
        {
            list.Items.RemoveAll(x => !ids.Contains(x.Id));
            list.WorldHistory = safety.Histories.GetValueOrDefault(list.Id) ?? [];
            foreach (var rule in list.Items)
            {
                if (ledgers.TryGetValue(rule.Id, out var state))
                {
                    if (rule.ItemId != state.ItemId) throw new InvalidDataException("Safety item identity changed");
                    rule.Purchased = state.Purchased; rule.Stock = state.Stock;
                }
                else { rule.Purchased = 0; rule.Stock = []; }
            }
        }
        foreach (var row in safety.Rules.Where(x => config.HasPending(x.Id) && config.FindRule(x.Id) == null))
            config.DetachedRules.Add(new() { Id = row.Id, ItemId = row.ItemId, Purchased = row.Purchased, Stock = row.Stock });
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            if (!Blocked && revision != savedRevision) FlushCore();
            disposed = true;
        }
    }
    private sealed record Recovery(byte[] Settings, byte[] Safety);
    private sealed record Ledger(Guid Id, uint ItemId, int Purchased, List<AcquiredStock> Stock);
    private sealed class Safety
    {
        public int Schema { get; set; } = 1;
        public Guid[] RuleIds { get; set; } = [];
        public Ledger[] Rules { get; set; } = [];
        public Reservation? Pending { get; set; }
        public SaleReservation? PendingSale { get; set; }
        public Dictionary<Guid, Dictionary<uint, WorldVisit>> Histories { get; set; } = [];
    }
}
