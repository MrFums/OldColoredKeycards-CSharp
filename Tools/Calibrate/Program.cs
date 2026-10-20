using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Calibrates the restored 0.14 keycard spawn points for OldColoredKeycards.
// args: oldDb (spt398 folder: <map>/looseLoot.json, bots/*.json, location.json)  newDb (SPT_Data/database)  newLocationJson  outDir
//
// Model of SPT's loose loot draw (identical in 3.9.8 LocationGenerator.ts and 4.1 LocationLootGenerator.cs):
//   N = round(looseLootMultiplier * Normal(mean, std))  (negative draws re-rolled), minus spots with probability exactly 1;
//   then N spots are drawn by weight (probability) without replacement; IsAlwaysSpawn / probability-1 spots always spawn.
// Weighted draw without replacement == "exponential race": spot j has key Exp(1)/w_j, the N smallest keys win.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

string oldDb = args[0], newDb = args[1], outDir = args[3];
var oldLocCfg = JsonNode.Parse(File.ReadAllText(Path.Combine(oldDb, "location.json")))!;
var newLocCfg = JsonNode.Parse(File.ReadAllText(args[2]))!;
Directory.CreateDirectory(outDir);

var colours = new (string Id, string Name)[] {
    ("5c1d0c5f86f7744bb2683cf0","Blue"), ("5c1d0efb86f7744baf2e7b7b","Red"), ("5c1d0dc586f7744baf2e7b79","Green"),
    ("5c1d0f4986f7744bb01837fa","Black"), ("5c1e495a86f7743109743dfb","Violet"), ("5c1d0d6d86f7744bb2683e1f","Yellow") };
var keycardIds = colours.Select(c => c.Id).ToHashSet();
string ColourName(string tpl) => colours.First(c => c.Id == tpl).Name;

const int Samples = 2500;
var maps = new[] { "bigmap","factory4_day","factory4_night","interchange","laboratory","lighthouse","rezervbase","sandbox","sandbox_high","shoreline","tarkovstreets","woods" };

var looseOut = new ConcurrentDictionary<string, JsonArray>();
var targetsOut = new ConcurrentDictionary<string, JsonObject>();
var report = new ConcurrentDictionary<string, string>();
var mapRows = new ConcurrentDictionary<string, (Dictionary<string, double> Target, Dictionary<string, double> Now, Dictionary<string, double> After)>();

