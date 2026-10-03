using TechBazar.Application.PcBuilder;
using TechBazar.Domain.Enums;

namespace TechBazar.UnitTests.PcBuilder;

public class BuildCompatibilityCheckerTests
{
    private static int _id;

    private static BuildPart P(BuildSlot slot, int qty = 1, params (string Key, string Value)[] specs) =>
        new(slot, ++_id, $"{slot}-{_id}", qty, specs.ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase));

    private static BuildPart Cpu(string socket = "AM5", int tdp = 65, string igpu = "AMD Radeon Graphics") =>
        P(BuildSlot.Cpu, 1, ("Socket", socket), ("TDP", $"{tdp} W"), ("Integrated Graphics", igpu));
    private static BuildPart Board(string socket = "AM5", string ram = "DDR5", string form = "Micro-ATX", int slots = 4, int max = 128, int m2 = 2, int sata = 4) =>
        P(BuildSlot.Motherboard, 1, ("Socket", socket), ("RAM Type", ram), ("Form Factor", form), ("Memory Slots", slots.ToString()), ("Max Memory", $"{max} GB"), ("M.2 Slots", m2.ToString()), ("SATA Ports", sata.ToString()));
    private static BuildPart Ram(string type = "DDR5", int gb = 16, int modules = 1, int qty = 1, string ff = "DIMM (Desktop)") =>
        P(BuildSlot.Ram, qty, ("RAM Type", type), ("Capacity", $"{gb} GB"), ("Modules", $"{modules} x {gb / modules} GB"), ("Form Factor", ff));
    private static BuildPart Nvme(int qty = 1) => P(BuildSlot.Storage, qty, ("Interface", "NVMe PCIe 4.0 x4"), ("Form Factor", "M.2 2280"), ("Capacity", "1 TB"));
    private static BuildPart Sata(int qty = 1) => P(BuildSlot.Storage, qty, ("Interface", "SATA III"), ("Form Factor", "2.5 inch"), ("Capacity", "500 GB"));
    private static BuildPart Gpu(int tdp = 115, int length = 227, int recPsu = 550) => P(BuildSlot.Gpu, 1, ("TDP", $"{tdp} W"), ("Length", $"{length} mm"), ("Recommended PSU", $"{recPsu} W"));
    private static BuildPart Psu(int watts = 650) => P(BuildSlot.Psu, 1, ("Wattage", $"{watts} W"));
    private static BuildPart Case(string supported = "ATX, Micro-ATX, Mini-ITX", int gpuLen = 360, int coolerH = 160) =>
        P(BuildSlot.Case, 1, ("Supported Motherboards", supported), ("Max GPU Length", $"{gpuLen} mm"), ("Max CPU Cooler Height", $"{coolerH} mm"));
    private static BuildPart Cooler(string sockets = "LGA1700, AM4, AM5", int rating = 150, int height = 155) =>
        P(BuildSlot.Cooler, 1, ("Supported Sockets", sockets), ("TDP Rating", $"{rating} W"), ("Height", $"{height} mm"));

    private static CompatibilityReport Check(params BuildPart[] parts) => BuildCompatibilityChecker.Check(parts);
    private static bool Has(CompatibilityReport r, string code, IssueSeverity? sev = null) => r.Issues.Any(i => i.Code == code && (sev is null || i.Severity == sev));

    [Fact]
    public void ACompleteCompatibleBuild_HasNoErrorsOrWarnings()
    {
        var r = Check(Cpu(), Board(), Ram(modules: 2), Nvme(), Gpu(), Psu(650), Case());
        Assert.True(r.IsComplete);
        Assert.True(r.IsCompatible);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void EmptyBuild_IsIncompleteButNotIncompatible_AndListsEveryRequiredPart()
    {
        var r = Check();
        Assert.False(r.IsComplete);
        Assert.True(r.IsCompatible);
        Assert.Equal(6, r.Issues.Count(i => i.Code == "MISSING_PART"));
        Assert.All(r.Issues, i => Assert.Equal(IssueSeverity.Info, i.Severity));
        Assert.Equal(0, r.EstimatedWatts);
    }

    // ------------------------------------------------------------------ socket
    [Theory]
    [InlineData("AM5", "AM5", false)]
    [InlineData("am5", "AM5", false)]
    [InlineData("LGA1700", "LGA 1700", false)]   // formatting differences are not mismatches
    [InlineData("AM4", "AM5", true)]
    [InlineData("LGA1700", "AM5", true)]
    public void CpuSocketMustMatchTheMotherboardSocket(string cpuSocket, string boardSocket, bool mismatch)
    {
        var r = Check(Cpu(cpuSocket), Board(boardSocket));
        Assert.Equal(mismatch, Has(r, "SOCKET_MISMATCH", IssueSeverity.Error));
        Assert.Equal(!mismatch, r.IsCompatible);
    }

    [Fact]
    public void SocketMismatch_PointsAtBothParts() =>
        Assert.Equal([BuildSlot.Cpu, BuildSlot.Motherboard], Check(Cpu("AM4"), Board("AM5")).Issues.Single(i => i.Code == "SOCKET_MISMATCH").Slots);

    // ------------------------------------------------------------------ RAM
    [Theory]
    [InlineData("DDR5", "DDR5", false)]
    [InlineData("DDR4", "DDR4", false)]
    [InlineData("DDR4", "DDR5", true)]
    [InlineData("DDR5", "DDR4", true)]
    public void RamTypeMustMatchTheMotherboard(string ramType, string boardType, bool mismatch) =>
        Assert.Equal(mismatch, Has(Check(Board(ram: boardType), Ram(ramType)), "RAM_TYPE_MISMATCH", IssueSeverity.Error));

    [Fact]
    public void LaptopMemoryIsRejectedForADesktopBoard() =>
        Assert.True(Has(Check(Board(ram: "DDR4"), Ram("DDR4", ff: "SO-DIMM (Laptop)")), "RAM_FORM_FACTOR", IssueSeverity.Error));

    [Fact]
    public void ModuleCountMustFitTheMemorySlots()
    {
        Assert.False(Has(Check(Board(slots: 4), Ram(modules: 2, gb: 32, qty: 2)), "RAM_SLOTS_EXCEEDED")); // 2 kits x 2 = 4
        Assert.True(Has(Check(Board(slots: 2), Ram(modules: 2, gb: 32, qty: 2)), "RAM_SLOTS_EXCEEDED", IssueSeverity.Error)); // 4 > 2
        Assert.True(Has(Check(Board(slots: 2), Ram(qty: 3)), "RAM_SLOTS_EXCEEDED"));
    }

    [Fact]
    public void TotalCapacityMustNotExceedTheBoardMaximum()
    {
        Assert.False(Has(Check(Board(max: 64), Ram(gb: 32, modules: 2, qty: 2)), "RAM_CAPACITY_EXCEEDED")); // exactly 64
        var r = Check(Board(max: 64), Ram(gb: 32, modules: 2, qty: 3));
        Assert.True(Has(r, "RAM_CAPACITY_EXCEEDED", IssueSeverity.Error));
    }

    // ------------------------------------------------------------------ storage
    [Fact]
    public void M2DrivesAreLimitedByM2Slots()
    {
        Assert.False(Has(Check(Board(m2: 2), Nvme(2)), "M2_SLOTS_EXCEEDED"));
        Assert.True(Has(Check(Board(m2: 2), Nvme(3)), "M2_SLOTS_EXCEEDED", IssueSeverity.Error));
        Assert.True(Has(Check(Board(m2: 1), Nvme(), Nvme()), "M2_SLOTS_EXCEEDED"));
    }

    [Fact]
    public void SataDrivesAreLimitedBySataPorts()
    {
        Assert.False(Has(Check(Board(sata: 4), Sata(4)), "SATA_PORTS_EXCEEDED"));
        Assert.True(Has(Check(Board(sata: 2), Sata(3)), "SATA_PORTS_EXCEEDED", IssueSeverity.Error));
        Assert.False(Has(Check(Board(sata: 1, m2: 2), Nvme(2), Sata(1)), "SATA_PORTS_EXCEEDED")); // NVMe does not use SATA ports
    }

    // ------------------------------------------------------------------ case
    [Theory]
    [InlineData("Micro-ATX", "ATX, Micro-ATX, Mini-ITX", false)]
    [InlineData("ATX", "ATX, Micro-ATX, Mini-ITX", false)]
    [InlineData("ATX", "Micro-ATX, Mini-ITX", true)]
    [InlineData("Mini-ITX", "Micro-ATX, Mini-ITX", false)]
    [InlineData("E-ATX", "ATX, Micro-ATX", true)]
    public void TheCaseMustSupportTheMotherboardFormFactor(string boardForm, string supported, bool problem) =>
        Assert.Equal(problem, Has(Check(Board(form: boardForm), Case(supported)), "CASE_MOTHERBOARD_FIT", IssueSeverity.Error));

    [Theory]
    [InlineData(300, 300, false)]   // exactly fits
    [InlineData(301, 300, true)]
    [InlineData(227, 360, false)]
    public void GraphicsCardLengthMustFitTheCase(int gpuLen, int caseMax, bool tooLong) =>
        Assert.Equal(tooLong, Has(Check(Gpu(length: gpuLen), Case(gpuLen: caseMax)), "GPU_TOO_LONG", IssueSeverity.Error));

    // ------------------------------------------------------------------ cooler
    [Fact]
    public void CoolerMustSupportTheCpuSocket()
    {
        Assert.False(Has(Check(Cpu("AM5"), Cooler("LGA1700, AM4, AM5")), "COOLER_SOCKET"));
        Assert.True(Has(Check(Cpu("AM5"), Cooler("LGA1700, AM4")), "COOLER_SOCKET", IssueSeverity.Error));
    }

    [Fact]
    public void CoolerHeightMustFitTheCase()
    {
        Assert.False(Has(Check(Case(coolerH: 160), Cooler(height: 160)), "COOLER_TOO_TALL"));
        Assert.True(Has(Check(Case(coolerH: 159), Cooler(height: 160)), "COOLER_TOO_TALL", IssueSeverity.Error));
    }

    [Fact]
    public void WeakCoolerIsAWarningNotAnError()
    {
        var r = Check(Cpu(tdp: 125), Cooler(rating: 100));
        Assert.True(Has(r, "COOLER_WEAK", IssueSeverity.Warning));
        Assert.True(r.IsCompatible);
    }

    [Fact]
    public void HotCpuWithoutACoolerGetsAdvice()
    {
        Assert.True(Has(Check(Cpu(tdp: 125)), "COOLER_RECOMMENDED", IssueSeverity.Warning));
        Assert.False(Has(Check(Cpu(tdp: 65)), "COOLER_RECOMMENDED"));
        Assert.False(Has(Check(Cpu(tdp: 125), Cooler()), "COOLER_RECOMMENDED"));
    }

    // ------------------------------------------------------------------ display
    [Fact]
    public void CpuWithoutIntegratedGraphicsNeedsAGraphicsCard()
    {
        Assert.True(Has(Check(Cpu(igpu: "None (F-series)")), "NO_DISPLAY_OUTPUT", IssueSeverity.Error));
        Assert.False(Has(Check(Cpu(igpu: "None (F-series)"), Gpu()), "NO_DISPLAY_OUTPUT"));
        Assert.False(Has(Check(Cpu(igpu: "Intel UHD 770")), "NO_DISPLAY_OUTPUT"));
    }

    // ------------------------------------------------------------------ power
    [Fact]
    public void EstimatedPower_IsCpuPlusGpuPlusDocumentedOverheads()
    {
        // 65 (cpu) + 115 (gpu) + 60 base + 2 modules * 5 + 1 drive * 8 + cooler 5 = 263
        var r = Check(Cpu(tdp: 65), Gpu(tdp: 115), Ram(modules: 2, gb: 16), Nvme(), Cooler(), Psu(700));
        Assert.Equal(263, r.EstimatedWatts);
        Assert.Equal((int)Math.Ceiling(263 * 1.3m), r.RecommendedPsuWatts); // 343
        Assert.Equal(700, r.PsuWatts);
    }

    [Fact]
    public void PsuMustCoverEstimatedPowerWith30PercentHeadroom_Boundary()
    {
        var parts = new Func<BuildPart>[] { () => Cpu(tdp: 65), () => Gpu(tdp: 115, recPsu: 0), () => Ram(modules: 2, gb: 16), () => Nvme() };
        var baseline = Check([.. parts.Select(f => f()), Psu(1000)]);
        var required = baseline.RecommendedPsuWatts;          // ceil(258 * 1.3) = 336
        Assert.Equal(336, required);

        var exact = Check([.. parts.Select(f => f()), Psu(required)]);
        Assert.False(Has(exact, "PSU_UNDERPOWERED"));

        var oneShort = Check([.. parts.Select(f => f()), Psu(required - 1)]);
        Assert.True(Has(oneShort, "PSU_UNDERPOWERED", IssueSeverity.Error));
        Assert.False(oneShort.IsCompatible);
        Assert.Contains("336", oneShort.Issues.Single(i => i.Code == "PSU_UNDERPOWERED").Message);
    }

    [Fact]
    public void PsuBelowTheGpuRecommendationIsAWarning()
    {
        var r = Check(Cpu(tdp: 35), Gpu(tdp: 75, recPsu: 600), Psu(450)); // enough for the load, below the vendor's advice
        Assert.False(Has(r, "PSU_UNDERPOWERED"));
        Assert.True(Has(r, "PSU_BELOW_GPU_RECOMMENDATION", IssueSeverity.Warning));
        Assert.True(r.IsCompatible);
    }

    [Fact]
    public void WithoutAPsuNoPowerVerdictIsGiven_ButTheTargetIsStillReported()
    {
        var r = Check(Cpu(tdp: 65), Gpu(tdp: 115));
        Assert.False(Has(r, "PSU_UNDERPOWERED"));
        Assert.True(r.RecommendedPsuWatts > 0);
        Assert.Null(r.PsuWatts);
    }

    // ------------------------------------------------------------------ structure
    [Fact]
    public void OnlyRamAndStorageAllowMultipleQuantities()
    {
        Assert.True(Has(Check(P(BuildSlot.Cpu, 2, ("Socket", "AM5"))), "INVALID_QUANTITY", IssueSeverity.Error));
        Assert.True(Has(Check(P(BuildSlot.Gpu, 2)), "INVALID_QUANTITY"));
        Assert.False(Has(Check(Ram(qty: 2), Nvme(2)), "INVALID_QUANTITY"));
        Assert.True(Has(Check(P(BuildSlot.Ram, 0)), "INVALID_QUANTITY"));
    }

    [Fact]
    public void TwoDifferentPartsInASingleSlotIsAnError() =>
        Assert.True(Has(Check(Cpu(), Cpu("LGA1700")), "DUPLICATE_SLOT", IssueSeverity.Error));

    [Fact]
    public void IssuesAreOrderedErrorsFirst()
    {
        var r = Check(Cpu("AM4", tdp: 125), Board("AM5"));
        Assert.Equal(IssueSeverity.Error, r.Issues[0].Severity);
        Assert.Equal(r.Issues.OrderBy(i => i.Severity).Select(i => i.Severity), r.Issues.Select(i => i.Severity));
    }

    [Fact]
    public void ABuildWithOnlyOptionalPartsMissingIsComplete()
    {
        var r = Check(Cpu(), Board(), Ram(), Nvme(), Psu(650), Case()); // no GPU, no cooler, no monitor
        Assert.True(r.IsComplete);
    }

    // ------------------------------------------------------------------ filters
    [Fact]
    public void SlotFilters_NarrowChoicesToCompatibleParts()
    {
        var r = Check(Cpu("LGA1700"), Board("LGA1700", "DDR4"), Ram("DDR4"));
        Assert.Equal(["Socket:LGA1700"], r.SlotFilters[BuildSlot.Cpu]);
        Assert.Equal(["RAM Type:DDR4"], r.SlotFilters[BuildSlot.Ram]);
        Assert.Contains("Socket:LGA1700", r.SlotFilters[BuildSlot.Motherboard]);
        Assert.Contains("RAM Type:DDR4", r.SlotFilters[BuildSlot.Motherboard]);
    }

    [Fact]
    public void SlotFilters_ForACpuOnlyBuild_OnlyConstrainTheMotherboard()
    {
        var r = Check(Cpu("AM5"));
        Assert.Equal(["Socket:AM5"], r.SlotFilters[BuildSlot.Motherboard]);
        Assert.False(r.SlotFilters.ContainsKey(BuildSlot.Cpu));
    }
}
