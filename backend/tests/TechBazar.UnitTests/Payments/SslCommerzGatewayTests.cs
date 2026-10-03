using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechBazar.Application.Payments;
using TechBazar.Infrastructure.Payments;

namespace TechBazar.UnitTests.Payments;

public class SslCommerzGatewayTests
{
    private const string StoreId = "techb68f3a1c2d4e5f";
    private const string StorePassword = "techb68f3a1c2d4e5f@ssl";

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static SslCommerzGateway Gateway(FakeHandler h, string? id = StoreId, string? pw = StorePassword) =>
        new(new HttpClient(h), Options.Create(new SslCommerzOptions { StoreId = id, StorePassword = pw }), NullLogger<SslCommerzGateway>.Instance);

    /// <summary>A callback exactly as SSLCommerz would send it, with a correct verify_sign.</summary>
    private static Dictionary<string, string> SignedPayload(string tranId = "TB-261003-ABCDE-1", string amount = "2070.00", string status = "VALID", string password = StorePassword, string valId = "2610031200abcDEF")
    {
        var p = new Dictionary<string, string>
        {
            ["status"] = status, ["tran_id"] = tranId, ["val_id"] = valId, ["amount"] = amount, ["currency"] = "BDT",
            ["bank_tran_id"] = "261003120000BNK", ["card_type"] = "BKASH-BKash", ["store_amount"] = "2018.25",
            ["verify_key"] = "tran_id,amount,currency,status,val_id,bank_tran_id",
        };
        var toSign = p["verify_key"].Split(',').ToDictionary(k => k, k => p[k]);
        p["verify_sign"] = SslCommerzGateway.Sign(toSign, password);
        return p;
    }

    private static string ValidationOk(string tranId = "TB-261003-ABCDE-1", string amount = "2070.00", string status = "VALID", string currency = "BDT") =>
        $$"""{"status":"{{status}}","tran_id":"{{tranId}}","val_id":"2610031200abcDEF","amount":"{{amount}}","currency_type":"{{currency}}","currency_amount":"{{amount}}","bank_tran_id":"261003120000BNK"}""";

    // ------------------------------------------------------------------ signature
    [Fact]
    public void ASignedPayloadVerifies() => Assert.True(SslCommerzGateway.HasValidSignature(SignedPayload(), StorePassword));

    [Fact]
    public void TamperingWithAnySignedFieldBreaksTheSignature()
    {
        foreach (var field in new[] { "tran_id", "amount", "currency", "status", "val_id", "bank_tran_id" })
        {
            var p = SignedPayload();
            p[field] = p[field] + "x";
            Assert.False(SslCommerzGateway.HasValidSignature(p, StorePassword), field);
        }
    }

    [Fact]
    public void UnsignedFieldsCanChangeWithoutBreakingTheSignature_ButTheyAreNotTrustedAnyway()
    {
        var p = SignedPayload();
        p["store_amount"] = "999999"; // not in verify_key
        Assert.True(SslCommerzGateway.HasValidSignature(p, StorePassword));
    }

    [Fact]
    public void ASignatureMadeWithTheWrongPasswordIsRejected() =>
        Assert.False(SslCommerzGateway.HasValidSignature(SignedPayload(password: "someone-elses-secret"), StorePassword));

    [Fact]
    public void MissingSignatureMaterialIsRejected()
    {
        foreach (var drop in new[] { "verify_sign", "verify_key" })
        {
            var p = SignedPayload(); p.Remove(drop);
            Assert.False(SslCommerzGateway.HasValidSignature(p, StorePassword), drop);
        }
        var q = SignedPayload(); q.Remove("amount"); // verify_key names a field that is absent
        Assert.False(SslCommerzGateway.HasValidSignature(q, StorePassword));
        var empty = SignedPayload(); empty["verify_sign"] = "";
        Assert.False(SslCommerzGateway.HasValidSignature(empty, StorePassword));
    }

    [Fact]
    public void SignatureComparisonIgnoresHexCase()
    {
        var p = SignedPayload(); p["verify_sign"] = p["verify_sign"].ToUpperInvariant();
        Assert.True(SslCommerzGateway.HasValidSignature(p, StorePassword));
    }

    // ------------------------------------------------------------------ verification
    [Fact]
    public async Task ValidSignaturePlusValidationApi_YieldsPaidWithTheGatewaysOwnAmount()
    {
        var h = new FakeHandler(_ => Json(ValidationOk()));
        var v = await Gateway(h).VerifyCallbackAsync(CallbackKind.Ipn, SignedPayload());

        Assert.True(v.IsAuthentic);
        Assert.Equal(PaymentOutcome.Paid, v.Outcome);
        Assert.Equal("TB-261003-ABCDE-1", v.TransactionId);
        Assert.Equal(2070.00m, v.Amount);
        Assert.Equal("BDT", v.Currency);
        Assert.Equal("261003120000BNK", v.GatewayReference);

        var call = Assert.Single(h.Calls).Request;
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.StartsWith("https://sandbox.sslcommerz.com/validator/api/validationserverAPI.php", call.RequestUri!.ToString());
        Assert.Contains("val_id=2610031200abcDEF", call.RequestUri.Query);
        Assert.Contains($"store_id={StoreId}", call.RequestUri.Query);
    }

