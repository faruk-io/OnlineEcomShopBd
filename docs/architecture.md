# TechBazar BD — architecture notes

## Layers (Clean Architecture)
```
Api ──► Infrastructure ──► Application ──► Domain
 │            │                 │
 │            │                 └─ DTOs, service interfaces + implementations (query side), validators,
 │            │                    IApplicationDbContext (EF Core abstractions only, no provider)
 │            └─ ApplicationDbContext (SQL Server), Fluent configs, migrations, Identity, JWT, seeder
 └─ Controllers, ValidationFilter, GlobalExceptionHandler, Swagger/CORS/rate-limit/output-cache wiring
```
Why Application references `Microsoft.EntityFrameworkCore` (but no provider): the catalog is read-heavy and the
filtering/sorting/paging is expressed as composable `IQueryable` projections. Hiding EF behind repositories would
force materialising or re-inventing a query DSL. Infrastructure supplies the provider; tests swap it.

## Request pipeline (order matters)
`ExceptionHandler → StatusCodePages → Serilog request log → Swagger → HTTPS → CORS → RateLimiter → Authentication →
Authorization → OutputCache → Controllers (ValidationFilter → action)`

- **Errors**: everything becomes RFC 7807 `ProblemDetails` (with `traceId`). `ValidationException` → 400 with an
  `errors` map keyed by camelCase field; `NotFoundException` → 404; `ConflictException` → 409;
  `AuthenticationFailedException` → 401; unhandled → 500 (stack trace only in Development).
- **Validation**: FluentValidation validators are resolved per action argument by `ValidationFilter`.
- **Caching**: `Catalog` (60 s) and `Autocomplete` (30 s) output-cache policies, varied by the full query string.
  Prices/stock can therefore be up to 60 s stale; use the `catalog` tag to evict when admin writes are added.
- **Rate limiting**: fixed window per client IP on `/api/v1/auth/*` (default 10 requests / 60 s, configurable).
  Behind a reverse proxy configure `ForwardedHeaders`, otherwise every client shares the proxy's IP.

## Authentication
- Access token: JWT (HS256, 15 min) with `sub`, `email`, `name`, `role` claims.
- Refresh token: 64 random bytes (base64), only its SHA-256 is stored. **Rotated on every use**; presenting an already
  rotated token revokes all of that user's live refresh tokens (theft detection). Rotation is an atomic
  compare-and-set (`UPDATE ... WHERE RevokedAt IS NULL`) so concurrent refreshes cannot both succeed.
- Lockout after 5 failed logins (15 min). Login errors are deliberately identical for unknown user / bad password.
- The SPA should keep the access token in memory and the refresh token in an `HttpOnly; Secure; SameSite` cookie once
  the storefront is built (API currently takes it in the JSON body to stay client-agnostic).

## Catalog query contract (`GET /api/v1/products`)
| Param | Meaning |
|-------|---------|
| `category` | category slug, includes all descendants |
| `brand` (repeatable / comma list) | brand slugs, OR |
| `minPrice`, `maxPrice` | on `EffectivePrice` (sale price when present) |
| `inStock=true` | `StockStatus = InStock` only |
| `spec=Key:Value` (repeatable) | same key → OR, different keys → AND, e.g. `spec=Socket:AM5&spec=RAM Type:DDR5` |
| `q` | tokens AND-ed across name, SKU, brand, category (LIKE, wildcard-safe) |
| `sort` | `popularity` (default) · `newest` · `price_asc` · `price_desc` |
| `page`, `pageSize` | 1-based, `pageSize` ≤ 60 (default 20) |

`GET /products/facets?category=` returns the brand counts, price range and per-spec value counts to render the filter sidebar.

## Database
- Schema is created **only** by EF migrations (`Persistence/Migrations`). Seed data is inserted by `DataSeeder`
  (idempotent, runs at API start when `Seed:Enabled`), not by migrations, because 60+ products with specs are far easier
  to maintain/diff as code and the seeder can hash the admin password via Identity.
- Money is `decimal(18,2)` via a global convention. `Product.EffectivePrice` is a real column maintained on save so
  filtering/sorting by price is index-friendly.
- Tests use SQLite in-memory: `ApplicationDbContext` maps `decimal` → `double` **only** when the provider is SQLite
  (SQLite cannot ORDER BY decimals). `SqlServerTranslationTests` compiles the catalog queries with the SQL Server provider
  so provider-specific regressions are still caught.
