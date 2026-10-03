# TechBazar BD

E-commerce platform for computer & electronics retail in Bangladesh (original brand, functionally inspired by
large local retailers).

| Part | Tech | Folder |
|------|------|--------|
| API | ASP.NET Core Web API, .NET 9, EF Core 9, SQL Server, Identity + JWT | [`backend/`](backend) |
| Web | Angular 22 storefront (standalone, signals, SSR) | [`frontend/`](frontend) |
| Docs | Architecture notes, ERD | [`docs/`](docs) |

See [`CLAUDE.md`](CLAUDE.md) for conventions and commands, and [`docs/`](docs) for the ERD and architecture.

## Run the API locally
Prerequisites: .NET 9 SDK, SQL Server (Developer/Express/LocalDB) reachable from SSMS, `dotnet tool install -g dotnet-ef --version 9.*`.

```bash
cd backend
# 1. secrets (never committed)
dotnet user-secrets --project src/TechBazar.Api set "Jwt:Key" "$(openssl rand -base64 48)"
dotnet user-secrets --project src/TechBazar.Api set "Seed:AdminPassword" "ChangeMe123"      # optional: creates admin@techbazar.bd
# 2. only if your SQL Server is not the default local instance / Windows auth:
dotnet user-secrets --project src/TechBazar.Api set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=TechBazarBD;User Id=sa;Password=<pw>;TrustServerCertificate=True"
# 3. create the schema (also happens automatically on first run in Development)
dotnet ef database update -p src/TechBazar.Infrastructure -s src/TechBazar.Api
# 4. run (seeds categories, brands, 66 products, coupons on first start)
dotnet run --project src/TechBazar.Api          # Swagger: https://localhost:7080/swagger
```
Apply the new Phase 3 migration with the same `dotnet ef database update` (it is incremental; existing data is kept and the
seeder adds the new CPU-cooler category/products). The admin account is `admin@techbazar.bd` with the `Seed:AdminPassword` you set.

### Password reset & email verification
Both flows email a one-time link (`/reset-password#token=…`, `/verify-email#token=…`). In development the emails are only logged (the full text,
including the link, is at Debug level in the API console). With Docker, **Mailpit** catches them: open http://localhost:8025.
For real delivery set SMTP (credentials as secrets, never in git):
```bash
dotnet user-secrets --project src/TechBazar.Api set "Email:Smtp:Host" "smtp.your-provider.com"
dotnet user-secrets --project src/TechBazar.Api set "Email:Smtp:FromAddress" "no-reply@your-domain"
dotnet user-secrets --project src/TechBazar.Api set "Email:Smtp:Username" "<user>"        # TLS is mandatory when a username is set
dotnet user-secrets --project src/TechBazar.Api set "Email:Smtp:Password" "<password>"
dotnet user-secrets --project src/TechBazar.Api set "Account:StorefrontBaseUrl" "https://www.your-domain"   # origin used in emailed links
```
Optional: `Account:RequireVerifiedEmailForCheckout=true` to require a verified address before ordering. Design and threat model: [`docs/security.md`](docs/security.md).

### Online payments (SSLCommerz sandbox)
Cash on delivery works out of the box. To enable "Pay online", create a free sandbox store, then set
`SslCommerz:StoreId`, `SslCommerz:StorePassword` (user-secrets) and `Payments:PublicApiBaseUrl` to a URL the gateway can reach
(e.g. an ngrok tunnel to the API: SSLCommerz POSTs `/api/v1/payments/sslcommerz/ipn|success|fail|cancel` there). Set the
IPN URL in the sandbox merchant panel to `<PublicApiBaseUrl>/api/v1/payments/sslcommerz/ipn`. Without credentials the online
option is shown as unavailable. Emails are log-only in development (`IEmailSender`, see the API console).

Connect SSMS to the same server, database **TechBazarBD**. Tables: `Products`, `Categories`, `ProductSpecifications`,
`Users`, `Roles`, `RefreshTokens`, ...

## Run the storefront
Needs Node ≥ 22.22.3 and the API running on http://localhost:5080 (`dotnet run --project src/TechBazar.Api` uses
`applicationUrl` 5080 for http).
```bash
cd frontend && npm install
npm start                                   # http://localhost:4200  (dev server, /api proxied to :5080)
# or the production/SSR build
npm run build && API_URL=http://localhost:5080 npm run serve:ssr:techbazar-web   # http://localhost:4000
```
Register a customer on `/register`. The admin panel is at `/admin` (sign in as the seeded admin). PC Builder: `/builder`.

## Run everything with Docker (SQL Server included)
```bash
cp .env.example .env            # then fill in the three secrets (commands are in the file; never commit .env)
docker compose up --build       # SQL Server + API (migrates & seeds in Development) + storefront
# storefront http://localhost:4000   API/Swagger http://localhost:5080/swagger   admin: admin@techbazar.bd / SEED_ADMIN_PASSWORD
# emails (verification / password reset) are caught by Mailpit: http://localhost:8025
```
Details, resetting the database and what is *not* production-ready: [`docs/docker.md`](docs/docker.md). The Docker images were written
and validated statically here (`docker compose config`); there was no Docker daemon in the authoring sandbox, so run the first build yourself.

## Quality gates (what CI runs)
```bash
# backend (from /backend)
dotnet build -c Release -warnaserror && dotnet format --verify-no-changes && dotnet test -c Release
dotnet list package --vulnerable --include-transitive      # must report nothing
# frontend (from /frontend)
npm run lint && npm test -- --watch=false && npm run build && npm audit --omit=dev --audit-level=high
# end-to-end (Playwright: real browser -> SSR server -> real API pipeline on a SQLite test host; Chromium via `npm run e2e:install`)
npm run e2e                                                  # browse > filter > cart > register > checkout (COD) + security + guards + builder
```
`.github/workflows/ci.yml` runs these as `backend`, `frontend`, `e2e` and `docker` jobs; Dependabot is configured. In the authoring sandbox set
`PW_CHROMIUM_PATH` to a preinstalled Chromium (see the header of `frontend/playwright.config.ts`).

## Security & performance
* [`docs/security.md`](docs/security.md): OWASP review, **every finding and fix with its regression test**, accepted risks, production checklist.
* [`docs/performance.md`](docs/performance.md): measured query counts per endpoint, indexes, compression, bundle budgets.
* Production configuration that matters: `Jwt__Key`, `Auth__RefreshCookie__Secure=Always`, `ForwardedHeaders__KnownNetworks__0=<your proxy CIDR>`,
  `HSTS=1` (web), `Security__HttpsRedirection=false` (API behind a TLS proxy), `Swagger__Enabled` unset.

## Tests
```bash
cd backend  && dotnet test                  # 398 unit + 165 integration (SQLite in-memory for tests only)
cd frontend && npm test -- --watch=false    # 370 Vitest specs
cd frontend && npm run e2e                  # 16 Playwright tests
```
Screenshots of the storefront: [`docs/screenshots/`](docs/screenshots). Design notes: [`docs/frontend.md`](docs/frontend.md), [`docs/architecture.md`](docs/architecture.md).
