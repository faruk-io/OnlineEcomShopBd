using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TechBazar.Application.Auth;
using TechBazar.Infrastructure.Identity;

namespace TechBazar.UnitTests.Orders;

public class AccountJobQueueTests
{
    private sealed class FakeAccountService : IAccountService
    {
        public List<(string Email, string? Ip)> Resets { get; } = [];
        public Func<string, Task>? OnReset { get; set; }
        public async Task RequestPasswordResetAsync(string email, string? ipAddress, CancellationToken ct = default)
        {
            lock (Resets) Resets.Add((email, ipAddress));
            if (OnReset is not null) await OnReset(email);
        }
        public Task ResetPasswordAsync(string token, string newPassword, string? ipAddress, CancellationToken ct = default) => Task.CompletedTask;
        public Task VerifyEmailAsync(string token, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendVerificationAsync(Guid userId, string? ipAddress, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureCanCheckoutAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static (AccountJobQueue Queue, AccountJobWorker Worker, FakeAccountService Service) Create()
    {
        var service = new FakeAccountService();
        var scopes = new ServiceCollection().AddSingleton<IAccountService>(service).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var queue = new AccountJobQueue(NullLogger<AccountJobQueue>.Instance);
        return (queue, new AccountJobWorker(queue, scopes, NullLogger<AccountJobWorker>.Instance), service);
    }

    [Fact]
    public async Task QueuedJobsAreRunInOrder_AndDrainWaitsForThem()
    {
        var (queue, worker, service) = Create();
        await worker.StartAsync(CancellationToken.None);
        queue.QueuePasswordReset("a@example.com", "1.1.1.1");
        queue.QueuePasswordReset("b@example.com", null);
        await queue.DrainAsync();
        Assert.Equal([("a@example.com", "1.1.1.1"), ("b@example.com", null)], service.Resets);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AFailingJobNeitherStopsTheWorkerNorWedgesDrain()
    {
        var (queue, worker, service) = Create();
        service.OnReset = email => email.StartsWith("boom") ? throw new InvalidOperationException("smtp exploded") : Task.CompletedTask;
        await worker.StartAsync(CancellationToken.None);
        queue.QueuePasswordReset("boom@example.com", null);
        queue.QueuePasswordReset("fine@example.com", null);
        await queue.DrainAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["boom@example.com", "fine@example.com"], service.Resets.Select(r => r.Email));   // second job still ran
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TheQueueIsBounded_ExtraJobsAreDropped_NotBufferedWithoutLimit()
    {
        var (queue, _, _) = Create();                                  // no worker running: nothing is consumed
        for (var i = 0; i < 1500; i++) queue.QueuePasswordReset($"u{i}@example.com", null);
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => queue.DrainAsync(TimeSpan.FromMilliseconds(100)));   // 1000 still pending
        Assert.NotNull(ex);
        // pending never exceeded the capacity, so memory is bounded by design
        var (queue2, worker2, service2) = Create();
        for (var i = 0; i < 1500; i++) queue2.QueuePasswordReset($"u{i}@example.com", null);
        await worker2.StartAsync(CancellationToken.None);
        await queue2.DrainAsync();
        Assert.Equal(1000, service2.Resets.Count);
        await worker2.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DrainReturnsImmediatelyWhenIdle()
    {
        var (queue, _, _) = Create();
        await queue.DrainAsync(TimeSpan.FromMilliseconds(50));
    }
}
