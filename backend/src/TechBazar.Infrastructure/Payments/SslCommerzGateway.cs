using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechBazar.Application.Payments;
using TechBazar.Domain.Enums;

namespace TechBazar.Infrastructure.Payments;

public sealed class SslCommerzOptions
{
    public const string SectionName = "SslCommerz";
    /// <summary>Sandbox credentials come from https://developer.sslcommerz.com/registration/ ; keep them in user-secrets.</summary>
    public string? StoreId { get; set; }
    public string? StorePassword { get; set; }
    public bool IsSandbox { get; set; } = true;
    public string SandboxBaseUrl { get; set; } = "https://sandbox.sslcommerz.com";
    public string LiveBaseUrl { get; set; } = "https://securepay.sslcommerz.com";
    public string BaseUrl => (IsSandbox ? SandboxBaseUrl : LiveBaseUrl).TrimEnd('/');
}

/// <summary>
/// SSLCommerz hosted-checkout integration (sandbox by default).
///
/// Init:   POST {base}/gwprocess/v4/api.php  -> JSON with GatewayPageURL the browser is redirected to.
/// Callbacks (success/fail/cancel URL and the server-to-server IPN) POST the same form fields. A "paid" claim is only
/// believed after BOTH (1) the verify_sign hash matches (md5 over the fields named in verify_key + md5(store_passwd)) and
/// (2) SSLCommerz's validation API confirms val_id for our store, returning the tran_id and the amount actually charged.
/// </summary>
public sealed class SslCommerzGateway(HttpClient http, IOptions<SslCommerzOptions> options, ILogger<SslCommerzGateway> logger) : IPaymentGateway
{
    private readonly SslCommerzOptions _o = options.Value;

