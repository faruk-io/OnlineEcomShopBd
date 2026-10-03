using Microsoft.EntityFrameworkCore;
using TechBazar.Application.Abstractions;
using TechBazar.Application.Common;
using TechBazar.Domain.Entities;

namespace TechBazar.Application.Orders;

public sealed class AddressService(IApplicationDbContext db) : IAddressService
{
    private const int MaxAddresses = 10;

    public async Task<IReadOnlyList<AddressDto>> ListAsync(Guid userId, CancellationToken ct = default) =>
        await db.Addresses.AsNoTracking().Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.Id)
            .Select(a => new AddressDto(a.Id, a.Label, a.FullName, a.Phone, a.Division, a.District, a.Upazila, a.AddressLine, a.PostalCode, a.IsDefault))
            .ToListAsync(ct);

    public async Task<AddressDto> CreateAsync(Guid userId, SaveAddressRequest r, CancellationToken ct = default)
    {
        var existing = await db.Addresses.Where(a => a.UserId == userId).ToListAsync(ct);
        if (existing.Count >= MaxAddresses) throw new ConflictException($"You can save up to {MaxAddresses} addresses.");

        var address = new Address { UserId = userId };
        Apply(address, r);
        address.IsDefault = r.IsDefault || existing.Count == 0; // the first address is always the default
        if (address.IsDefault) foreach (var a in existing) a.IsDefault = false;
        db.Addresses.Add(address);
        await db.SaveChangesAsync(ct);
        return ToDto(address);
    }

    public async Task<AddressDto> UpdateAsync(Guid userId, int id, SaveAddressRequest r, CancellationToken ct = default)
    {
        var all = await db.Addresses.Where(a => a.UserId == userId).ToListAsync(ct);
        var address = all.FirstOrDefault(a => a.Id == id) ?? throw new NotFoundException("Address not found.");
        Apply(address, r);
        if (r.IsDefault && !address.IsDefault)
        {
            foreach (var a in all) a.IsDefault = false;
            address.IsDefault = true;
        }
        await db.SaveChangesAsync(ct);
        return ToDto(address);
    }

    public async Task DeleteAsync(Guid userId, int id, CancellationToken ct = default)
    {
        var all = await db.Addresses.Where(a => a.UserId == userId).ToListAsync(ct);
        var address = all.FirstOrDefault(a => a.Id == id) ?? throw new NotFoundException("Address not found.");
        db.Addresses.Remove(address);
        if (address.IsDefault) all.Where(a => a.Id != id).OrderByDescending(a => a.Id).FirstOrDefault()?.SetDefault(); // promote another
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AddressDto>> SetDefaultAsync(Guid userId, int id, CancellationToken ct = default)
    {
        var all = await db.Addresses.Where(a => a.UserId == userId).ToListAsync(ct);
        if (all.All(a => a.Id != id)) throw new NotFoundException("Address not found.");
        foreach (var a in all) a.IsDefault = a.Id == id;
        await db.SaveChangesAsync(ct);
        return await ListAsync(userId, ct);
    }

    private static void Apply(Address a, SaveAddressRequest r)
    {
        a.Label = r.Label.Trim();
        a.FullName = r.FullName.Trim();
        a.Phone = r.Phone.Trim();
        a.Division = Divisions.All.First(d => d.Equals(r.Division.Trim(), StringComparison.OrdinalIgnoreCase));
        a.District = r.District.Trim();
        a.Upazila = string.IsNullOrWhiteSpace(r.Upazila) ? null : r.Upazila.Trim();
        a.AddressLine = r.AddressLine.Trim();
        a.PostalCode = string.IsNullOrWhiteSpace(r.PostalCode) ? null : r.PostalCode.Trim();
    }

    private static AddressDto ToDto(Address a) => new(a.Id, a.Label, a.FullName, a.Phone, a.Division, a.District, a.Upazila, a.AddressLine, a.PostalCode, a.IsDefault);
}

internal static class AddressExtensions
{
    public static void SetDefault(this Address a) => a.IsDefault = true;
}
