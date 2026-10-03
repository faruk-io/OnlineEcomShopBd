using TechBazar.Application.Catalog;
using TechBazar.Domain.Enums;

namespace TechBazar.Application.PcBuilder;

public enum IssueSeverity { Error = 1, Warning = 2, Info = 3 }

public sealed record BuildIssue(IssueSeverity Severity, string Code, string Message, IReadOnlyList<BuildSlot> Slots);

/// <summary>One chosen part with the spec rows the rules need (key lookup is case-insensitive).</summary>
public sealed record BuildPart(BuildSlot Slot, int ProductId, string Name, int Quantity, IReadOnlyDictionary<string, string> Specs)
{
    public string? Spec(string key) => Specs.TryGetValue(key, out var v) ? v : null;
    public decimal? Number(string key) => SpecRules.ParseNumeric(Spec(key));
}

public sealed record CompatibilityReport(
    bool IsComplete,
    bool IsCompatible,
    int EstimatedWatts,
    int RecommendedPsuWatts,
    int? PsuWatts,
    IReadOnlyList<BuildIssue> Issues,
    IReadOnlyDictionary<BuildSlot, IReadOnlyList<string>> SlotFilters);

/// <summary>
/// Pure, server-side PC compatibility rules (the browser only displays the result).
/// Errors make the build incompatible; warnings are advice; info lists missing parts.
/// PSU rule: PSU wattage must be at least estimated system power x <see cref="PowerHeadroom"/>.
/// </summary>
public static class BuildCompatibilityChecker
{
    public const decimal PowerHeadroom = 1.3m;
    public const int BaseSystemWatts = 60;      // motherboard, chipset, fans, USB devices
    public const int WattsPerRamModule = 5;
    public const int WattsPerDrive = 8;
    public const int CoolerWatts = 5;

    public static readonly BuildSlot[] RequiredSlots = [BuildSlot.Cpu, BuildSlot.Motherboard, BuildSlot.Ram, BuildSlot.Storage, BuildSlot.Psu, BuildSlot.Case];
    public static readonly BuildSlot[] MultiSlots = [BuildSlot.Ram, BuildSlot.Storage];

