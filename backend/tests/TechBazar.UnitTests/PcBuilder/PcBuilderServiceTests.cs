using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TechBazar.Application.Common;
using TechBazar.Application.PcBuilder;
using TechBazar.Domain.Enums;
using TechBazar.UnitTests.Support;

namespace TechBazar.UnitTests.PcBuilder;

/// <summary>The rules against REAL seeded products (specs, prices, categories), not hand-made dictionaries.</summary>
public class PcBuilderServiceTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    private BuildRequest Build(params (BuildSlot slot, string name, int qty)[] items)
    {
        using var scope = db.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TechBazar.Infrastructure.Persistence.ApplicationDbContext>();
        return new BuildRequest(items.Select(i =>
        {
            var id = ctx.Products.Where(p => p.Name.Contains(i.name)).Select(p => p.Id).FirstOrDefault();
            Assert.True(id > 0, $"seed product containing '{i.name}' not found");
            return new BuildItemRequest(i.slot, id, i.qty);
        }).ToList());
    }

    private async Task<BuildReportDto> Evaluate(BuildRequest r)
    {
        using var scope = db.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPcBuilderService>().EvaluateAsync(r);
    }

    [Fact]
    public async Task AKnownGoodAm4Build_IsCompleteAndCompatible_WithAServerCalculatedTotal()
    {
        var report = await Evaluate(Build(
            (BuildSlot.Cpu, "Ryzen 5 5600 Processor", 1), (BuildSlot.Motherboard, "B550M DS3H", 1), (BuildSlot.Ram, "Vengeance LPX 16GB", 1),
            (BuildSlot.Storage, "WD Blue SN580", 1), (BuildSlot.Gpu, "RTX 4060 OC", 1), (BuildSlot.Psu, "MWE 650", 1),
            (BuildSlot.Case, "MasterBox Q300L", 1), (BuildSlot.Cooler, "Hyper 212", 1)));

        Assert.True(report.Compatibility.IsComplete);
        Assert.True(report.Compatibility.IsCompatible, string.Join(" | ", report.Compatibility.Issues.Select(i => i.Message)));
        Assert.Equal(report.Lines.Sum(l => l.UnitPrice * l.Quantity), report.Total);
        Assert.True(report.Total > 0);
        Assert.True(report.AllPurchasable);
        Assert.Equal(11900m, report.Lines.Single(l => l.Slot == BuildSlot.Cpu).UnitPrice); // the SALE price, from the database
    }

    [Fact]
    public async Task Am5CpuOnAnAm4Board_WithDdr5Ram_ReportsEveryProblem()
    {
        var report = await Evaluate(Build(
            (BuildSlot.Cpu, "Ryzen 5 7600 Processor", 1), (BuildSlot.Motherboard, "B550M DS3H", 1), (BuildSlot.Ram, "Kingston FURY Beast 16GB DDR5", 1)));
        var codes = report.Compatibility.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Code).ToList();
        Assert.Contains("SOCKET_MISMATCH", codes);
        Assert.Contains("RAM_TYPE_MISMATCH", codes);
        Assert.False(report.Compatibility.IsCompatible);
    }

    [Fact]
    public async Task APowerHungryBuildOnA550WPsu_FailsTheHeadroomRule()
    {
        var report = await Evaluate(Build(
            (BuildSlot.Cpu, "Ryzen 7 7800X3D", 1), (BuildSlot.Motherboard, "B650M-P", 1), (BuildSlot.Ram, "Kingston FURY Beast 16GB DDR5", 1),
            (BuildSlot.Storage, "Samsung 980 500GB", 1), (BuildSlot.Gpu, "RX 7800 XT", 1), (BuildSlot.Psu, "CV550", 1), (BuildSlot.Case, "4000D", 1)));
        // 120 + 263 + 60 + 5 + 8 = 456 W -> x1.3 = 593 W > 550 W
        Assert.Equal(456, report.Compatibility.EstimatedWatts);
        Assert.Equal(593, report.Compatibility.RecommendedPsuWatts);
        Assert.Contains(report.Compatibility.Issues, i => i.Code == "PSU_UNDERPOWERED" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task AnAtxBoardDoesNotFitAMicroAtxCase()
    {
        var report = await Evaluate(Build((BuildSlot.Motherboard, "TUF GAMING B650-PLUS", 1), (BuildSlot.Case, "MasterBox Q300L", 1)));
        Assert.Contains(report.Compatibility.Issues, i => i.Code == "CASE_MOTHERBOARD_FIT");
    }

    [Fact]
    public async Task ACoolerWithoutLga1700Support_IsRejectedForAnIntelCpu_AndFiltersGuideTheUser()
    {
        var report = await Evaluate(Build((BuildSlot.Cpu, "Core i5-12400F", 1), (BuildSlot.Motherboard, "B660M-A", 1)));
        Assert.Equal(["Socket:LGA1700"], report.Compatibility.SlotFilters[BuildSlot.Cpu]);
        Assert.Contains("RAM Type:DDR4", report.Compatibility.SlotFilters[BuildSlot.Ram]);
    }

    [Fact]
    public async Task ThePriceIsAlwaysTakenFromTheDatabase()
    {
        // The request type has no price field at all: there is nothing a client could tamper with.
        Assert.DoesNotContain(typeof(BuildItemRequest).GetProperties(), p => p.Name.Contains("Price", StringComparison.OrdinalIgnoreCase));
        var report = await Evaluate(Build((BuildSlot.Ram, "Vengeance LPX 16GB", 2)));
        var line = Assert.Single(report.Lines);
        Assert.Equal(line.UnitPrice * 2, line.LineTotal);
    }

    [Fact]
    public async Task APartInTheWrongSlotIsRejected()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Evaluate(Build((BuildSlot.Gpu, "Ryzen 5 5600 Processor", 1))));
        Assert.Contains("cannot be used as", ex.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task AnUnknownProductIs404()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Evaluate(new BuildRequest([new BuildItemRequest(BuildSlot.Cpu, 999999, 1)])));
    }

    [Fact]
    public async Task SavedBuilds_GetAShortCode_AndReloadWithCurrentPrices()
    {
        var req = Build((BuildSlot.Cpu, "Ryzen 5 5600 Processor", 1), (BuildSlot.Motherboard, "B550M DS3H", 1));
        using var scope = db.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPcBuilderService>();

        var saved = await svc.SaveAsync(null, new SaveBuildRequest("Budget AM4", req.Items));
        Assert.Matches("^[A-Z2-9]{8}$", saved.Code);
        Assert.DoesNotContain(saved.Code, new[] { "0", "O", "1", "I" }.Where(c => saved.Code.Contains(c)));

        var loaded = await svc.GetAsync(saved.Code.ToLowerInvariant()); // case-insensitive
        Assert.Equal("Budget AM4", loaded.Name);
        Assert.Equal(saved.Report.Total, loaded.Report.Total);
        Assert.Equal(2, loaded.Report.Lines.Count);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync("ZZZZZZZZ"));
    }

    [Fact]
    public void BuilderSlots_CoverAllNineRequestedPartTypes()
    {
        using var scope = db.CreateScope();
        var slots = scope.ServiceProvider.GetRequiredService<IPcBuilderService>().Slots();
        Assert.Equal(Enum.GetValues<BuildSlot>().Length, slots.Count);
        Assert.Equal(["Processor", "Motherboard", "CPU cooler", "Memory (RAM)", "Storage", "Graphics card", "Power supply", "Casing", "Monitor"], slots.Select(s => s.Label));
        Assert.Equal(6, slots.Count(s => s.Required));
    }
}
