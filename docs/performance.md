# Performance review (Phase 4)

All numbers were measured in this repository's tests (see `QueryBudgetTests`, `ResponseSizeTests`, `DashboardServiceTests`).
SQL Server was not available in the sandbox, so query *counts* and translated SQL are measured; execution plans are not.

## N+1 detection method
An EF Core command interceptor counts SQL commands per request. An N+1 shows up as a count that **grows with the amount of data**, so the
tests compare a small and a large result and require equal counts.

| Endpoint | SQL commands | Scales with data? |
|---|---|---|
| `GET /products` (5 vs 60 rows) | 2 / 2 | no |
| `GET /products/{slug}` (2 products) | 6 / 6 | no |
| `GET /products/facets` (processor vs ssd vs all) | 7 / 7 / 6 | no |
| `GET /cart` (1 vs 10 lines), `POST /cart/preview` | 1 / 1 | no |
| `POST /checkout/quote` (1 vs 10 lines) | 3 / 3 | no |
| `POST /orders` (1 vs 10 lines) | 7 SELECT / 7 SELECT (+1 INSERT & UPDATE per row on SQLite; SQL Server batches writes) | reads: no |
| `GET /orders` (3 vs 15 rows), `GET /orders/{n}` | 2 / 2, 3 | no |
| `POST /pc-builder/evaluate` (2 vs 7 parts) | 1 / 1 | no |
| `GET /admin/products` (5 vs 100), `/admin/orders`, `/admin/dashboard` | 2 / 2, 2, 5 | no |

**Result: no N+1 found** - the earlier phases already used projections. Every read path is `AsNoTracking`/projected (audited per service);
tracked queries are mutations only.

## Fixes
| # | Finding | Fix | Evidence |
|---|---|---|---|
| P1 | **Admin dashboard loaded every order in the window and every line of every sale into memory**, then aggregated in C# (cost grows with sales; 365-day view = all orders of the year per page view) | Aggregation in SQL: `GROUP BY CONVERT(date, CreatedAt)` and a joined top-5 `GROUP BY ProductId`; rows transferred are now bounded (one per day, <= 5 top products, <= 20 low-stock, 8 recent orders, one per status), independent of how many orders exist | 9 behavioural pins written first (`DashboardServiceTests`), still green after the rewrite; SQL Server translation asserted (`SqlServerTranslationTests`) |
| P2 | Index gaps against real query shapes | Migration `Phase4Indexes` (idempotent SQL script reviewed): `ProductSpecifications(Key,Value) INCLUDE(ProductId)` (spec filters/facets become covering); `Orders(Status,CreatedAt)` (admin lists/status counts); `Orders(CreatedAt) INCLUDE(Status,GrandTotal)` (dashboard window); `OrderItems(OrderId) INCLUDE(ProductId,Quantity,LineTotal)` (order detail + top products); `Products(StockStatus,StockQuantity)` (in-stock filter + low-stock) | script printed in review; **plan-level gains not measured (no SQL Server here)** |
| P3 | No response compression | Brotli/gzip (API) and gzip/br (SSR); auth endpoints excluded | listing `31,234 B -> 7,512 B` (-76%), categories -77%, facets -60%, detail -54% |
| P4 | `max-age=1y` on **every** static file, including unhashed images/favicons (stale for a year after a change) | immutable only for content-hashed files; others 1 h | `server-security.spec`, E2E header check |
| P5 | Bundle budgets 600 kB / 1 MB (a 2.5x regression would pass) | initial 450/550 kB, any script 350/450 kB, component style 6/12 kB. Today: **384 kB raw / 107 kB transferred** | build fails with `bundle initial exceeded maximum budget` when squeezed (verified) |
| P6 | SSR proxy: no upstream timeout, no compression | 30 s timeout (504), compression | review |
| P7 | Images | already `loading=lazy` (14/19), explicit width/height on all 19 (no layout shift), LCP images `fetchpriority=high`; the 2 admin previews now lazy | audit script |

## Known limitations
Uploaded originals are not resized; search is a `LIKE` scan; output cache is in-memory per instance (use Redis for scale-out); uploads
live on local disk.
