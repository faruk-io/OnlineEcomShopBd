# Security review (Phase 4)

Scope: the whole repository (API, SSR server, Angular app, Docker/CI). Method: read every controller, service and pipeline stage;
enumerate endpoints by reflection; scan tree **and git history** for secrets; run `dotnet list package --vulnerable` and `npm audit`;
write regression tests for each finding (so a fix cannot silently regress). Mapped to the OWASP Top 10 (2021).

## Findings and fixes

| # | Sev. | OWASP | Finding | Fix | Proof |
|---|------|-------|---------|-----|-------|
| S1 | High | A02/A07 | **Refresh token in `localStorage`**: any XSS = persistent account takeover | Refresh token is now an `HttpOnly; SameSite=Strict; Path=/api/v1/auth` cookie (`Secure` when HTTPS / `Auth:RefreshCookie:Secure=Always`). The cookie is honoured only together with a custom `X-Refresh-Mode` header (forces a CORS pre-flight, so cross-site forgery is impossible). The body carries `refreshToken: null`. Legacy tokens in `localStorage` are exchanged once, then deleted. API clients can still use body mode | `SecurityApiTests` (cookie flags, rotation, replay, CSRF-without-header), `auth.service.spec`, E2E `security.spec` (JS cannot see it, survives reload, logout) |
| S2 | High | A01 | **No deny-by-default**: public catalog controllers were public only because nothing said otherwise; a new controller without `[Authorize]` would ship open | `FallbackPolicy = RequireAuthenticatedUser`; public endpoints say `[AllowAnonymous]` | `SecurityApiTests`: every endpoint declares access; the **anonymous allow-list is frozen** (adding a public endpoint fails the test until reviewed); every non-public endpoint returns 401 anonymously and every admin endpoint 403 for a customer |
| S3 | High | A04/A05 | **Per-IP rate limits useless behind the SSR proxy**: all visitors share the proxy's address (auth limit = 10 logins/min *site-wide*), and naive "trust forwarded headers" lets clients spoof their IP | `ForwardedHeaders:KnownProxies` / `KnownNetworks` (CIDR) config; SSR server **appends** to `X-Forwarded-For` (never overwrites); startup warning in Production when unset. **My first version cleared the default trust list, and an empty list means "trust everyone"** - caught by my own test, fixed to keep the loopback default | `ForwardedHeadersTests` (5): trusted chain yields the real visitor, spoofed leading entry ignored, untrusted peer cannot set address/scheme, nothing configured trusts nobody |
| S4 | Med | A07 | **Logout needed a valid access token**: after the 15 min token expired, logout silently left the (rotated) refresh token alive | `POST /auth/logout` revokes by possession of the refresh token (body or cookie), always 204; new `POST /auth/logout-all` + "Sign out on all devices" in the account menu | `AuthApiTests`, `SecurityApiTests`, E2E (stolen cookie value is rejected after logout; second device is signed out) |
| S5 | Med | A05 | **Swagger served in every environment** (full route/DTO map) | Development only, or `Swagger:Enabled=true` | `SecurityApiTests.UnknownAndSensitivePaths…` |
| S6 | Med | A05 | **No security headers** on API or SSR HTML; `X-Powered-By: Express` | API: nosniff, X-Frame-Options, Referrer-Policy, Permissions-Policy, CORP/COOP, `default-src 'none'` CSP, `Cache-Control: no-store` for tokens/private data. SSR: same plus a **hash-based CSP** computed from the rendered page's inline scripts (no `unsafe-inline`/`unsafe-eval` for scripts), HSTS opt-in (`HSTS=1`), `X-Powered-By` removed | unit specs for the CSP builder; E2E in real Chromium: app runs with zero violations **and injected inline script / inline handler / `javascript:` URL are blocked** |
| S7 | Med | A04 | **No global rate limit; coupon brute force** through the authenticated quote endpoint | Global per-client limiter (600/min) + `checkout` policy (30/min per user). Test exposed that `UseRateLimiter` ran *before* authentication, so "per user" collapsed to per IP: moved after `UseAuthentication` | `SecurityApiTests` (global 429 + `Retry-After`; second customer unaffected) |
| S8 | Med | A09 | **PII in logs**: the log-only email sender (the active sender in production) wrote full emails (name, phone, address, order) to console and 14-day log files | Information: masked recipient, subject, length. Body only at Debug | `EmailLoggingTests` |
| S9 | Low | A02 | JWT algorithm not pinned | `ValidAlgorithms = HS256`, `RequireSignedTokens`, `RequireExpirationTime` | tests: HS512/HS384 signed with the *right* key rejected, `alg:none`, wrong key/issuer/audience/expired |
| S10 | Low | A07 | Login timing revealed unknown/disabled accounts (no password hash computed) | dummy hash on those paths | review + generic-message test |
| S11 | Low | A09 | SSLCommerz validation call has `store_passwd` in its URL (their API design); default HttpClient logging could record it | `RemoveAllLoggers()` on that client | review |
| S12 | Low | A05 | Kestrel `Server` header, 30 MB default body, no header timeout | `AddServerHeader=false`, 2 MB body (image upload endpoint 5 MB), 15 s header timeout | `SecurityApiTests` headers |
| S13 | Low | A05/A10 | SSR `/uploads` proxy forwarded `..` paths (reach other API routes); `/api` proxy folded multiple `Set-Cookie`, had no upstream timeout, and its 1 MB limit **rejected admin image uploads > 1 MB** (functional bug) | strict path allow-list, GET/HEAD only; `getSetCookie()`; 30 s timeout; 6 MB limit for the upload route only | `server-security.spec` (path/limit/XFF helpers), E2E |
| S14 | Low | A05 | Uploaded files served without a restrictive CSP | `Content-Security-Policy: default-src 'none'; sandbox` + CORP on `/uploads` | `AdminApiTests` upload |
| S15 | Info | A02 | Compression of token-bearing responses (BREACH class) | auth endpoints excluded from API and SSR compression | `ResponseSizeTests`, `SecurityApiTests` |
| S16 | Info | A05 | `.gitignore` lacked key/cert patterns | `*.pfx *.p12 *.pem *.key *.crt secrets.json` | `git check-ignore` |