    [Fact]
    public async Task TheAmountComesFromTheValidationApi_NotFromTheCallbackBody()
    {
        // The (signed) callback claims 2070.00 but the validation API says the customer really paid 1.00.
        var h = new FakeHandler(_ => Json(ValidationOk(amount: "1.00")));
        var v = await Gateway(h).VerifyCallbackAsync(CallbackKind.Ipn, SignedPayload(amount: "2070.00"));
        Assert.Equal(1.00m, v.Amount);
    }

    [Fact]
    public async Task APaidClaimWithABadSignatureNeverReachesTheValidationApi()
    {
        var h = new FakeHandler(_ => Json(ValidationOk()));
        var p = SignedPayload(); p["amount"] = "1.00";
        var v = await Gateway(h).VerifyCallbackAsync(CallbackKind.Success, p);
        Assert.False(v.IsAuthentic);
        Assert.Empty(h.Calls);
    }

    [Theory]
    [InlineData("INVALID_TRANSACTION")]
    [InlineData("FAILED")]
    [InlineData("EXPIRED")]
    public async Task IfTheValidationApiDoesNotConfirm_TheCallbackIsRejected(string status)
    {
        var v = await Gateway(new FakeHandler(_ => Json(ValidationOk(status: status)))).VerifyCallbackAsync(CallbackKind.Ipn, SignedPayload());
        Assert.False(v.IsAuthentic);
    }

