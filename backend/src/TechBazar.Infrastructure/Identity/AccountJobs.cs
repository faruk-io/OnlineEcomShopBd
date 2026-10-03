using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TechBazar.Application.Auth;

namespace TechBazar.Infrastructure.Identity;

/// <summary>
/// In-process, bounded queue for account emails. Bounded on purpose: a flood of requests cannot exhaust memory (extra jobs are dropped and
/// logged; callers still get the same 202). Jobs are lost on shutdown, which is acceptable: the user simply asks again.
/// </summary>
public sealed class AccountJobQueue(ILogger<AccountJobQueue> logger) : IAccountJobs
{
    public readonly record struct Job(string Email, string? IpAddress);

    // FullMode.Wait (not DropWrite): with DropWrite, TryWrite still returns true when it silently drops the item, so overflow could not be detected.
    private readonly Channel<Job> _channel = Channel.CreateBounded<Job>(new BoundedChannelOptions(1000)
    { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private int _pending;

    internal ChannelReader<Job> Reader => _channel.Reader;
    internal void Completed() => Interlocked.Decrement(ref _pending);

    public void QueuePasswordReset(string email, string? ipAddress)
    {
        Interlocked.Increment(ref _pending);
        if (_channel.Writer.TryWrite(new Job(email, ipAddress))) return;
        Interlocked.Decrement(ref _pending);
        logger.LogWarning("Account job queue is full; a password-reset request was dropped");
    }

    /// <summary>Waits until every queued job has been processed (used by tests; a few polls of 10 ms).</summary>
    public async Task DrainAsync(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (Volatile.Read(ref _pending) > 0)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Account jobs did not finish in time.");
            await Task.Delay(10);
        }
    }
}

public sealed class AccountJobWorker(AccountJobQueue queue, IServiceScopeFactory scopes, ILogger<AccountJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IAccountService>().RequestPasswordResetAsync(job.Email, job.IpAddress, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "A password-reset job failed");   // never log the address
            }
            finally
            {
                queue.Completed();   // a failing job must neither stop the worker nor wedge DrainAsync
            }
        }
    }
}
