import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { join } from 'node:path';

const browserDistFolder = join(import.meta.dirname, '../browser');

const app = express();
const angularApp = new AngularNodeAppEngine();

/**
 * Same-origin API gateway. The browser (and the server-side renderer) call a relative `/api/v1/...`;
 * this forwards it to the .NET API (`API_URL`, default http://localhost:5080). In production you can instead put
 * both behind one reverse proxy (nginx/IIS/Azure Front Door) and drop this block.
 */
const API_URL = (process.env['API_URL'] ?? 'http://localhost:5080').replace(/\/$/, '');
const HOP_BY_HOP = new Set(['connection', 'keep-alive', 'transfer-encoding', 'upgrade', 'host', 'content-length', 'te', 'trailer', 'content-encoding']);

app.use('/api', express.raw({ type: () => true, limit: '1mb' }), async (req, res) => {
  try {
    const headers = new Headers();
    for (const [name, value] of Object.entries(req.headers)) {
      if (value !== undefined && !HOP_BY_HOP.has(name)) headers.set(name, Array.isArray(value) ? value.join(',') : value);
    }
    headers.set('x-forwarded-for', req.socket.remoteAddress ?? 'unknown');
    const hasBody = !['GET', 'HEAD'].includes(req.method) && Buffer.isBuffer(req.body) && req.body.length > 0;
    const upstream = await fetch(`${API_URL}${req.originalUrl}`, {
      method: req.method,
      headers,
      body: hasBody ? new Uint8Array(req.body as Buffer) : undefined,
      redirect: 'manual',
    });
    res.status(upstream.status);
    upstream.headers.forEach((value, name) => {
      if (!HOP_BY_HOP.has(name)) res.setHeader(name, value);
    });
    res.send(Buffer.from(await upstream.arrayBuffer()));
  } catch {
    res.status(502).type('application/problem+json').send({ status: 502, title: 'The API is unreachable.' });
  }
});

/**
 * Serve static files from /browser
 */
app.use(
  express.static(browserDistFolder, {
    maxAge: '1y',
    index: false,
    redirect: false,
  }),
);

/**
 * Handle all other requests by rendering the Angular application.
 */
app.use((req, res, next) => {
  angularApp
    .handle(req)
    .then((response) => (response ? writeResponseToNodeResponse(response, res) : next()))
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