Parallel.ForEach(maps, new ParallelOptions { MaxDegreeOfParallelism = 4 }, map =>
{
    var rng = new Random(map.GetHashCode() ^ 0x5eed);
    double mult = newLocCfg["looseLootMultiplier"]![map]?.GetValue<double>() ?? newLocCfg["looseLootMultiplier"]!["default"]!.GetValue<double>();
    double oldMult = oldLocCfg["looseLootMultiplier"]![map]?.GetValue<double>() ?? mult;
    var log = new StringBuilder();

    // ---- 0.14: how many keycards each old spot produced per raid ----
    var old = LoadLoot(Path.Combine(oldDb, map, "looseLoot.json"), newFormat: false, Blacklist(oldLocCfg, map));
    var oldKeySpots = old.Spots.Where(s => s.Keycards.Count > 0).ToList();
    var oldPick = PickChances(old, oldMult, oldKeySpots, Array.Empty<double>(), rng).Watched;
    var target = colours.ToDictionary(c => c.Id, _ => 0.0);
    var spotTargets = new List<(Spot Spot, double Total)>();
    for (int i = 0; i < oldKeySpots.Count; i++)
    {
        var s = oldKeySpots[i];
        double total = 0;
        foreach (var (tpl, rp) in s.Keycards) { var e = oldPick[i] * rp / s.TotalRp; target[tpl] += e; total += e; }
        spotTargets.Add((s, total));
    }

    // ---- 4.1 as it is now ----
    var now = LoadLoot(Path.Combine(newDb, "locations", map, "looseLoot.json"), newFormat: true, Blacklist(newLocCfg, map));
    var nowKeySpots = now.Spots.Where(s => s.Keycards.Count > 0).ToList();
    var nowPick = PickChances(now, mult, nowKeySpots, Array.Empty<double>(), rng).Watched;
    var current = colours.ToDictionary(c => c.Id, _ => 0.0);
    for (int i = 0; i < nowKeySpots.Count; i++)
        foreach (var (tpl, rp) in nowKeySpots[i].Keycards) current[tpl] += nowPick[i] * rp / nowKeySpots[i].TotalRp;

    // ---- 4.1 with keycards removed, plus keycard-only old spots ----
    var cleaned = now with { Spots = now.Spots.Where(s => s.TotalRp - s.Keycards.Values.Sum() > 0 || s.Guaranteed || s.Always).ToList() };
    var weights = new double[spotTargets.Count];
    for (int iteration = 0; iteration < 3; iteration++)
    {
        var thresholds = PickChances(cleaned, mult, new List<Spot>(), weights, rng).Thresholds;
        for (int i = 0; i < spotTargets.Count; i++) weights[i] = SolveWeight(thresholds, spotTargets[i].Total);
    }

    // Independent check: fresh draws with the new spots in the pool.
    var check = PickChances(cleaned, mult, new List<Spot>(), weights, new Random(rng.Next()));
    var after = colours.ToDictionary(c => c.Id, _ => 0.0);
    var spawnpoints = new JsonArray();
    var targets = new JsonObject();
    for (int i = 0; i < spotTargets.Count; i++)
    {
        var (s, total) = spotTargets[i];
        var keycardRp = s.Keycards.Values.Sum();
        foreach (var (tpl, rp) in s.Keycards) after[tpl] += check.NewSpotPicks[i] * rp / keycardRp;
        if (weights[i] <= 0) continue;
        var spawnpoint = BuildSpawnpoint(s, weights[i], rng);
        spawnpoints.Add(spawnpoint);
        // The mod re-solves the weight against each server's real pool; this is what it aims for.
        targets[spawnpoint["template"]!["Id"]!.GetValue<string>()] = total;
        log.AppendLine($"  {s.TemplateId,-60} target {total:F5}/raid  weight {weights[i]:G4}  check {check.NewSpotPicks[i]:F5}");
    }

    looseOut[map] = spawnpoints;
    targetsOut[map] = targets;
    mapRows[map] = (target, current, after);
    report[map] = $"[{map}] {oldKeySpots.Count} old keycard spots, {nowKeySpots.Count} current keycard spots, mult {mult}\n" + log;
});

// ---- bots ----
var botMap = new Dictionary<string, string?> {
    ["bossboar"] = "bossboar", ["bossbully"] = "bossbully", ["bossgluhar"] = "bossgluhar", ["bosskilla"] = "bosskilla",
    ["bosskojaniy"] = "bosskojaniy", ["bosssanitar"] = "bosssanitar", ["bosstagilla"] = "bosstagilla", ["bossknight"] = "bossknight",
    ["followerbigpipe"] = "followerbigpipe", ["followerbirdeye"] = "followerbirdeye", ["sectantpriest"] = "sectantpriest", ["sectantwarrior"] = "sectantwarrior",
    // variants of 0.14 bosses use their base boss
    ["bosskillaagro"] = "bosskilla", ["bosstagillaagro"] = "bosstagilla", ["infectedtagilla"] = "bosstagilla",
    // new since 0.14, nothing to restore: left as they are (Partisan uses the boss average below)
    ["sectantoni"] = null, ["sectantprizrak"] = null };