    [Fact]
    public async Task AValidationResponseForADifferentTransactionIsRejected()
    {
        var v = await Gateway(new FakeHandler(_ => Json(ValidationOk(tranId: "TB-OTHER-1")))).VerifyCallbackAsync(CallbackKind.Ipn, SignedPayload());
        Assert.False(v.IsAuthentic);
        Assert.Contains("mismatch", v.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task ValidationApiOutagesAreNotTreatedAsPayments(HttpStatusCode code)
    {
        var v = await Gateway(new FakeHandler(_ => Json("{}", code))).VerifyCallbackAsync(CallbackKind.Ipn, SignedPayload());
        Assert.False(v.IsAuthentic);
    }

    [Fact]
    public async Task NetworkErrorsAndGarbageResponsesAreRejected()
    {
        var down = await Gateway(new FakeHandler(_ => throw new HttpRequestException("boom"))).VerifyCallbackAsync(CallbackKind.Ipn, SignedPayload());
        Assert.False(down.IsAuthentic);
        var junk = await Gateway(new FakeHandler(_ => Json("<html>oops</html>"))).VerifyCallbackAsync(CallbackKind.Ipn, SignedPayload());
        Assert.False(junk.IsAuthentic);
    }

    [Fact]
    public async Task AValidClaimWithoutValIdIsRejected()
    {
        var p = SignedPayload(); p.Remove("val_id"); p["verify_key"] = "tran_id,amount,currency,status,bank_tran_id";
        p["verify_sign"] = SslCommerzGateway.Sign(p["verify_key"].Split(',').ToDictionary(k => k, k => p[k]), StorePassword);
        Assert.False((await Gateway(new FakeHandler(_ => Json(ValidationOk()))).VerifyCallbackAsync(CallbackKind.Ipn, p)).IsAuthentic);
    }

    [Theory]
    [InlineData(CallbackKind.Fail, "FAILED", PaymentOutcome.Failed)]
    [InlineData(CallbackKind.Cancel, "CANCELLED", PaymentOutcome.Cancelled)]
    [InlineData(CallbackKind.Ipn, "FAILED", PaymentOutcome.Failed)]
    [InlineData(CallbackKind.Ipn, "CANCELLED", PaymentOutcome.Cancelled)]
    [InlineData(CallbackKind.Ipn, "UNATTEMPTED", PaymentOutcome.Failed)]
    public async Task NegativeStatuses_AreReportedWithoutCallingTheValidationApi(CallbackKind kind, string status, PaymentOutcome expected)
    {
        var h = new FakeHandler(_ => Json("{}"));
        var v = await Gateway(h).VerifyCallbackAsync(kind, new Dictionary<string, string> { ["tran_id"] = "T1", ["status"] = status });
        Assert.True(v.IsAuthentic);
        Assert.Equal(expected, v.Outcome);
        Assert.Empty(h.Calls);
    }

    [Fact]
    public async Task ASuccessUrlHitCannotBeUsedToFakeAFailure_WhenStatusSaysValid_ButKindIsFail()
    {
        // kind=Fail always means "not paid", whatever the body claims, so it can never mark a payment as Paid.
        var v = await Gateway(new FakeHandler(_ => Json(ValidationOk()))).VerifyCallbackAsync(CallbackKind.Fail, SignedPayload());
        Assert.NotEqual(PaymentOutcome.Paid, v.Outcome);
    }

    [Fact]
    public async Task MissingTransactionIdIsRejected()
    {
        var v = await Gateway(new FakeHandler(_ => Json("{}"))).VerifyCallbackAsync(CallbackKind.Ipn, new Dictionary<string, string> { ["status"] = "VALID" });
        Assert.False(v.IsAuthentic);
    }

    [Fact]
    public async Task AnUnconfiguredGatewayCannotConfirmPayments()
    {
        var gw = Gateway(new FakeHandler(_ => Json(ValidationOk())), id: null, pw: null);
        Assert.False(gw.IsConfigured);
        Assert.False((await gw.VerifyCallbackAsync(CallbackKind.Ipn, SignedPayload())).IsAuthentic);
    }

    // ------------------------------------------------------------------ initiation
    private static PaymentInitRequest Init() => new(
        "TB-261003-ABCDE-1", 2070m, "BDT", "TB-261003-ABCDE", "Rahim Uddin", "rahim@example.com", "01712345678", "House 1, Road 2", "Dhaka", "TechBazar BD order",
        "https://api.test/api/v1/payments/sslcommerz/success", "https://api.test/api/v1/payments/sslcommerz/fail",
        "https://api.test/api/v1/payments/sslcommerz/cancel", "https://api.test/api/v1/payments/sslcommerz/ipn");

    [Fact]
    public async Task Initiate_PostsTheServerAmountAndCallbackUrls_AndReturnsTheHostedPageUrl()
    {
        var h = new FakeHandler(_ => Json("""{"status":"SUCCESS","sessionkey":"ABC","GatewayPageURL":"https://sandbox.sslcommerz.com/EasyCheckOut/testcde123"}"""));
        var r = await Gateway(h).InitiateAsync(Init());

        Assert.True(r.Success);
        Assert.Equal("https://sandbox.sslcommerz.com/EasyCheckOut/testcde123", r.RedirectUrl);
        var (req, body) = Assert.Single(h.Calls);
        Assert.Equal("https://sandbox.sslcommerz.com/gwprocess/v4/api.php", req.RequestUri!.ToString());
        var form = System.Web.HttpUtility.ParseQueryString(body!);
        Assert.Equal("2070.00", form["total_amount"]);
        Assert.Equal("BDT", form["currency"]);
        Assert.Equal("TB-261003-ABCDE-1", form["tran_id"]);
        Assert.Equal(StoreId, form["store_id"]);
        Assert.Equal("https://api.test/api/v1/payments/sslcommerz/ipn", form["ipn_url"]);
        Assert.Equal("https://api.test/api/v1/payments/sslcommerz/success", form["success_url"]);
        Assert.Equal("Bangladesh", form["cus_country"]);
    }

    [Fact]
    public async Task Initiate_FormatsAmountsWithAPointRegardlessOfCulture()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("bn-BD");
        try
        {
            var h = new FakeHandler(_ => Json("""{"status":"SUCCESS","GatewayPageURL":"https://x.test/p"}"""));
            await Gateway(h).InitiateAsync(Init() with { Amount = 1250.5m });
            Assert.Contains("total_amount=1250.50", h.Calls[0].Body);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    [Theory]
    [InlineData("""{"status":"FAILED","failedreason":"Invalid Information"}""", "Invalid Information")]
    [InlineData("""{"status":"SUCCESS","GatewayPageURL":"http://insecure.test/pay"}""", "refused")]   // never redirect a customer to plain http
    [InlineData("""{"status":"SUCCESS"}""", "refused")]
    [InlineData("not json", "unreadable")]
    public async Task Initiate_FailuresAreReportedAsFailures(string body, string expectedError)
    {
        var r = await Gateway(new FakeHandler(_ => Json(body))).InitiateAsync(Init());
        Assert.False(r.Success);
        Assert.Null(r.RedirectUrl);
        Assert.Contains(expectedError, r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Initiate_HttpErrorIsAFailure()
    {
        var r = await Gateway(new FakeHandler(_ => Json("{}", HttpStatusCode.ServiceUnavailable))).InitiateAsync(Init());
        Assert.False(r.Success);
    }

    [Fact]
    public void LiveModeUsesTheSecurePayHost()
    {
        var o = new SslCommerzOptions { IsSandbox = false };
        Assert.Equal("https://securepay.sslcommerz.com", o.BaseUrl);
        Assert.Equal("https://sandbox.sslcommerz.com", new SslCommerzOptions().BaseUrl);
    }
}
