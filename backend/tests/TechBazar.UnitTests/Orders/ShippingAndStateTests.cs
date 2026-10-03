using TechBazar.Application.Orders;
using TechBazar.Domain.Enums;

namespace TechBazar.UnitTests.Orders;

public class ShippingRulesTests
{
    private readonly ShippingOptions _o = new() { InsideDhakaFee = 70, OutsideDhakaFee = 130, PickupFee = 0 };

    [Theory]
    [InlineData("Dhaka", true)]
    [InlineData("dhaka", true)]
    [InlineData("  DHAKA ", true)]
    [InlineData("Gazipur", false)]
    [InlineData("Chattogram", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void InsideDhaka_IsDecidedByTheAddressDistrict(string? district, bool inside) => Assert.Equal(inside, ShippingRules.IsInsideDhaka(district));

    [Theory]
    [InlineData(ShippingMethod.HomeDeliveryInsideDhaka, "Dhaka", ShippingMethod.HomeDeliveryInsideDhaka)]
    [InlineData(ShippingMethod.HomeDeliveryOutsideDhaka, "Dhaka", ShippingMethod.HomeDeliveryInsideDhaka)]
    [InlineData(ShippingMethod.HomeDeliveryInsideDhaka, "Sylhet", ShippingMethod.HomeDeliveryOutsideDhaka)] // a client cannot buy the cheap rate for a far-away address
    [InlineData(ShippingMethod.HomeDeliveryOutsideDhaka, "Sylhet", ShippingMethod.HomeDeliveryOutsideDhaka)]
    [InlineData(ShippingMethod.StorePickup, "Sylhet", ShippingMethod.StorePickup)]
    [InlineData(ShippingMethod.HomeDeliveryInsideDhaka, null, ShippingMethod.HomeDeliveryOutsideDhaka)]
    public void Resolve_PricesHomeDeliveryByAddress(ShippingMethod requested, string? district, ShippingMethod expected) =>
        Assert.Equal(expected, ShippingRules.Resolve(requested, district));

    [Fact]
    public void Fees_AreReadFromConfiguration()
    {
        Assert.Equal(70m, ShippingRules.Fee(ShippingMethod.HomeDeliveryInsideDhaka, _o));
        Assert.Equal(130m, ShippingRules.Fee(ShippingMethod.HomeDeliveryOutsideDhaka, _o));
        Assert.Equal(0m, ShippingRules.Fee(ShippingMethod.StorePickup, _o));
        Assert.Equal(99m, ShippingRules.Fee(ShippingMethod.HomeDeliveryInsideDhaka, new ShippingOptions { InsideDhakaFee = 99 }));
    }
}

public class OrderStateMachineTests
{
    private const ShippingMethod Home = ShippingMethod.HomeDeliveryInsideDhaka;
    private const ShippingMethod Pickup = ShippingMethod.StorePickup;

    [Fact]
    public void HomeDelivery_HappyPath()
    {
        OrderStatus[] path = [OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Shipped, OrderStatus.Delivered];
        for (var i = 0; i < path.Length - 1; i++) Assert.True(OrderStateMachine.CanTransition(path[i], path[i + 1], Home), $"{path[i]} -> {path[i + 1]}");
    }

    [Fact]
    public void Pickup_UsesReadyForPickupInsteadOfShipped()
    {
        Assert.True(OrderStateMachine.CanTransition(OrderStatus.Processing, OrderStatus.ReadyForPickup, Pickup));
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.Processing, OrderStatus.Shipped, Pickup));
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.Processing, OrderStatus.ReadyForPickup, Home));
        Assert.True(OrderStateMachine.CanTransition(OrderStatus.ReadyForPickup, OrderStatus.Delivered, Pickup));
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Processing)]
    public void CanBeCancelledBeforeItLeavesTheWarehouse(OrderStatus from) => Assert.True(OrderStateMachine.CanTransition(from, OrderStatus.Cancelled, Home));

    [Theory]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Returned)]
    public void CannotBeCancelledOnceShippedOrFinished(OrderStatus from) => Assert.False(OrderStateMachine.CanTransition(from, OrderStatus.Cancelled, Home));

    [Fact]
    public void TerminalStatesHaveNoExits()
    {
        Assert.Empty(OrderStateMachine.Next(OrderStatus.Cancelled, Home));
        Assert.Empty(OrderStateMachine.Next(OrderStatus.Returned, Home));
    }

    [Fact]
    public void NoSkippingAndNoGoingBack()
    {
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.Pending, OrderStatus.Shipped, Home));
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.Pending, OrderStatus.Delivered, Home));
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.Shipped, OrderStatus.Processing, Home));
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.Delivered, OrderStatus.Pending, Home));
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.Pending, OrderStatus.Pending, Home));
    }

    [Fact]
    public void DeliveredOrdersCanOnlyBeReturned() => Assert.Equal([OrderStatus.Returned], OrderStateMachine.Next(OrderStatus.Delivered, Home));

    [Fact]
    public void CustomerMayCancelOnlyPendingOrConfirmed()
    {
        Assert.True(OrderStateMachine.CustomerCanCancel(OrderStatus.Pending));
        Assert.True(OrderStateMachine.CustomerCanCancel(OrderStatus.Confirmed));
        Assert.False(OrderStateMachine.CustomerCanCancel(OrderStatus.Processing));
        Assert.False(OrderStateMachine.CustomerCanCancel(OrderStatus.Shipped));
    }

    [Fact]
    public void StockIsReleasedOnCancelAndReturnOnly()
    {
        Assert.True(OrderStateMachine.ReleasesStock(OrderStatus.Cancelled));
        Assert.True(OrderStateMachine.ReleasesStock(OrderStatus.Returned));
        Assert.False(OrderStateMachine.ReleasesStock(OrderStatus.Delivered));
    }

    [Fact]
    public void TimelineDiffersForPickup()
    {
        Assert.Contains(OrderStatus.Shipped, OrderStateMachine.Timeline(Home));
        Assert.DoesNotContain(OrderStatus.ReadyForPickup, OrderStateMachine.Timeline(Home));
        Assert.Contains(OrderStatus.ReadyForPickup, OrderStateMachine.Timeline(Pickup));
        Assert.DoesNotContain(OrderStatus.Shipped, OrderStateMachine.Timeline(Pickup));
    }

    [Fact]
    public void EveryTransitionTargetIsPartOfAReachableTimelineOrTerminal()
    {
        foreach (var m in new[] { Home, Pickup })
            foreach (var s in Enum.GetValues<OrderStatus>())
                foreach (var next in OrderStateMachine.Next(s, m))
                    Assert.True(OrderStateMachine.Timeline(m).Contains(next) || next is OrderStatus.Cancelled or OrderStatus.Returned, $"{s}->{next} ({m})");
    }

    [Fact]
    public void TimelineBuilder_MarksDoneAndCurrentSteps_AndShowsTerminalStates()
    {
        var t0 = new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc);
        var history = new[] { new StatusEntryDto(OrderStatus.Pending, null, t0), new StatusEntryDto(OrderStatus.Confirmed, null, t0.AddHours(1)) };

        var steps = OrderReader.BuildTimeline(OrderStatus.Confirmed, Home, history);
        Assert.Equal(5, steps.Count);
        Assert.Equal([true, true, false, false, false], steps.Select(s => s.Done));
        Assert.Equal(OrderStatus.Confirmed, steps.Single(s => s.Current).Status);
        Assert.Equal(t0.AddHours(1), steps[1].ReachedAt);

        var cancelled = OrderReader.BuildTimeline(OrderStatus.Cancelled, Home, [.. history, new StatusEntryDto(OrderStatus.Cancelled, "Cancelled by customer", t0.AddHours(2))]);
        Assert.Equal([OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Cancelled], cancelled.Select(s => s.Status));
        Assert.True(cancelled[^1].Current);
        Assert.Equal("Picked up", OrderReader.Label(OrderStatus.Delivered, pickup: true));
    }
}