    public static CompatibilityReport Check(IReadOnlyCollection<BuildPart> parts)
    {
        var issues = new List<BuildIssue>();
        var cpu = parts.FirstOrDefault(p => p.Slot == BuildSlot.Cpu);
        var mb = parts.FirstOrDefault(p => p.Slot == BuildSlot.Motherboard);
        var gpu = parts.FirstOrDefault(p => p.Slot == BuildSlot.Gpu);
        var psu = parts.FirstOrDefault(p => p.Slot == BuildSlot.Psu);
        var pcCase = parts.FirstOrDefault(p => p.Slot == BuildSlot.Case);
        var cooler = parts.FirstOrDefault(p => p.Slot == BuildSlot.Cooler);
        var rams = parts.Where(p => p.Slot == BuildSlot.Ram).ToList();
        var drives = parts.Where(p => p.Slot == BuildSlot.Storage).ToList();

        void Add(IssueSeverity s, string code, string message, params BuildSlot[] slots) => issues.Add(new BuildIssue(s, code, message, slots));

        foreach (var dup in parts.GroupBy(p => p.Slot).Where(g => g.Count() > 1 && !MultiSlots.Contains(g.Key)))
            Add(IssueSeverity.Error, "DUPLICATE_SLOT", $"Choose only one {SlotName(dup.Key).ToLowerInvariant()}.", dup.Key);

        foreach (var p in parts.Where(p => p.Quantity < 1 || (p.Quantity > 1 && !MultiSlots.Contains(p.Slot))))
            Add(IssueSeverity.Error, "INVALID_QUANTITY", $"{SlotName(p.Slot)}: only one {SlotName(p.Slot).ToLowerInvariant()} can be used.", p.Slot);

        // ---- CPU <-> motherboard -------------------------------------------------------------------
        if (cpu is not null && mb is not null)
        {
            var cs = cpu.Spec("Socket"); var ms = mb.Spec("Socket");
            if (cs is not null && ms is not null && !Same(cs, ms))
                Add(IssueSeverity.Error, "SOCKET_MISMATCH", $"CPU socket {cs} does not fit the motherboard socket {ms}.", BuildSlot.Cpu, BuildSlot.Motherboard);
        }

        // ---- RAM <-> motherboard -------------------------------------------------------------------
        if (mb is not null && rams.Count > 0)
        {
            var mbType = mb.Spec("RAM Type");
            foreach (var r in rams)
            {
                var rt = r.Spec("RAM Type");
                if (mbType is not null && rt is not null && !Same(mbType, rt))
                    Add(IssueSeverity.Error, "RAM_TYPE_MISMATCH", $"{r.Name} is {rt} but the motherboard supports {mbType}.", BuildSlot.Ram, BuildSlot.Motherboard);
                if (r.Spec("Form Factor") is { } ff && ff.Contains("SO-DIMM", StringComparison.OrdinalIgnoreCase))
                    Add(IssueSeverity.Error, "RAM_FORM_FACTOR", $"{r.Name} is laptop (SO-DIMM) memory and does not fit a desktop motherboard.", BuildSlot.Ram);
            }

            var modules = rams.Sum(r => ModulesPerKit(r) * r.Quantity);
            if (mb.Number("Memory Slots") is { } slots && modules > slots)
                Add(IssueSeverity.Error, "RAM_SLOTS_EXCEEDED", $"{modules} memory modules selected but the motherboard has only {slots:0} slots.", BuildSlot.Ram, BuildSlot.Motherboard);

            var totalGb = rams.Sum(r => (r.Number("Capacity") ?? 0) * r.Quantity);
            if (mb.Number("Max Memory") is { } max && totalGb > max)
                Add(IssueSeverity.Error, "RAM_CAPACITY_EXCEEDED", $"{totalGb:0} GB of RAM exceeds the motherboard maximum of {max:0} GB.", BuildSlot.Ram, BuildSlot.Motherboard);
        }

        // ---- storage <-> motherboard ---------------------------------------------------------------
        if (mb is not null && drives.Count > 0)
        {
            var m2 = drives.Where(IsM2).Sum(d => d.Quantity);
            var sata = drives.Where(d => !IsM2(d) && (d.Spec("Interface")?.Contains("SATA", StringComparison.OrdinalIgnoreCase) ?? false)).Sum(d => d.Quantity);
            if (mb.Number("M.2 Slots") is { } m2Slots && m2 > m2Slots)
                Add(IssueSeverity.Error, "M2_SLOTS_EXCEEDED", $"{m2} M.2 drives selected but the motherboard has {m2Slots:0} M.2 slots.", BuildSlot.Storage, BuildSlot.Motherboard);
            if (mb.Number("SATA Ports") is { } sataPorts && sata > sataPorts)
                Add(IssueSeverity.Error, "SATA_PORTS_EXCEEDED", $"{sata} SATA drives selected but the motherboard has {sataPorts:0} SATA ports.", BuildSlot.Storage, BuildSlot.Motherboard);
        }

        // ---- case fit ------------------------------------------------------------------------------
        if (mb is not null && pcCase is not null && mb.Spec("Form Factor") is { } mbForm && pcCase.Spec("Supported Motherboards") is { } supported)
        {
            if (!SplitList(supported).Any(s => Same(s, mbForm)))
                Add(IssueSeverity.Error, "CASE_MOTHERBOARD_FIT", $"The case supports {supported} motherboards, but the selected board is {mbForm}.", BuildSlot.Case, BuildSlot.Motherboard);
        }
        if (gpu is not null && pcCase is not null && gpu.Number("Length") is { } gl && pcCase.Number("Max GPU Length") is { } maxGl && gl > maxGl)
            Add(IssueSeverity.Error, "GPU_TOO_LONG", $"The graphics card is {gl:0} mm long but the case fits cards up to {maxGl:0} mm.", BuildSlot.Gpu, BuildSlot.Case);

        // ---- cooler --------------------------------------------------------------------------------
        if (cooler is not null)
        {
            if (cpu?.Spec("Socket") is { } sock && cooler.Spec("Supported Sockets") is { } sockets && !SplitList(sockets).Any(s => Same(s, sock)))
                Add(IssueSeverity.Error, "COOLER_SOCKET", $"{cooler.Name} does not support the {sock} socket.", BuildSlot.Cooler, BuildSlot.Cpu);
            if (cpu?.Number("TDP") is { } tdp && cooler.Number("TDP Rating") is { } rating && rating < tdp)
                Add(IssueSeverity.Warning, "COOLER_WEAK", $"{cooler.Name} is rated for {rating:0} W but the CPU can draw {tdp:0} W; expect higher temperatures.", BuildSlot.Cooler, BuildSlot.Cpu);
            if (pcCase?.Number("Max CPU Cooler Height") is { } maxH && cooler.Number("Height") is { } h && h > maxH)
                Add(IssueSeverity.Error, "COOLER_TOO_TALL", $"The cooler is {h:0} mm tall but the case allows {maxH:0} mm.", BuildSlot.Cooler, BuildSlot.Case);
        }
        else if (cpu is not null && (cpu.Number("TDP") ?? 0) >= 120)
        {
            Add(IssueSeverity.Warning, "COOLER_RECOMMENDED", "This CPU draws 120 W or more and ships without a cooler: add an aftermarket CPU cooler.", BuildSlot.Cpu, BuildSlot.Cooler);
        }

        // ---- display output ------------------------------------------------------------------------
        if (cpu is not null && gpu is null && (cpu.Spec("Integrated Graphics")?.StartsWith("None", StringComparison.OrdinalIgnoreCase) ?? false))
            Add(IssueSeverity.Error, "NO_DISPLAY_OUTPUT", "This CPU has no integrated graphics: add a graphics card to get a display.", BuildSlot.Cpu, BuildSlot.Gpu);

        // ---- power ---------------------------------------------------------------------------------
        var moduleCount = rams.Sum(r => ModulesPerKit(r) * r.Quantity);
        var driveCount = drives.Sum(d => d.Quantity);
        var drawParts = cpu is not null || gpu is not null;
        var estimated = !drawParts ? 0 : (int)Math.Ceiling(
            (cpu?.Number("TDP") ?? 0) + (gpu?.Number("TDP") ?? 0) + BaseSystemWatts
            + WattsPerRamModule * moduleCount + WattsPerDrive * driveCount + (cooler is null ? 0 : CoolerWatts));
        var required = (int)Math.Ceiling(estimated * PowerHeadroom);
        int? psuWatts = psu?.Number("Wattage") is { } w ? (int)w : null;

        if (psuWatts is { } have && estimated > 0)
        {
            if (have < required)
                Add(IssueSeverity.Error, "PSU_UNDERPOWERED", $"Estimated load is {estimated} W; with {PowerHeadroom:0.#}x headroom you need a {required} W+ PSU, but this one is {have} W.", BuildSlot.Psu);
            if (gpu?.Number("Recommended PSU") is { } rec && have < rec)
                Add(IssueSeverity.Warning, "PSU_BELOW_GPU_RECOMMENDATION", $"The graphics card manufacturer recommends a {rec:0} W PSU; this one is {have} W.", BuildSlot.Psu, BuildSlot.Gpu);
        }

        // ---- completeness --------------------------------------------------------------------------
        var missing = RequiredSlots.Where(s => parts.All(p => p.Slot != s)).ToList();
        foreach (var s in missing)
            Add(IssueSeverity.Info, "MISSING_PART", $"Add a {SlotName(s).ToLowerInvariant()} to complete your build.", s);

        return new CompatibilityReport(
            IsComplete: missing.Count == 0,
            IsCompatible: issues.All(i => i.Severity != IssueSeverity.Error),
            EstimatedWatts: estimated,
            RecommendedPsuWatts: required,
            PsuWatts: psuWatts,
            Issues: issues.OrderBy(i => i.Severity).ThenBy(i => i.Code).ToList(),
            SlotFilters: BuildFilters(cpu, mb, rams));
    }

