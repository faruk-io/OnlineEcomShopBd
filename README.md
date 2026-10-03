# TechBazar BD

E-commerce platform for computer & electronics retail in Bangladesh (original brand, functionally inspired by
large local retailers).

| Part | Tech | Folder |
|------|------|--------|
| API | ASP.NET Core Web API, .NET 9, EF Core 9, SQL Server, Identity + JWT | [`backend/`](backend) |
| Web | Angular 22 (standalone, signals, SSR) — scaffold only so far | [`frontend/`](frontend) |
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
Connect SSMS to the same server, database **TechBazarBD**. Tables: `Products`, `Categories`, `ProductSpecifications`,
`Users`, `Roles`, `RefreshTokens`, ...

## Tests
```bash
cd backend && dotnet test      # 96 unit + 39 integration (SQLite in-memory for tests only)
```
## Frontend
```bash
cd frontend && npm install && npm start     # http://localhost:4200
```
