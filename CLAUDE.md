# TechBazar BD — project guide for Claude sessions

Original e-commerce platform for computer & electronics retail in Bangladesh
(functionally inspired by large local retailers; **never copy any third-party
names, logos, text or images**). Brand: **TechBazar BD**. Currency: BDT (৳).

## Stack (strict)
| Layer     | Tech |
|-----------|------|
| Backend   | ASP.NET Core Web API, **.NET 9** (`net9.0`), C#, EF Core 9 (SQL Server provider) |
| Frontend  | **Angular 22**, standalone components, signals, `@if/@for`, lazy routes, SSR (`@angular/ssr`) |
| Database  | SQL Server (dev uses SSMS). Schema **only** via EF Core migrations; data via seeder |
| Auth      | ASP.NET Core Identity + JWT access token + rotating refresh tokens |
| Logging   | Serilog |
| Tests     | xUnit; integration via `WebApplicationFactory` on SQLite in-memory (**tests only**) |

## Repository layout
```
/backend    TechBazar.sln (Clean Architecture)
  src/TechBazar.Domain          entities, enums, no dependencies
  src/TechBazar.Application     DTOs, services, validators, abstractions (EF Core abstractions only, no provider)
  src/TechBazar.Infrastructure  DbContext, Fluent configs, migrations, seeder, Identity, JWT
  src/TechBazar.Api             controllers, middleware, DI/pipeline wiring, Program.cs
  tests/TechBazar.UnitTests
  tests/TechBazar.IntegrationTests
/frontend   Angular 22 storefront (`techbazar-web`): see docs/frontend.md
/docs       architecture notes, ERD (Mermaid)
```
Dependency rule: Api → Infrastructure → Application → Domain. Domain never references
anything. Application never references Identity, ASP.NET or a DB provider.

## Conventions
- API base path `/api/v1/...`; JSON camelCase; enums serialised as strings.
- Errors are always RFC 7807 `ProblemDetails` (global `IExceptionHandler` + status-code pages).
  Throw `NotFoundException`, `ConflictException`, `AuthenticationFailedException`,
  FluentValidation `ValidationException` — never return ad-hoc error shapes.
- Validation: FluentValidation validators in Application, executed by `ValidationFilter`.
- Money: `decimal(18,2)` (global convention). Timestamps: UTC `DateTime`.
- Entities derive from `BaseEntity` (Id, CreatedAt, UpdatedAt, IsDeleted, DeletedAt).
  Deletes are soft; a global query filter hides deleted rows. Audit fields are set in
  `ApplicationDbContext.SaveChangesAsync`. Use `IgnoreQueryFilters()` deliberately.
- Slug and SKU have **unique filtered indexes** (`[IsDeleted] = 0`).
- Fluent API only (`IEntityTypeConfiguration<T>` in `Infrastructure/Persistence/Configurations`);
  no data annotations on entities.
- No MediatR/AutoMapper: plain services + projection (`Select`) to DTOs.
- Catalog GET endpoints use output cache policies `Catalog` / `Autocomplete`.
- Auth endpoints use rate-limit policy `auth`.
- Secrets (JWT key, admin password, connection strings with passwords) live in
  **user-secrets / env vars**, never in git. Tests inject config in-memory.
- Spec attributes for the future PC Builder use canonical keys: `Socket`, `RAM Type`
  (`DDR4`/`DDR5`), `TDP` (W, CPU/GPU), `Wattage` (PSU), `Form Factor`.
- **Totals are server-only.** Cart/checkout/builder prices are recomputed from the database on every request
  (`OrderCalculator`, `ShippingRules`); request bodies carry choices (ids, method, coupon code), never amounts.
  Home-delivery zone comes from the *address district* (`Dhaka` = inside), not from the client's method choice.
- Pure business rules live in Application as static classes with exhaustive unit tests: `OrderCalculator`, `ShippingRules`,
  `OrderStateMachine`, `BuildCompatibilityChecker`, `SpecRules`. Status changes go through `IOrderFulfilment.TransitionAsync`
  (state machine, stock release, coupon release, refund flag, history row, email).
- Payments: `IPaymentGateway` (`cod`, `sslcommerz`). Callbacks (`/api/v1/payments/{gateway}/success|fail|cancel|ipn`) are anonymous,
  re-verified by the gateway (signature + validation API), amount/currency checked against the `Payment` row, and processed
  idempotently (`Payment.Version` optimistic concurrency; replays return 200 "already processed"). Never trust browser redirects.
- Optimistic concurrency uses an int `Version` (`IVersioned`: Product, Coupon, Payment) bumped in `SaveChangesAsync`; stock/coupon
  writers retry on `DbUpdateConcurrencyException`.
- Admin API is `/api/v1/admin/*`, policy `AdminOnly` (401 anonymous, 403 customer). Uploads: content-sniffed (`ImageSniffer`,
  PNG/JPEG/GIF/WebP, no SVG), GUID file names, stored by `IFileStorage` under `Storage:RootPath`, served at `/uploads`.
- Email goes through `IEmailSender` (dev: `LoggingEmailSender`). Sending must never fail an order.
- Seeder is additive: it adds missing categories/brands/products/coupons and never overwrites admin edits or resurrects deletions.
- Small logical commits, imperative messages. PR only when asked.

### Frontend conventions (`/frontend/src/app`)
- Standalone components only, `ChangeDetectionStrategy.OnPush`, **signals** for state, `@if/@for` control flow,
  zoneless. Every route is lazy (`loadComponent`). No UI library: design tokens live in `src/styles.scss`.
- Layout: `core/` (models, services, interceptors, guards, util) · `shared/` (dumb components) · `layout/` (shell)
  · `features/<name>/<name>.page.ts` (routed pages). Models in `core/models/api.models.ts` mirror the API JSON.
