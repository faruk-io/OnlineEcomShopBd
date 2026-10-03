# Storefront architecture (Angular 22)

## Routes
| URL | Page | Render | Notes |
|-----|------|--------|-------|
| `/` | Home | SSR | hero slider, featured categories, deals (`onSale=true`), new arrivals, brands |
| `/category/:slug` | Listing | SSR | filters in query string; subcategory pills; `noindex` when filtered |
| `/shop`, `/search?q=` | Listing | SSR | all products / full-text search (`q` ≥ 2 chars) |
| `/product/:slug` | Product | SSR | gallery, buy box, specs tables, reviews, related, JSON-LD, 404 status |
| `/compare` | Compare | SSR | up to 4 products, "differences only" |
| `/wishlist`, `/cart` | Wishlist, Cart | browser | guest data lives in localStorage |
| `/login`, `/register` | Auth | browser | `returnUrl` is validated (no open redirects) |
| `/checkout` | Checkout | browser | auth required; address, shipping, payment, coupon; server quote |
| `/account/profile`, `/account/addresses`, `/account/orders`, `/account/orders/:number` | Account | browser | guarded; tracking timeline, cancel, pay again; `?payment=` banner |
| `/builder`, `/builder?b=CODE` | PC Builder | SSR | server-evaluated compatibility, share link, add build to cart |
| `/admin/**` | Admin panel | browser | separate lazy shell behind `adminGuard` (+ server-side role checks) |

## Listing URL contract (shareable / SEO)
`/category/processor?brand=intel&brand=amd&minPrice=10000&maxPrice=60000&inStock=true&onSale=true&spec=Socket:LGA1700&sort=price_asc&page=2`

`core/util/listing-query.ts` is the only place that parses/serialises it; defaults are omitted. Filter changes
`router.navigate` (no local filter state), pagination uses real `<a rel="prev|next">` links.

## Data flow
```
Component ─ signals/rxResource ─► CatalogService ─► HttpClient ─► [loading → error → auth] ─► /api/v1 (proxy) ─► .NET API
```
* **Auth**: `AuthService` keeps the JWT in memory, the rotating refresh token in `localStorage`. `authInterceptor`
  attaches the token and on `401` performs a **single-flight** refresh then replays the request once; if the refresh
  fails the session is cleared and the original 401 is surfaced.
* **Cart / wishlist**: guest → localStorage; signed in → server. On login the guest copy is merged
  (`POST /cart/merge`: quantities add up, capped at 10, unavailable products skipped) and removed. A failed merge keeps
  the local copy. The guest cart page re-prices through the anonymous `POST /cart/preview`.
* **Errors**: every failed API call becomes an `ApiError` (ProblemDetails). 5xx / network / 429 / 403 toast globally;
  validation (400), 401 and 404 are handled by the caller (forms show field errors, pages show not-found).
* **Loading**: `LoadingService` counts foreground requests → top progress bar (autocomplete is `BACKGROUND`).

## SSR & SEO
* Catalog pages are rendered per request (`RenderMode.Server`); user-specific pages are client-only.
* `SeoService` writes title, description, Open Graph, canonical, robots and JSON-LD (`Product` + `Offer` in BDT, an
  `aggregateRating` only when real reviews exist). Unknown product/category → HTTP 404 + `noindex`.
* HTTP transfer cache avoids a second API call after hydration (both sides use the same relative URL).
* Browser-only state (cart, counters, session) loads in `afterNextRender` so hydration never mismatches.

## Accessibility
Skip link; landmarks (`header`, `nav` ×N with labels, `main`, `footer`); focus moves to `<main>` after route changes;
search is an ARIA 1.2 combobox (`aria-activedescendant`, live-region count); mega menu opens by hover **and** a real
button (Esc closes); carousel follows the WAI-ARIA pattern and respects `prefers-reduced-motion`; price slider is two
native range inputs plus number fields; filters are real `fieldset/legend`, `details/summary`; toasts are `role=status`;
forms link errors with `aria-describedby`/`aria-invalid`; tables use `caption` + `th scope`; visible focus ring.

## Testing
* `npm test -- --watch=false` — 187 Vitest specs: BDT formatting, URL ↔ filter mapping, auth/refresh single-flight,
  interceptors (token, 401→refresh→replay, error mapping, loading), cart/wishlist/compare services incl. guest→server
  merge, SEO tags, components (card, search combobox with debounce + keyboard, pagination, slider, header/mega menu) and
  pages (listing ↔ URL ↔ API, product, cart, login/register).
* The repo's CI-less verification also drove the real stack (SQLite-backed API + SSR server + Chromium) through 49
  browser checks: hydration without errors, mega menu, autocomplete, filters/URL sync, price slider by keyboard,
  pagination, cart/wishlist/compare as guest, register → cart merged, session restore on hard reload, silent refresh on
  401, logout, route guard, 404 status, no horizontal overflow on a 390 px viewport. Screenshots: `docs/screenshots/`.

## Known gaps
* Online payments need SSLCommerz sandbox credentials (not available in CI); the gateway is covered by fake-HTTP tests.
* Seed product images are generated SVG placeholders; admin uploads are stored on local disk (`Storage:RootPath`) — use blob storage + CDN to scale out.
* Reviews are display-only (no write endpoint yet); product data has none, so ratings show "No reviews yet".
* Facet counts are per category (not recomputed for the currently selected filters).
* The refresh token is in `localStorage`; move it to an `HttpOnly; Secure; SameSite` cookie (API + server.ts proxy) before production.
* Behind a proxy, enable `ForwardedHeaders` in the API so auth rate limiting sees client IPs, not the SSR server's.