    public string Name => PaymentService.OnlineGateway;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_o.StoreId) && !string.IsNullOrWhiteSpace(_o.StorePassword);
    public bool Supports(PaymentMethod method) => method == PaymentMethod.Online;

    public async Task<PaymentInitResult> InitiateAsync(PaymentInitRequest r, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["store_id"] = _o.StoreId!, ["store_passwd"] = _o.StorePassword!,
            ["total_amount"] = r.Amount.ToString("0.00", CultureInfo.InvariantCulture), ["currency"] = r.Currency, ["tran_id"] = r.TransactionId,
            ["success_url"] = r.SuccessUrl, ["fail_url"] = r.FailUrl, ["cancel_url"] = r.CancelUrl, ["ipn_url"] = r.IpnUrl,
            ["cus_name"] = r.CustomerName, ["cus_email"] = r.CustomerEmail, ["cus_phone"] = r.CustomerPhone,
            ["cus_add1"] = r.AddressLine, ["cus_city"] = r.City, ["cus_country"] = "Bangladesh",
            ["shipping_method"] = "NO", ["product_name"] = r.ProductSummary, ["product_category"] = "Electronics", ["product_profile"] = "general",
        };
        using var response = await http.PostAsync($"{_o.BaseUrl}/gwprocess/v4/api.php", new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return new PaymentInitResult(false, null, $"Gateway HTTP {(int)response.StatusCode}");

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var status = Str(root, "status");
            var url = Str(root, "GatewayPageURL");
            if (string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
                return new PaymentInitResult(true, url, null);
            return new PaymentInitResult(false, null, Str(root, "failedreason") ?? "Gateway refused the session");
        }
        catch (JsonException)
        {
            return new PaymentInitResult(false, null, "Gateway returned an unreadable response");
        }
    }

    public async Task<PaymentVerification> VerifyCallbackAsync(CallbackKind kind, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default)
    {
        payload.TryGetValue("tran_id", out var tranId);
        payload.TryGetValue("status", out var status);
        if (string.IsNullOrWhiteSpace(tranId)) return Rejected(null, "Missing tran_id");

        var claimsPaid = status is not null && (status.Equals("VALID", StringComparison.OrdinalIgnoreCase) || status.Equals("VALIDATED", StringComparison.OrdinalIgnoreCase));
        if (!claimsPaid || kind is CallbackKind.Fail or CallbackKind.Cancel)
        {
            // A negative message can only move a still-PENDING attempt to failed/cancelled; it can never touch a paid one.
            var cancelled = kind == CallbackKind.Cancel || string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
            return new PaymentVerification(true, cancelled ? PaymentOutcome.Cancelled : PaymentOutcome.Failed, tranId, null, null, null,
                payload.TryGetValue("error", out var err) && !string.IsNullOrWhiteSpace(err) ? err : status);
        }

        // ---- (1) signature -------------------------------------------------------------------------
        if (!IsConfigured) return Rejected(tranId, "Gateway not configured");
        if (!HasValidSignature(payload, _o.StorePassword!)) return Rejected(tranId, "Invalid verify_sign");
        if (!payload.TryGetValue("val_id", out var valId) || string.IsNullOrWhiteSpace(valId)) return Rejected(tranId, "Missing val_id");

        // ---- (2) ask SSLCommerz whether val_id is really valid for OUR store -----------------------
        var url = $"{_o.BaseUrl}/validator/api/validationserverAPI.php?val_id={Uri.EscapeDataString(valId)}" +
                  $"&store_id={Uri.EscapeDataString(_o.StoreId!)}&store_passwd={Uri.EscapeDataString(_o.StorePassword!)}&v=1&format=json";
        try
        {
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return Rejected(tranId, $"Validation API HTTP {(int)response.StatusCode}");
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            var vStatus = Str(root, "status");
            if (!(string.Equals(vStatus, "VALID", StringComparison.OrdinalIgnoreCase) || string.Equals(vStatus, "VALIDATED", StringComparison.OrdinalIgnoreCase)))
                return Rejected(tranId, $"Validation API says {vStatus}");
            if (!string.Equals(Str(root, "tran_id"), tranId, StringComparison.Ordinal)) return Rejected(tranId, "tran_id mismatch");

            // Amount in BDT as charged (currency_amount is in the card's currency, "amount" in the store currency).
            var amountText = Str(root, "currency_amount") is { } ca && string.Equals(Str(root, "currency_type"), "BDT", StringComparison.OrdinalIgnoreCase) ? ca : Str(root, "amount");
            decimal? amount = decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var a) ? a : null;
            var currency = Str(root, "currency_type") ?? Str(root, "currency") ?? "BDT";
            return new PaymentVerification(true, PaymentOutcome.Paid, tranId, amount, currency, Str(root, "bank_tran_id") ?? valId, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(ex, "SSLCommerz validation call failed for {Tran}", tranId);
            return Rejected(tranId, "Could not reach the validation API");
        }
    }

    /// <summary>
    /// verify_sign = md5( "k1=v1&k2=v2&…" ) over the fields listed (comma separated) in verify_key plus
    /// store_passwd=md5(StorePassword), sorted by key name. Compared in constant time.
    /// </summary>
    public static bool HasValidSignature(IReadOnlyDictionary<string, string> payload, string storePassword)
    {
        if (!payload.TryGetValue("verify_sign", out var sign) || string.IsNullOrWhiteSpace(sign)) return false;
        if (!payload.TryGetValue("verify_key", out var keys) || string.IsNullOrWhiteSpace(keys)) return false;

        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!payload.TryGetValue(key, out var value)) return false;
            fields[key] = value;
        }
        fields["store_passwd"] = Md5Hex(storePassword);

        var expected = Md5Hex(string.Join("&", fields.Select(kv => $"{kv.Key}={kv.Value}")));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(sign.Trim().ToLowerInvariant()));
    }

    /// <summary>Builds a correctly signed payload (used by tests and the local sandbox simulator).</summary>
    public static string Sign(IReadOnlyDictionary<string, string> fieldsToSign, string storePassword)
    {
        var fields = new SortedDictionary<string, string>(fieldsToSign.ToDictionary(k => k.Key, k => k.Value), StringComparer.Ordinal) { ["store_passwd"] = Md5Hex(storePassword) };
        return Md5Hex(string.Join("&", fields.Select(kv => $"{kv.Key}={kv.Value}")));
    }

    private static PaymentVerification Rejected(string? tranId, string reason) => new(false, PaymentOutcome.Failed, tranId, null, null, null, reason);
    private static string Md5Hex(string s) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString() : null;
}
