using TechBazar.Application.Common;
using TechBazar.Application.Orders;
using TechBazar.UnitTests.Support;

namespace TechBazar.UnitTests.Orders;

public class AddressServiceTests
{
    private static SaveAddressRequest Req(string label = "Home", bool isDefault = false, string district = "Dhaka") =>
        new(label, "Rahim Uddin", "01712345678", "Dhaka", district, "Mirpur", "House 1, Road 2", "1216", isDefault);

    [Fact]
    public async Task TheFirstAddressBecomesTheDefault_AndANewDefaultReplacesIt()
    {
        using var s = await Scenario.CreateAsync();
        var svc = s.Get<IAddressService>();
        var home = await svc.CreateAsync(s.UserId, Req("Home"));
        var office = await svc.CreateAsync(s.UserId, Req("Office"));
        Assert.True(home.IsDefault);
        Assert.False(office.IsDefault);

        var third = await svc.CreateAsync(s.UserId, Req("Parents", isDefault: true));
        var list = await svc.ListAsync(s.UserId);
        Assert.Equal(third.Id, Assert.Single(list, a => a.IsDefault).Id);
        Assert.Equal(third.Id, list[0].Id);   // the default is listed first
    }

    [Fact]
    public async Task SetDefaultAndDelete_KeepExactlyOneDefault()
    {
        using var s = await Scenario.CreateAsync();
        var svc = s.Get<IAddressService>();
        var a = await svc.CreateAsync(s.UserId, Req("A"));
        var b = await svc.CreateAsync(s.UserId, Req("B"));
        await svc.SetDefaultAsync(s.UserId, b.Id);
        Assert.Equal(b.Id, Assert.Single(await svc.ListAsync(s.UserId), x => x.IsDefault).Id);

        await svc.DeleteAsync(s.UserId, b.Id);
        var left = await svc.ListAsync(s.UserId);
        Assert.Equal(a.Id, Assert.Single(left).Id);
        Assert.True(left[0].IsDefault);     // the remaining address is promoted
    }

    [Fact]
    public async Task UpdateChangesFieldsAndNormalisesTheDivisionName()
    {
        using var s = await Scenario.CreateAsync();
        var svc = s.Get<IAddressService>();
        var a = await svc.CreateAsync(s.UserId, Req());
        var updated = await svc.UpdateAsync(s.UserId, a.Id, Req("Work", district: "Gazipur") with { Division = "dhaka", PostalCode = null, Upazila = " " });
        Assert.Equal(("Work", "Dhaka", "Gazipur", null, null), (updated.Label, updated.Division, updated.District, updated.PostalCode, updated.Upazila));
    }

    [Fact]
    public async Task AddressesArePrivateToTheirOwner()
    {
        using var s = await Scenario.CreateAsync();
        var (other, _) = await s.NewUserAsync("karim@example.com");
        var svc = s.Get<IAddressService>();
        var mine = await svc.CreateAsync(s.UserId, Req());

        await Assert.ThrowsAsync<NotFoundException>(() => svc.UpdateAsync(other, mine.Id, Req()));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.DeleteAsync(other, mine.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.SetDefaultAsync(other, mine.Id));
        Assert.Empty(await svc.ListAsync(other));
    }

    [Fact]
    public async Task AtMostTenAddressesCanBeSaved()
    {
        using var s = await Scenario.CreateAsync();
        var svc = s.Get<IAddressService>();
        for (var i = 0; i < 10; i++) await svc.CreateAsync(s.UserId, Req($"A{i}"));
        await Assert.ThrowsAsync<ConflictException>(() => svc.CreateAsync(s.UserId, Req("one too many")));
    }

    [Theory]
    [InlineData("Home", "Rahim", "01712345678", "Dhaka", "Dhaka", "House 1", "1207", true)]
    [InlineData("Home", "Rahim", "+8801712345678", "Chattogram", "Chattogram", "House 1", null, true)]
    [InlineData("", "Rahim", "01712345678", "Dhaka", "Dhaka", "House 1", null, false)]
    [InlineData("Home", "", "01712345678", "Dhaka", "Dhaka", "House 1", null, false)]
    [InlineData("Home", "Rahim", "12345", "Dhaka", "Dhaka", "House 1", null, false)]
    [InlineData("Home", "Rahim", "01712345678", "Atlantis", "Dhaka", "House 1", null, false)]
    [InlineData("Home", "Rahim", "01712345678", "Dhaka", "", "House 1", null, false)]
    [InlineData("Home", "Rahim", "01712345678", "Dhaka", "Dhaka", "", null, false)]
    [InlineData("Home", "Rahim", "01712345678", "Dhaka", "Dhaka", "House 1", "12", false)]
    public void Validation(string label, string name, string phone, string division, string district, string line, string? postal, bool valid) =>
        Assert.Equal(valid, new SaveAddressRequestValidator().Validate(new SaveAddressRequest(label, name, phone, division, district, null, line, postal, false)).IsValid);
}
