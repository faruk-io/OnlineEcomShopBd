# syntax=docker/dockerfile:1
# Build context = /frontend   (docker build -t techbazar-web ./frontend)
# Angular CLI 22 needs Node >= 22.22.3, hence the Node 24 base for both stages.

# ---- build ---------------------------------------------------------------
FROM node:26-bookworm-slim AS build
WORKDIR /app
ENV NG_CLI_ANALYTICS=false CI=true

COPY package.json package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci --no-audit --no-fund

COPY . .
# Production + SSR build -> dist/techbazar-web/{browser,server}
RUN npm run build

# ---- runtime -------------------------------------------------------------
# The SSR server bundle (dist/techbazar-web/server/server.mjs) is self-contained: express, @angular/ssr and the app
# are bundled by esbuild (its only bare import is node:module). So no node_modules are copied, which keeps the image small.
FROM node:26-slim AS final
WORKDIR /app
ENV NODE_ENV=production \
    PORT=4000 \
    API_URL=http://api:8080 \
    NG_ALLOWED_HOSTS=localhost

COPY --from=build --chown=node:node /app/dist/techbazar-web ./dist/techbazar-web

USER node
EXPOSE 4000

# Probe a static asset: it is served by express before SSR, so it needs no API and no Host-header allow-listing.
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=3 \
  CMD ["node", "-e", "require('http').get({host:'127.0.0.1',port:process.env.PORT||4000,path:'/favicon.svg'},r=>process.exit(r.statusCode===200?0:1)).on('error',()=>process.exit(1))"]

CMD ["node", "dist/techbazar-web/server/server.mjs"]
