# TechBazar BD — storefront (Angular 22)

Standalone components · signals · `@if/@for` · lazy routes · SSR (`@angular/ssr`) · Vitest.

```bash
# Node >= 22.22.3 required by Angular CLI 22
npm install

# 1) start the API (see ../README.md), default http://localhost:5080
# 2) dev server with live reload; /api is proxied to the API (proxy.conf.json)
npm start                      # http://localhost:4200

# production build + server-side rendering
npm run build
API_URL=http://localhost:5080 PORT=4000 npm run serve:ssr:techbazar-web   # http://localhost:4000

npm test -- --watch=false      # unit tests (Vitest + jsdom)
```

* The browser and the SSR renderer call a **relative** `/api/v1/...`. In dev `proxy.conf.json` forwards it; in the built
  server `src/server.ts` forwards it to `API_URL`. Behind nginx/IIS you can proxy `/api` to the API instead.
* SSR checks the `Host` header: add production domains to `security.allowedHosts` in `angular.json`.
* More: [`../docs/frontend.md`](../docs/frontend.md).