// Bots with no 0.14 version that should still get 0.14-style boss odds: the average 0.14 pocket share
// of these bosses, applied by the mod to every slot that holds keycards. BlackDiv bots come from a mod.
var averageOf = new[] { "bossboar", "bossgluhar", "bosskilla", "bosstagilla", "bossknight" };
var averageFor = new[] { "bosspartisan", "blackdivassault", "blackdivbreacher", "blackdivib", "blackdivlead", "blackdivsupport" };
var slots = new[] { "Pockets", "Backpack", "TacticalVest", "SecuredContainer", "SpecialLoot" };
var botsOut = new JsonObject();
var botReport = new StringBuilder();
foreach (var (botType, source) in botMap)
{
    if (source is null) { botReport.AppendLine($"  {botType}: new since 0.14, unchanged"); continue; }
    var newItems = JsonNode.Parse(File.ReadAllText(Path.Combine(newDb, "bots", "types", botType + ".json")))!["inventory"]!["items"]!;
    var oldItems = JsonNode.Parse(File.ReadAllText(Path.Combine(oldDb, "bots", source + ".json")))!["inventory"]!["items"]!;
    var botObj = new JsonObject();
    foreach (var slot in slots)
    {
        var newSlot = newItems[slot]?.AsObject(); var oldSlot = oldItems[slot]?.AsObject();
        bool newHas = newSlot != null && newSlot.Any(p => keycardIds.Contains(p.Key));
        bool oldHas = oldSlot != null && oldSlot.Any(p => keycardIds.Contains(p.Key));
        // Only slots that carry keycards today: a variant boss must not gain keycards its 4.1 slot never had.
        if (!newHas) continue;
        double oldTotal = oldSlot?.Sum(p => p.Value!.GetValue<double>()) ?? 0;
        var shares = new JsonObject();
        foreach (var (id, name) in colours)
        {
            double share = oldSlot != null && oldSlot.TryGetPropertyValue(id, out var w) && oldTotal > 0 ? w!.GetValue<double>() / oldTotal : 0;
            shares[id] = share;
        }
        botObj[slot] = shares;
        double newTotal = newSlot?.Sum(p => p.Value!.GetValue<double>()) ?? 0;
        botReport.AppendLine($"  {botType} {slot} (from {source}): " + string.Join(" ", colours.Select(c =>
        {
            double nowShare = newSlot != null && newSlot.TryGetPropertyValue(c.Id, out var nw) && newTotal > 0 ? nw!.GetValue<double>() / newTotal : 0;
            return $"{c.Name} {nowShare:P4}->{shares[c.Id]!.GetValue<double>():P4}";
        })));
    }
    if (botObj.Count > 0) botsOut[botType] = botObj;
}

var bossAverage = new JsonObject();
foreach (var (id, name) in colours)
{
    var shares = averageOf.Select(boss =>
    {
        var pockets = JsonNode.Parse(File.ReadAllText(Path.Combine(oldDb, "bots", boss + ".json")))!["inventory"]!["items"]!["Pockets"]!.AsObject();
        var total = pockets.Sum(p => p.Value!.GetValue<double>());
        return pockets.TryGetPropertyValue(id, out var w) ? w!.GetValue<double>() / total : 0;
    }).ToList();
    bossAverage[id] = shares.Average();
}
botReport.AppendLine($"  boss average (for {string.Join(", ", averageFor)}): " + string.Join(" ", colours.Select(c => $"{c.Name} {bossAverage[c.Id]!.GetValue<double>():P4}")));

// ---- outputs ----
var data = new JsonObject
{
    ["source"] = "SPT 3.9.8 database (EFT 0.14.9.30626), calibrated against SPT 4.1.6 loose loot",
    ["looseLoot"] = new JsonObject(maps.Where(looseOut.ContainsKey).Select(m => KeyValuePair.Create(m, (JsonNode?)looseOut[m]))),
    ["spotTargets"] = new JsonObject(maps.Where(targetsOut.ContainsKey).Select(m => KeyValuePair.Create(m, (JsonNode?)targetsOut[m]))),
    ["bots"] = botsOut,
    ["bossAverage"] = bossAverage,
    ["bossAverageBots"] = new JsonArray(averageFor.Select(b => (JsonNode?)b).ToArray())
};
File.WriteAllText(Path.Combine(outDir, "old-keycard-spawns.json"), data.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));

var md = new StringBuilder();
md.AppendLine("| Map | Keycard | 0.14 target | 4.1 now | With mod (model) |");
md.AppendLine("|---|---|---|---|---|");
foreach (var map in maps)
{
    var (t, n, a) = mapRows[map];
    foreach (var (id, name) in colours)
        if (t[id] + n[id] + a[id] > 0.00001)
            md.AppendLine($"| {map} | {name} | {t[id]:F4} | {n[id]:F4} | {a[id]:F4} |");
}
File.WriteAllText(Path.Combine(outDir, "targets.md"), md.ToString());

foreach (var map in maps) Console.Write(report[map]);
Console.WriteLine("\nPER RAID, ALL COLOURS:   map              0.14 target   4.1 now   with mod (model)");
foreach (var map in maps) { var (t, n, a) = mapRows[map]; Console.WriteLine($"  {map,-16} {t.Values.Sum(),12:F4} {n.Values.Sum(),9:F4} {a.Values.Sum(),12:F4}"); }
Console.WriteLine("\nBOTS (share of slot pool, now -> 0.14):");
Console.Write(botReport);

// ================= helpers =================

static HashSet<string> Blacklist(JsonNode cfg, string map) =>
    cfg["looseLootBlacklist"]?[map]?.AsArray().Select(x => x!.GetValue<string>()).ToHashSet() ?? new HashSet<string>();

