using Microsoft.EntityFrameworkCore;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Common;
using TechBazar.Domain.Entities;

namespace TechBazar.Application.Admin;

public sealed class AdminCouponService(IApplicationDbContext db) : IAdminCouponService
{
    public async Task<IReadOnlyList<AdminCouponDto>> ListAsync(CancellationToken ct = default) =>
        (await db.Coupons.AsNoTracking().OrderByDescending(c => c.Id).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<AdminCouponDto> CreateAsync(SaveCouponRequest r, CancellationToken ct = default)
    {
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.Coupons.AnyAsync(c => c.Code == code, ct)) throw new ConflictException($"The code '{code}' already exists.");
        var c = new Coupon(); Apply(c, r, code);
        db.Coupons.Add(c);
        await db.SaveChangesAsync(ct);
        return ToDto(c);
    }

    public async Task<AdminCouponDto> UpdateAsync(int id, SaveCouponRequest r, CancellationToken ct = default)
    {
        var c = await db.Coupons.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Coupon not found.");
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.Coupons.AnyAsync(x => x.Code == code && x.Id != id, ct)) throw new ConflictException($"The code '{code}' already exists.");
        if (r.UsageLimit is { } limit && limit < c.UsedCount) throw new ConflictException($"The coupon has already been used {c.UsedCount} times: the limit cannot be lower.");
        Apply(c, r, code);
        await db.SaveChangesAsync(ct);
        return ToDto(c);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var c = await db.Coupons.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Coupon not found.");
        db.Coupons.Remove(c);
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(Coupon c, SaveCouponRequest r, string code)
    {
        c.Code = code; c.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        c.DiscountType = r.DiscountType; c.Value = r.Value; c.MinOrderAmount = r.MinOrderAmount; c.MaxDiscountAmount = r.MaxDiscountAmount;
        c.UsageLimit = r.UsageLimit; c.StartsAt = r.StartsAt?.ToUniversalTime(); c.ExpiresAt = r.ExpiresAt?.ToUniversalTime(); c.IsActive = r.IsActive;
    }

    private static AdminCouponDto ToDto(Coupon c) => new(c.Id, c.Code, c.Description, c.DiscountType, c.Value, c.MinOrderAmount, c.MaxDiscountAmount, c.UsageLimit, c.UsedCount, c.StartsAt, c.ExpiresAt, c.IsActive);
}
