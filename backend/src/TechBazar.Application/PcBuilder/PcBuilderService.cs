using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Common;
using TechBazar.Application.Orders;
using TechBazar.Application.Payments;
using TechBazar.Domain.Entities;
using TechBazar.Domain.Enums;

namespace TechBazar.Application.PcBuilder;

public sealed class PcBuilderService(IApplicationDbContext db) : IPcBuilderService
{
    private static readonly IReadOnlyList<BuilderSlotDto> SlotList =
    [
        new(BuildSlot.Cpu, "Processor", "processor", true, false),
        new(BuildSlot.Motherboard, "Motherboard", "motherboard", true, false),
        new(BuildSlot.Cooler, "CPU cooler", "cpu-cooler", false, false),
        new(BuildSlot.Ram, "Memory (RAM)", "ram", true, true),
        new(BuildSlot.Storage, "Storage", "ssd", true, true),
        new(BuildSlot.Gpu, "Graphics card", "graphics-card", false, false),
        new(BuildSlot.Psu, "Power supply", "power-supply", true, false),
        new(BuildSlot.Case, "Casing", "casing", true, false),
        new(BuildSlot.Monitor, "Monitor", "monitor", false, false),
    ];

    public IReadOnlyList<BuilderSlotDto> Slots() => SlotList;

    public async Task<BuildReportDto> EvaluateAsync(BuildRequest request, CancellationToken ct = default)
    {
        // Same part twice in a slot = one line with a higher quantity.
        var wanted = request.Items.GroupBy(i => (i.Slot, i.ProductId)).Select(g => new BuildItemRequest(g.Key.Slot, g.Key.ProductId, g.Sum(x => x.Quantity))).ToList();
        var ids = wanted.Select(w => w.ProductId).Distinct().ToList();

        var products = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id, p.Name, p.Slug, p.Sku, p.Price, p.EffectivePrice, p.StockStatus, Brand = p.Brand.Name, Category = p.Category.Slug,
                Image = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.Url).FirstOrDefault(),
                Specs = p.Specifications.Select(s => new { s.Key, s.Value }).ToList(),
            })
            .ToDictionaryAsync(p => p.Id, ct);

        var lines = new List<BuildLineDto>();
        var parts = new List<BuildPart>();
        foreach (var w in wanted)
        {
            if (!products.TryGetValue(w.ProductId, out var p)) throw new NotFoundException($"Product {w.ProductId} was not found.");
            var slot = SlotList.First(s => s.Slot == w.Slot);
            if (!string.Equals(p.Category, slot.CategorySlug, StringComparison.OrdinalIgnoreCase))
                throw new ValidationException([new ValidationFailure("items", $"{p.Name} cannot be used as {slot.Label.ToLowerInvariant()}.")]);

            var purchasable = p.StockStatus is StockStatus.InStock or StockStatus.PreOrder;
            lines.Add(new BuildLineDto(w.Slot, p.Id, p.Name, p.Slug, p.Sku, p.Image, p.Brand, p.Price, p.EffectivePrice, w.Quantity,
                OrderCalculator.Round(p.EffectivePrice * w.Quantity), p.StockStatus, purchasable));

            var specs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in p.Specs) specs[s.Key] = s.Value;
            parts.Add(new BuildPart(w.Slot, p.Id, p.Name, w.Quantity, specs));
        }

        var report = BuildCompatibilityChecker.Check(parts);
        lines = lines.OrderBy(l => SlotList.ToList().FindIndex(s => s.Slot == l.Slot)).ToList();
        return new BuildReportDto(
            lines, lines.Sum(l => l.LineTotal), lines.Sum(l => OrderCalculator.Round((l.ListPrice - l.UnitPrice) * l.Quantity)),
            lines.All(l => l.Purchasable), report);
    }

    public async Task<SavedBuildDto> SaveAsync(Guid? userId, SaveBuildRequest request, CancellationToken ct = default)
    {
        var report = await EvaluateAsync(new BuildRequest(request.Items), ct); // validates products + slots
        var build = new PcBuild { UserId = userId, Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(), ShareCode = await NewCodeAsync(ct) };
        foreach (var l in report.Lines) build.Items.Add(new PcBuildItem { PcBuild = build, Slot = l.Slot, ProductId = l.ProductId, Quantity = l.Quantity });
        db.PcBuilds.Add(build);
        await db.SaveChangesAsync(ct);
        return new SavedBuildDto(build.ShareCode, build.Name, build.CreatedAt, report);
    }

    public async Task<SavedBuildDto> GetAsync(string code, CancellationToken ct = default)
    {
        var build = await db.PcBuilds.AsNoTracking().Include(b => b.Items)
                        .FirstOrDefaultAsync(b => b.ShareCode == code.ToUpperInvariant(), ct)
                    ?? throw new NotFoundException("This build link is not valid.");
        // Re-priced and re-validated against the CURRENT catalog; parts that were removed since are dropped.
        var items = build.Items.Select(i => new BuildItemRequest(i.Slot, i.ProductId, i.Quantity)).ToList();
        var live = await db.Products.AsNoTracking().Where(p => p.IsActive && items.Select(i => i.ProductId).Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
        var report = await EvaluateAsync(new BuildRequest(items.Where(i => live.Contains(i.ProductId)).ToList()), ct);
        return new SavedBuildDto(build.ShareCode, build.Name, build.CreatedAt, report);
    }

    private async Task<string> NewCodeAsync(CancellationToken ct)
    {
        for (var i = 0; i < 5; i++)
        {
            var code = PaymentService.RandomCode(8);
            if (!await db.PcBuilds.IgnoreQueryFilters().AnyAsync(b => b.ShareCode == code, ct)) return code;
        }
        throw new InvalidOperationException("Could not allocate a share code.");
    }
}