Loot LoadLoot(string path, bool newFormat, HashSet<string> blacklist)
{
    var root = JsonNode.Parse(File.ReadAllText(path))!;
    var spots = new List<Spot>();
    foreach (var sp in root["spawnpoints"]!.AsArray())
    {
        var t = sp!["template"]!;
        var keyToTpl = new Dictionary<string, string>();
        foreach (var it in t["Items"]!.AsArray())
        {
            var key = newFormat && it!["composedKey"] is JsonValue ck ? ck.GetValue<string>() : it!["_id"]!.GetValue<string>();
            keyToTpl[key] = it["_tpl"]!.GetValue<string>();
        }
        double total = 0; var kc = new Dictionary<string, double>(); var kcKeys = new Dictionary<string, string>();
        foreach (var dist in sp["itemDistribution"]!.AsArray())
        {
            var rp = dist!["relativeProbability"]!.GetValue<double>(); total += rp;
            var key = dist["composedKey"]!["key"]!.GetValue<string>();
            if (keyToTpl.TryGetValue(key, out var tpl) && keycardIds.Contains(tpl)) { kc[tpl] = kc.GetValueOrDefault(tpl) + rp; kcKeys[key] = tpl; }
        }
        var id = t["Id"]!.GetValue<string>();
        spots.Add(new Spot
        {
            LocationId = sp["locationId"]!.GetValue<string>(), TemplateId = id, Node = sp,
            Prob = sp["probability"]!.GetValue<double>(),
            Always = t["IsAlwaysSpawn"]?.GetValue<bool>() == true,
            Blacklisted = blacklist.Contains(id),
            Keycards = kc, KeycardKeys = kcKeys, TotalRp = total
        });
    }
    var sc = root["spawnpointCount"]!;
    return new Loot(sc["mean"]!.GetValue<double>(), sc["std"]!.GetValue<double>(), spots);
}

// Returns the chance each watched spot is picked, the threshold per sample, and the pick chance of extra keycard-only spots.
(double[] Watched, double[] Thresholds, double[] NewSpotPicks) PickChances(Loot loot, double mult, List<Spot> watch, double[] extraWeights, Random rng)
{
    var pool = loot.Spots.Where(s => !s.Blacklisted && !s.Always && !s.Guaranteed).ToList();
    int guaranteed = loot.Spots.Count(s => !s.Blacklisted && !s.Always && s.Guaranteed);
    var weights = pool.Select(s => s.Prob).Concat(extraWeights.Where(w => w > 0)).ToArray();
    var extraIndex = new int[extraWeights.Length]; int next = pool.Count;
    for (int i = 0; i < extraWeights.Length; i++) extraIndex[i] = extraWeights[i] > 0 ? next++ : -1;
    var watchIndex = watch.Select(s => pool.IndexOf(s)).ToArray();

    var watchedHits = new double[watch.Count]; var extraHits = new double[extraWeights.Length];
    var thresholds = new double[Samples];
    var keys = new double[weights.Length]; var scratch = new double[weights.Length];
    for (int sample = 0; sample < Samples; sample++)
    {
        int n = (int)Math.Round(mult * Normal(loot.Mean, loot.Std, rng)) - guaranteed;
        for (int j = 0; j < weights.Length; j++) keys[j] = weights[j] > 0 ? -Math.Log(1.0 - rng.NextDouble()) / weights[j] : double.PositiveInfinity;
        double threshold;
        if (n <= 0) threshold = -1;
        else if (n >= weights.Length) threshold = double.PositiveInfinity;
        else { Array.Copy(keys, scratch, keys.Length); threshold = QuickSelect(scratch, n - 1); }
        thresholds[sample] = threshold;
        for (int w = 0; w < watch.Count; w++)
        {
            var s = watch[w];
            if (s.Blacklisted) continue;
            if (s.Always || s.Guaranteed) { watchedHits[w]++; continue; }
            // The spot's exact pick chance at this threshold, not a 0/1 hit: rare spots would otherwise
            // often score 0 by chance and be dropped.
            watchedHits[w] += PickAt(s.Prob, threshold);
        }
        for (int e = 0; e < extraWeights.Length; e++) if (extraIndex[e] >= 0) extraHits[e] += PickAt(extraWeights[e], threshold);
    }
    return (watchedHits.Select(h => h / Samples).ToArray(), thresholds, extraHits.Select(h => h / Samples).ToArray());
}

