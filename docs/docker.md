# Docker (local development stack)

`docker-compose.yml` at the repo root runs **SQL Server 2022 + API + SSR storefront** for local work. It is a dev
convenience, not a production deployment (see the last section).

## Prerequisites
Docker Engine / Docker Desktop with the Compose plugin (v2+), ~4 GB RAM free for SQL Server.

## First run
```bash
cp .env.example .env            # .env is git-ignored
# fill in the three secrets (see comments in the file), e.g.
#   MSSQL_SA_PASSWORD=Tb-$(openssl rand -hex 12)-Aa1
#   JWT_KEY=$(openssl rand -base64 48 | tr -d '+/=\n')
#   SEED_ADMIN_PASSWORD=$(openssl rand -hex 12)
docker compose up --build
```
Compose refuses to start if one of the three variables is missing. On first start the API (running as
`ASPNETCORE_ENVIRONMENT=Development`, with `Seed__ApplyMigrations` and `Seed__Enabled` = true) applies the EF Core
migrations and runs the idempotent seeder (categories, brands, products, coupons, admin account). The web container waits
for the API health check, so the first start takes a minute or two.

| What | URL |
|------|-----|
| Storefront (SSR) | http://localhost:4000 |
| API + Swagger | http://localhost:5080/swagger , health: http://localhost:5080/health |
| SQL Server | `localhost,1433`, user `sa`, the `MSSQL_SA_PASSWORD` you chose, database `TechBazarBD` (SSMS works) |
| Admin login | `admin@techbazar.bd` / `SEED_ADMIN_PASSWORD`, then open `/admin` |

All ports are published on `127.0.0.1` only.

## Everyday commands
```bash
docker compose logs -f api            # API logs (Serilog console)
docker compose up --build -d web      # rebuild just the storefront after code changes
docker compose down                   # stop, keep data
docker compose down -v                # stop AND delete the database + uploaded images (reset)
```
**Resetting the DB** = `docker compose down -v && docker compose up --build`; migrations and the seeder run again on a
fresh volume. Changing `MSSQL_SA_PASSWORD` after the first start has no effect on an existing volume: reset it.

## Images
* `backend/Dockerfile` (context `backend/`): SDK 9 build, `aspnet:9.0` runtime, non-root `app` user, listens on 8080,
  uploads in `/data/uploads` (volume), health check against `/health` (done with bash `/dev/tcp`, because the runtime image
  has no curl/wget).
* `frontend/Dockerfile` (context `frontend/`): Node 24 build (`npm ci`, `ng build`), `node:24-slim` runtime as the `node`
  user, serves `dist/techbazar-web` on 4000. The SSR bundle is self-contained, so no `node_modules` ship in the image.
  Env: `API_URL` (where `/api` and `/uploads` are proxied), `NG_ALLOWED_HOSTS` (comma-separated Host allow-list; SSR
  answers 400 for any other Host), `PORT`.

## Behind a reverse proxy / TLS
Terminate TLS in front (nginx, Caddy, Traefik, a cloud load balancer) and forward to the `web` container only; it already
proxies `/api` and `/uploads` to the API, so the API need not be public. Then:
* add your domain to `NG_ALLOWED_HOSTS` (e.g. `shop.example.com`) and, if the proxy sets `X-Forwarded-*`, to the
  `NG_TRUST_PROXY_HEADERS` list (see Angular SSR docs);
* set `Payments__PublicApiBaseUrl` / `Payments__StorefrontBaseUrl` to the public https origin (payment gateways call back);
* the API sees the Node proxy as the client, so per-IP rate limiting uses the forwarded address only if you configure
  forwarded headers.

## NOT production-ready
* Runs the API in `Development` (Swagger on, verbose logs) with auto-migrate and seeding. Production should apply
  migrations as a deploy step (`dotnet ef migrations script` / bundle) and run with `Seed__Enabled=false`.
* SQL Server in a single container with the `sa` account and `TrustServerCertificate=True`; use a managed/HA database,
  a least-privilege login and a real certificate.
* Secrets come from a `.env` file; use a secret store (Docker/Kubernetes secrets, Key Vault) in production.
* Uploads live on a local volume (single instance only); use object storage + CDN to scale out.
* No TLS, no backups, no resource limits, no log shipping, images are not pinned by digest.
* Images were validated statically; build them in CI before relying on them.

## Mail (Mailpit)
`mailpit` catches every email the API sends (verification, password reset, order mails): http://localhost:8025. Nothing leaves your machine.
For a real relay set `Email__Smtp__Host/Port/Security/Username/Password/FromAddress` (use `StartTls`/`SslOnConnect`; a username without TLS is refused at startup).
