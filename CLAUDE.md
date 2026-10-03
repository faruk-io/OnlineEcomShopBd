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
/frontend   Angular workspace (`techbazar-web`)
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
- Small logical commits, imperative messages. PR only when asked.

## Commands
```bash
# backend (from /backend)
dotnet restore && dotnet build
dotnet test
dotnet user-secrets --project src/TechBazar.Api set "Jwt:Key" "<>=32 random chars>"
dotnet user-secrets --project src/TechBazar.Api set "Seed:AdminPassword" "<strong pw>"
dotnet ef migrations add <Name> -p src/TechBazar.Infrastructure -s src/TechBazar.Api -o Persistence/Migrations
dotnet ef database update      -p src/TechBazar.Infrastructure -s src/TechBazar.Api
dotnet run --project src/TechBazar.Api      # https://localhost:7080 / http://localhost:5080, Swagger at /swagger
# frontend (from /frontend)
npm install && npm start                    # http://localhost:4200
npm run build && npm run serve:ssr:techbazar-web
```
Seeding runs on API start in Development (`Seed:Enabled`), idempotent.

## Environment note
The cloud sandbox only has the .NET 10 SDK; projects still target `net9.0`. Run tests there with
`DOTNET_ROLL_FORWARD=Major dotnet test`. On a normal .NET 9 machine no env var is needed.

## Roadmap
Phase 1 (done): domain, migration, seed, catalog + auth APIs, tests, Angular scaffold.
Next: cart/wishlist/checkout/orders APIs, coupons, reviews, admin CRUD, PC Builder compatibility
engine, Angular storefront.