// Chance a spot of this weight is among the drawn ones, given the sample's threshold.
static double PickAt(double weight, double threshold) =>
    threshold < 0 ? 0 : double.IsPositiveInfinity(threshold) ? 1 : 1 - Math.Exp(-weight * threshold);

// Weight w so that the average over samples of P(Exp(1)/w <= T) = 1 - exp(-w T) equals the target.
static double SolveWeight(double[] thresholds, double target)
{
    if (target <= 0) return 0;
    double Pick(double w) => thresholds.Average(t => t < 0 ? 0 : double.IsPositiveInfinity(t) ? 1 : 1 - Math.Exp(-w * t));
    double lo = 0, hi = 1e-6;
    while (Pick(hi) < target && hi < 1e6) hi *= 2;
    for (int i = 0; i < 60; i++) { var mid = (lo + hi) / 2; if (Pick(mid) < target) lo = mid; else hi = mid; }
    return (lo + hi) / 2;
}

static double Normal(double mean, double std, Random rng)
{
    for (int attempt = 0; attempt <= 100; attempt++)
    {
        double u = 0, v = 0;
        while (u == 0) u = rng.NextDouble();
        while (v == 0) v = rng.NextDouble();
        var x = mean + Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v) * std;
        if (x >= 0) return x;
    }
    return 0.01 + rng.NextDouble() * (mean * 2 - 0.01);
}

static double QuickSelect(double[] a, int k)
{
    int lo = 0, hi = a.Length - 1;
    var r = new Random(k);
    while (true)
    {
        if (lo == hi) return a[lo];
        var pivot = a[lo + r.Next(hi - lo + 1)];
        int i = lo, j = hi;
        while (i <= j)
        {
            while (a[i] < pivot) i++;
            while (a[j] > pivot) j--;
            if (i <= j) { (a[i], a[j]) = (a[j], a[i]); i++; j--; }
        }
        if (k <= j) hi = j; else if (k >= i) lo = i; else return a[k];
    }
}

// A keycard-only copy of a 0.14 spawn point, in SPT 4.1 looseLoot.json format.
JsonNode BuildSpawnpoint(Spot s, double weight, Random rng)
{
    var oldTemplate = s.Node["template"]!;
    var template = JsonNode.Parse(oldTemplate.ToJsonString())!.AsObject();
    var guid = new Guid(Enumerable.Range(0, 16).Select(_ => (byte)rng.Next(256)).ToArray());
    var baseName = s.TemplateId.Contains(" [") ? s.TemplateId[..s.TemplateId.LastIndexOf(" [", StringComparison.Ordinal)] : s.TemplateId;
    template["Id"] = $"{baseName} [{guid}]";
    template["IsAlwaysSpawn"] = false;

    var items = new JsonArray(); var dist = new JsonArray(); string? root = null;
    foreach (var d in s.Node["itemDistribution"]!.AsArray())
    {
        var key = d!["composedKey"]!["key"]!.GetValue<string>();
        if (!s.KeycardKeys.TryGetValue(key, out var tpl)) continue;
        // 0.14 item IDs aren't always valid 24-hex MongoIds (some are hashes like "1785089740"), which 4.1 rejects.
        // Each item gets a new ID; the old key only links the item to its distribution entry.
        var id = string.Concat(Enumerable.Range(0, 12).Select(_ => rng.Next(256).ToString("x2")));
        root ??= id;
        items.Add(new JsonObject { ["_id"] = id, ["_tpl"] = tpl, ["composedKey"] = key, ["upd"] = new JsonObject { ["StackObjectsCount"] = 1 } });
        dist.Add(new JsonObject { ["composedKey"] = new JsonObject { ["key"] = key }, ["relativeProbability"] = d["relativeProbability"]!.GetValue<double>() });
    }
    template["Root"] = root;
    template["Items"] = items;

    return new JsonObject
    {
        ["locationId"] = s.LocationId + " OldColoredKeycards",
        ["probability"] = weight,
        ["template"] = template,
        ["itemDistribution"] = dist
    };
}

record Loot(double Mean, double Std, List<Spot> Spots);

class Spot
{
    public string LocationId = "", TemplateId = "";
    public JsonNode Node = null!;
    public double Prob, TotalRp;
    public bool Always, Blacklisted;
    public bool Guaranteed => Prob == 1.0;
    public Dictionary<string, double> Keycards = new();
    public Dictionary<string, string> KeycardKeys = new();
}