- **All API calls use the relative base `/api/v1`** (`core/config.ts`). `proxy.conf.json` (dev) and `src/server.ts` (SSR
  server) forward it to the .NET API (`API_URL`). Never hardcode an API origin.
- HTTP interceptor order is `loading → error → auth` (auth innermost so it sees raw 401s). Errors reach callers as
  `ApiError` (`core/models/api.models.ts`); use `HttpContext` tokens `SKIP_AUTH`, `SILENT_ERRORS`, `BACKGROUND`.
- Auth: access token **in memory only**, rotating refresh token in localStorage (`tb.refresh.v1`). Guards await
  `AuthService.whenReady()` (silent session restore) before deciding.
- Guest state (`tb.cart.v1`, `tb.wishlist.v1`, `tb.compare.v1`) is loaded in `App` via `afterNextRender` so the first
  client render matches the SSR HTML. Cart/wishlist merge into the server on login (`POST /cart/merge`, `/wishlist/merge`).
- Listing filters live in the URL (`core/util/listing-query.ts` is the one parser/serializer); never keep filter state
  elsewhere. Money is always rendered through `formatBdt` / `bdt` pipe (`৳1,25,000`, hand-rolled, not `Intl`, so SSR = browser).
- SSR gotchas learned the hard way: do not set `innerHTML` on SVG (server DOM throws) → icons use the sprite in
  `index.html`; `resource.error()` wraps the thrown value (`.cause`) and `resource.value()` **throws** in the error
  state → use `apiErrorOf` / `safeValue` (`core/util/resource.ts`); user-specific pages are `RenderMode.Client`.
- SEO: set per-page meta with `SeoService.set()` (title, description, canonical, robots, JSON-LD). Filtered / searched
  listings are `noindex,follow`. Unknown product/category pages call `setStatus(404)`.
- Phase 3 pages: `features/checkout` (quote from `/checkout/quote`; place → COD goes to tracking, online `Redirector.to(url)` to the
  gateway), `features/account/{addresses,orders,order-detail}` (tracking timeline comes from the API), `features/builder`
  (server evaluates every change; share link `/builder?b=CODE`), `features/admin` (own lazy shell behind `adminGuard`; every admin
  call is also enforced server-side). Gateway return lands on `/account/orders/:number?payment=success|failed|cancelled|pending`.
- `/api` **and `/uploads`** are proxied to the API (`proxy.conf.json`, `src/server.ts`).
- Tests: Vitest via `ng test` (jsdom). Fake only the timers you need (`vi.useFakeTimers({ toFake: [...] })`) because
  faking `setTimeout` freezes Angular's zoneless scheduler; never `await fixture.whenStable()` while an HTTP request you
  must flush is pending.

## Commands
```bash
# backend (from /backend)
dotnet restore && dotnet build
dotnet test
dotnet user-secrets --project src/TechBazar.Api set "Jwt:Key" "<>=32 random chars>"
dotnet user-secrets --project src/TechBazar.Api set "Seed:AdminPassword" "<strong pw>"
# optional SSLCommerz sandbox (https://developer.sslcommerz.com/registration/): never commit these
dotnet user-secrets --project src/TechBazar.Api set "SslCommerz:StoreId" "<sandbox store id>"
dotnet user-secrets --project src/TechBazar.Api set "SslCommerz:StorePassword" "<sandbox store password>"
dotnet user-secrets --project src/TechBazar.Api set "Payments:PublicApiBaseUrl" "https://<public tunnel to the API>"
dotnet ef migrations add <Name> -p src/TechBazar.Infrastructure -s src/TechBazar.Api -o Persistence/Migrations
dotnet ef database update      -p src/TechBazar.Infrastructure -s src/TechBazar.Api
dotnet run --project src/TechBazar.Api      # https://localhost:7080 / http://localhost:5080, Swagger at /swagger
# frontend (from /frontend)  -- needs Node >= 22.22.3 (Angular CLI 22)
npm install
npm start                                   # http://localhost:4200 (proxies /api -> http://localhost:5080)
npm test -- --watch=false                   # Vitest unit tests (ng test)
npm run build                               # production + SSR build
API_URL=http://localhost:5080 npm run serve:ssr:techbazar-web   # http://localhost:4000 (SSR + /api proxy)
```
Seeding runs on API start in Development (`Seed:Enabled`), idempotent.

## Environment note
The cloud sandbox only has the .NET 10 SDK; projects still target `net9.0`. Run tests there with
`DOTNET_ROLL_FORWARD=Major dotnet test`. On a normal .NET 9 machine no env var is needed.

The sandbox's Node is 22.22.0 (< 22.22.3), so Angular CLI commands there run with a newer Node binary
(`npm i node@24` in a scratch dir and put its `bin` first on `PATH`). A normal dev machine just needs a supported Node.
SSR rejects unknown `Host` headers: add your domain(s) to `security.allowedHosts` in `angular.json` (or set `NG_ALLOWED_HOSTS`).

## Roadmap
Phase 1 (done): domain, migration, seed, catalog + auth APIs, tests, Angular scaffold.
Phase 3 (done): checkout (addresses, shipping, coupons), orders + tracking + emails, COD + SSLCommerz sandbox, PC Builder with
server-side compatibility, Admin panel (dashboard, product/category/brand/coupon CRUD, uploads, order status).
Phase 2 (done): storefront (home, listing with URL-synced filters, product, compare, wishlist, cart with guest→server
merge, auth, profile, order-history placeholder) + cart/wishlist/profile/onSale APIs.
Next: reviews (write), admin user management, refunds via gateway API, bKash/Nagad direct, HttpOnly refresh cookie, real SMTP sender.