    /// <summary>Spec filters (<c>Key:Value</c>, same syntax as the catalog API) that narrow each slot to compatible parts.</summary>
    private static IReadOnlyDictionary<BuildSlot, IReadOnlyList<string>> BuildFilters(BuildPart? cpu, BuildPart? mb, List<BuildPart> rams)
    {
        var f = new Dictionary<BuildSlot, IReadOnlyList<string>>();
        if (mb?.Spec("Socket") is { } mbSocket) f[BuildSlot.Cpu] = [$"Socket:{mbSocket}"];
        var mbFilters = new List<string>();
        if (cpu?.Spec("Socket") is { } cpuSocket) mbFilters.Add($"Socket:{cpuSocket}");
        if (rams.FirstOrDefault()?.Spec("RAM Type") is { } ramType) mbFilters.Add($"RAM Type:{ramType}");
        if (mbFilters.Count > 0) f[BuildSlot.Motherboard] = mbFilters;
        if (mb?.Spec("RAM Type") is { } type) f[BuildSlot.Ram] = [$"RAM Type:{type}"];
        return f;
    }

    public static string SlotName(BuildSlot s) => s switch
    {
        BuildSlot.Cpu => "CPU",
        BuildSlot.Motherboard => "Motherboard",
        BuildSlot.Ram => "RAM",
        BuildSlot.Storage => "Storage",
        BuildSlot.Gpu => "Graphics card",
        BuildSlot.Psu => "Power supply",
        BuildSlot.Case => "Case",
        BuildSlot.Cooler => "CPU cooler",
        BuildSlot.Monitor => "Monitor",
        _ => s.ToString(),
    };

    private static bool IsM2(BuildPart d) =>
        (d.Spec("Interface")?.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ?? false)
        || (d.Spec("Form Factor")?.Contains("M.2", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>"2 x 8 GB" -> 2 modules per kit; a single stick -> 1.</summary>
    private static int ModulesPerKit(BuildPart ram) => Math.Max(1, (int)(ram.Number("Modules") ?? 1));

    private static string Norm(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static bool Same(string a, string b) => Norm(a) == Norm(b);
    private static IEnumerable<string> SplitList(string s) => s.Split([',', '/', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
