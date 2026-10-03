using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TechBazar.Api.Extensions;
using TechBazar.Application.Payments;
using TechBazar.Domain.Enums;

namespace TechBazar.Api.Controllers;

/// <summary>
/// Gateway callbacks. SSLCommerz POSTs form fields to success / fail / cancel (via the customer's browser) and to ipn
/// (server to server). Everything is re-verified by the gateway implementation; the browser callbacks only decide where to
/// send the customer afterwards, they never grant anything by themselves.
/// </summary>
[Route("api/v1/payments/{gateway}")]
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(Policies.PublicRateLimit)]
[IgnoreAntiforgeryToken]
public sealed class PaymentsController(IPaymentService payments, IOptions<PaymentOptions> options) : ControllerBase
{
    [HttpPost("success")] public Task<IActionResult> Success(string gateway, CancellationToken ct) => Browser(gateway, CallbackKind.Success, ct);
    [HttpPost("fail")] public Task<IActionResult> Fail(string gateway, CancellationToken ct) => Browser(gateway, CallbackKind.Fail, ct);
    [HttpPost("cancel")] public Task<IActionResult> Cancel(string gateway, CancellationToken ct) => Browser(gateway, CallbackKind.Cancel, ct);

    /// <summary>Instant Payment Notification. 200 = processed (or already processed); non-200 makes the gateway retry later.</summary>
    [HttpPost("ipn")]
    public async Task<IActionResult> Ipn(string gateway, CancellationToken ct)
    {
        var result = await payments.HandleCallbackAsync(gateway, CallbackKind.Ipn, await ReadFormAsync(ct), ct);
        return result.Handled ? Ok(new { status = "OK", result.AlreadyProcessed }) : BadRequest(new { status = "REJECTED", result.Message });
    }

    private async Task<IActionResult> Browser(string gateway, CallbackKind kind, CancellationToken ct)
    {
        var result = await payments.HandleCallbackAsync(gateway, kind, await ReadFormAsync(ct), ct);
        var front = options.Value.StorefrontBaseUrl.TrimEnd('/');
        if (result.OrderNumber is null) return Redirect($"{front}/account/orders?payment=error");

        var outcome = result.Status switch
        {
            PaymentAttemptStatus.Paid => "success",
            PaymentAttemptStatus.Cancelled => "cancelled",
            PaymentAttemptStatus.Failed => "failed",
            _ => "pending", // could not be verified yet; the IPN will settle it
        };
        return Redirect($"{front}/account/orders/{Uri.EscapeDataString(result.OrderNumber)}?payment={outcome}");
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadFormAsync(CancellationToken ct)
    {
        if (!Request.HasFormContentType) return new Dictionary<string, string>();
        var form = await Request.ReadFormAsync(ct);
        return form.ToDictionary(k => k.Key, v => v.Value.ToString());
    }
}
