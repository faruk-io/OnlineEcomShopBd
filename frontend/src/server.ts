import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import compression from 'compression';
import express from 'express';
import { join } from 'node:path';
import { appendForwardedFor, baseSecurityHeaders, bodyLimitFor, cacheControlForStatic, contentSecurityPolicy, isSafeUploadPath } from './server-security';

const browserDistFolder = join(import.meta.dirname, '../browser');

const app = express();
const angularApp = new AngularNodeAppEngine();

// Hardening applies to the built server (`node server.mjs`), not to the Angular dev server that merely imports `reqHandler`.
const standalone = isMainModule(import.meta.url) || !!process.env['pm_id'];
app.disable('x-powered-by');
app.disable('etag'); // weak validators on dynamic SSR output only cost CPU

const hsts = process.env['HSTS'] === '1'; // enable ONLY when the public site is served over HTTPS
const securityHeaders = baseSecurityHeaders(hsts);
app.use((_req, res, next) => {
  for (const [name, value] of Object.entries(securityHeaders)) res.setHeader(name, value);
  next();
});

// gzip/brotli for HTML, JS, CSS, JSON, SVG. The token-bearing auth endpoints are skipped on purpose (BREACH-style attacks).
app.use(compression({ filter: (req, res) => !req.originalUrl.startsWith('/api/v1/auth') && compression.filter(req, res) }));

/**
 * Same-origin API gateway. The browser (and the server-side renderer) call a relative `/api/v1/...`;
 * this forwards it to the .NET API (`API_URL`, default http://localhost:5080). In production you can instead put
 * both behind one reverse proxy (nginx/IIS/Azure Front Door) and drop this block.
 */
const API_URL = (process.env['API_URL'] ?? 'http://localhost:5080').replace(/\/$/, '');
const HOP_BY_HOP = new Set(['connection', 'keep-alive', 'transfer-encoding', 'upgrade', 'host', 'content-length', 'te', 'trailer', 'content-encoding']);

const UPSTREAM_TIMEOUT_MS = 30_000;

app.use('/api', (req, res, next) => express.raw({ type: () => true, limit: bodyLimitFor(req.originalUrl) })(req, res, next), async (req, res) => {
  try {
    const headers = new Headers();
    for (const [name, value] of Object.entries(req.headers)) {
      if (value !== undefined && !HOP_BY_HOP.has(name)) headers.set(name, Array.isArray(value) ? value.join(',') : value);
    }
    // Append (never overwrite) so a trusted proxy chain in front of this server survives; the API only believes entries from the
    // proxies it is configured to trust (ForwardedHeaders:KnownProxies / KnownNetworks).
    headers.set('x-forwarded-for', appendForwardedFor(req.headers['x-forwarded-for'], req.socket.remoteAddress));
    headers.set('x-forwarded-proto', (req.headers['x-forwarded-proto'] as string | undefined) ?? req.protocol);
    const hasBody = !['GET', 'HEAD'].includes(req.method) && Buffer.isBuffer(req.body) && req.body.length > 0;
    const upstream = await fetch(`${API_URL}${req.originalUrl}`, {
      method: req.method,
      headers,
      body: hasBody ? new Uint8Array(req.body as Buffer) : undefined,
      redirect: 'manual',
      signal: AbortSignal.timeout(UPSTREAM_TIMEOUT_MS),
    });
    res.status(upstream.status);
    upstream.headers.forEach((value, name) => {
      if (!HOP_BY_HOP.has(name) && name !== 'set-cookie') res.setHeader(name, value);
    });
    // fetch() folds multiple Set-Cookie headers into one string; forward them individually (the refresh-token cookie depends on this).
    for (const cookie of upstream.headers.getSetCookie()) res.append('set-cookie', cookie);
    res.send(Buffer.from(await upstream.arrayBuffer()));
  } catch (error) {
    const timedOut = error instanceof Error && error.name === 'TimeoutError';
    res.status(timedOut ? 504 : 502).type('application/problem+json').send({ status: timedOut ? 504 : 502, title: timedOut ? 'The API timed out.' : 'The API is unreachable.' });
  }
});

/** Admin-uploaded product images are stored by (and served from) the API host; stream them through unchanged. */
app.use('/uploads', async (req, res) => {
  if (!['GET', 'HEAD'].includes(req.method) || !isSafeUploadPath(req.path)) {
    res.status(404).end();
    return;
  }
  try {
    const upstream = await fetch(`${API_URL}/uploads${req.path}`, { method: req.method, redirect: 'manual', signal: AbortSignal.timeout(UPSTREAM_TIMEOUT_MS) });
    res.status(upstream.status);
    upstream.headers.forEach((value, name) => {
      if (!HOP_BY_HOP.has(name)) res.setHeader(name, value);
    });
    res.send(Buffer.from(await upstream.arrayBuffer()));
  } catch {
    res.status(502).end();
  }
});

/**
 * Serve static files from /browser
 */
app.use(
  express.static(browserDistFolder, {
    index: false,
    redirect: false,
    setHeaders: (res, filePath) => res.setHeader('Cache-Control', cacheControlForStatic(filePath)),
  }),
);

/**
 * Handle all other requests by rendering the Angular application.
 */
app.use((req, res, next) => {
  angularApp
    .handle(req)
    .then(async (response) => {
      if (!response) return next();
      if (!standalone || !(response.headers.get('content-type') ?? '').includes('text/html')) return writeResponseToNodeResponse(response, res);
      // Hash-based CSP computed from the page that was actually rendered (inline hydration / event-replay scripts differ per build).
      const html = await response.text();
      const headers = new Headers(response.headers);
      headers.set('Content-Security-Policy', contentSecurityPolicy(html));
      return writeResponseToNodeResponse(new Response(html, { status: response.status, statusText: response.statusText, headers }), res);
    })
    .catch(next);
});

/**
 * Start the server if this module is the main entry point, or it is ran via PM2.
 * The server listens on the port defined by the `PORT` environment variable, or defaults to 4000.
 */
if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = process.env['PORT'] || 4000;
  app.listen(port, (error) => {
    if (error) {
      throw error;
    }

    console.log(`Node Express server listening on http://localhost:${port}`);
  });
}

/**
 * Request handler used by the Angular CLI (for dev-server and during build) or Firebase Cloud Functions.
 */
export const reqHandler = createNodeRequestHandler(app);