## Checked and found sound (with evidence)
- **SQL injection (A03)**: zero raw SQL (`FromSql*`/`ExecuteSql*` grep); EF parameterises everything; `LIKE` input is escaped (`TextSearch.Escape`, `%`/`_`/`[` tested). 14 hostile payloads (`'; DROP TABLE…`, `' OR '1'='1`, `WAITFOR`, `%`, `[a-z]`, 5 000 chars, …) x 10 endpoints never return 5xx or change data (`HostileInputNeverCausesServerErrors_OrChangesData`).
- **XSS (A03)**: no `innerHTML`/`bypassSecurityTrust*`/`document.write` in the app (grep); JSON-LD escapes `<`; email HTML is encoded; Angular templates escape by default; CSP above is the backstop.
- **Secrets (A02/A05)**: no credentials in tree or in git history (pattern scan + file-name history scan); secrets only via user-secrets / env; compose requires `${VAR:?}`.
- **Components (A06)**: `dotnet list package --vulnerable --include-transitive`: none. `npm audit`: 0. CI fails on either.
- **Broken access control (A01)**: users cannot read/modify others' addresses, orders, carts (IDOR tests incl. ordering with someone else's address id); admin endpoints all `AdminOnly`.
- **Input validation (A03/A04)**: validators on every request type, collection caps (cart 50 lines, 24 build parts, 20 spec / 30 brand filters), page sizes clamped, malformed/oversized/wrong-content-type bodies are 4xx.
- **Auth (A07)**: PBKDF2 hashing, password policy, lockout after 5 failures, refresh tokens 512-bit random, SHA-256 at rest, atomic rotation, **reuse of a rotated token revokes every session**, generic login errors.
- **Payments**: callbacks verified (signature + validation API), amount/currency checked against the server total, idempotent, never trust browser redirects (Phase 3 tests).
- **Error handling (A05)**: RFC 7807 everywhere; 500s never include exception text outside Development.

## Accepted risks / not done (be aware)
- Access tokens stay valid until they expire (15 min) after logout / deactivation: no per-request user lookup (would add a DB hit per call).
- XSS cannot *steal* the refresh cookie, but script running in the page can still call `/auth/refresh` and act as the user while the page is open. The CSP is the control for that.
- `style-src 'unsafe-inline'` (Angular component styles). Styles cannot execute script.
- No email verification, password reset or MFA yet. Registration reveals whether an email exists (409); a locked account says so.
- `AllowedHosts` is `*` (the API builds no URLs from the Host header; the SSR server enforces `allowedHosts`).
- Admin edits (products, coupons) are not audit-logged; only order status changes keep who/when.
- Search is `LIKE '%term%'` (index-unfriendly). Fine to ~100k products; use SQL Server full-text search beyond that.
- Uploaded images are served as uploaded (up to 5 MB). Put a CDN / image resizer in front before launch.
- Not executed in this sandbox: Docker builds (no daemon), GitHub Actions, SQL Server (translation is compiled in unit tests), SSLCommerz sandbox.

## Production checklist
1. `Jwt__Key` (>= 32 random chars), DB password, `Seed__AdminPassword` from a secret store; seeding off (`Seed__Enabled=false`).
2. TLS terminates at your proxy: set `Auth__RefreshCookie__Secure=Always`, `HSTS=1` on the web container, `Security__HttpsRedirection=false` on the API.
3. `ForwardedHeaders__KnownNetworks__0=<proxy/CIDR>` (or `KnownProxies`) on the API; watch for the startup warning.
4. `Cors__AllowedOrigins`, `Payments__PublicApiBaseUrl`, `Payments__StorefrontBaseUrl`, `NG_ALLOWED_HOSTS` set to the real domains.
5. Keep `Swagger__Enabled` unset. Review Serilog sinks/retention. Back up the SQL Server volume.
