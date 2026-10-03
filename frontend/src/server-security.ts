import { createHash } from 'node:crypto';

/**
 * Pure helpers for the Node SSR server (`server.ts`): security headers, a hash-based Content-Security-Policy,
 * and request sanitising for the API / uploads gateway. Kept free of Express so they can be unit-tested.
 */

/** Scripts that are data (JSON-LD, Angular's transfer state) are never executed, so they need no CSP entry. */
const EXECUTABLE_SCRIPT = /<script(?![^>]*\ssrc=)(?![^>]*type="(?:application\/(?:ld\+)?json|importmap|speculationrules)")[^>]*>([\s\S]*?)<\/script>/gi;

/** `'sha256-…'` source expressions for every inline executable <script> in the rendered page (event replay, hydration bootstrap, …). */
export function inlineScriptHashes(html: string): string[] {
  const hashes = new Set<string>();
  for (const m of html.matchAll(EXECUTABLE_SCRIPT)) {
    const body = m[1];
    if (body.trim().length === 0) continue;
    hashes.add(`'sha256-${createHash('sha256').update(body, 'utf8').digest('base64')}'`);
  }
  return [...hashes];
}

/**
 * Strict CSP for the storefront: scripts only from this origin plus the exact inline scripts Angular rendered (by hash, so injected
 * markup can never run), no plugins, no framing, no foreign form targets. `style-src 'unsafe-inline'` is needed for Angular's
 * component styles; styles cannot execute code. Images may come from any https host because admins can reference CDN images.
 */
export function contentSecurityPolicy(html: string): string {
  const scripts = ["'self'", ...inlineScriptHashes(html)].join(' ');
  return [
    "default-src 'self'",
    `script-src ${scripts}`,
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: https:",
    "font-src 'self'",
    "connect-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
    "manifest-src 'self'",
    "worker-src 'self'",
  ].join('; ');
}

/** Headers for every response of the SSR server. HSTS only when explicitly enabled (it must never be sent over plain HTTP by accident). */
export function baseSecurityHeaders(hsts: boolean): Record<string, string> {
  return {
    'X-Content-Type-Options': 'nosniff',
    'X-Frame-Options': 'DENY',
    'Referrer-Policy': 'strict-origin-when-cross-origin',
    'Permissions-Policy': 'accelerometer=(), camera=(), geolocation=(), gyroscope=(), microphone=(), payment=(), usb=()',
    'Cross-Origin-Opener-Policy': 'same-origin',
    ...(hsts ? { 'Strict-Transport-Security': 'max-age=31536000; includeSubDomains' } : {}),
  };
}

/**
 * Upload files are served from the API host through this server. Only plain relative file paths are forwarded:
 * no `..` segments (which would let `/uploads/../swagger` reach other API routes), no encoded slashes, no query tricks.
 */
export function isSafeUploadPath(path: string): boolean {
  if (!/^\/[A-Za-z0-9._\-/]+$/.test(path)) return false;
  // empty segments (`//`) are rejected as well: they signal protocol-relative / normalisation tricks
  return !path.slice(1).split('/').some((seg) => seg === '' || seg === '..' || seg === '.');
}

/** Appends the direct peer to any existing chain (never overwrites it) so the API can walk the chain through its trusted proxies. */
export function appendForwardedFor(prior: string | string[] | undefined, peer: string | undefined): string {
  const chain = (Array.isArray(prior) ? prior.join(',') : (prior ?? '')).trim();
  const who = peer && peer.length > 0 ? peer : 'unknown';
  return chain ? `${chain}, ${who}` : who;
}

/** The admin image upload is allowed to be larger than ordinary JSON bodies (the API caps images at 5 MB). */
export function bodyLimitFor(path: string): string {
  return path.startsWith('/api/v1/admin/uploads/') ? '6mb' : '1mb';
}

/**
 * Content-hashed build output (`main-3KNJFOGF.js`, `styles-2C7T3UHH.css`, `chunk-ABC123xy.js`) can be cached for a year because any change
 * produces a new file name. Everything else (placeholder images, favicon, robots.txt, ...) keeps its name when it changes, so it must
 * revalidate within the hour or users would keep a stale copy for a year.
 */
export function cacheControlForStatic(fileName: string): string {
  const hashed = /[-.][A-Za-z0-9_-]{8,}\.(?:m?js|css|woff2?)$/.test(fileName);
  return hashed ? 'public, max-age=31536000, immutable' : 'public, max-age=3600';
}
