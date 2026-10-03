namespace TechBazar.Api.Middleware;

/// <summary>
/// Defence-in-depth response headers for an API that only ever returns JSON / images:
/// no MIME sniffing, no framing, no referrer leakage, no powerful browser features, and a CSP that forbids everything
/// (a JSON response never needs to load or run anything). Authenticated and auth responses are never stored by caches.
/// Swagger UI (development only) needs scripts and is exempt from the CSP.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private static readonly string[] CspExemptPrefixes = ["/swagger"];
    private const string ApiCsp = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var ctx = (HttpContext)state;
            var h = ctx.Response.Headers;
            h.XContentTypeOptions = "nosniff";
            h.XFrameOptions = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), microphone=(), payment=(), usb=()";
            h["Cross-Origin-Resource-Policy"] = "same-site";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            if (!CspExemptPrefixes.Any(p => ctx.Request.Path.StartsWithSegments(p)) && !h.ContainsKey("Content-Security-Policy"))
                h.ContentSecurityPolicy = ApiCsp;

            // Never let a shared cache keep tokens, orders, carts or admin data. Public catalog responses keep their own policy.
            var sensitive = ctx.Request.Headers.ContainsKey("Authorization")
                            || ctx.Request.Path.StartsWithSegments("/api/v1/auth")
                            || ctx.Request.Path.StartsWithSegments("/api/v1/admin")
                            || !HttpMethods.IsGet(ctx.Request.Method);
            if (sensitive && !h.ContainsKey("Cache-Control")) h.CacheControl = "no-store";
            return Task.CompletedTask;
        }, context);
        return next(context);
    }
}
