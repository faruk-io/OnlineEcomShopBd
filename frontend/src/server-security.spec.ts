import { createHash } from 'node:crypto';
import { appendForwardedFor, baseSecurityHeaders, bodyLimitFor, contentSecurityPolicy, inlineScriptHashes, isSafeUploadPath } from './server-security';

const sha = (s: string) => `'sha256-${createHash('sha256').update(s, 'utf8').digest('base64')}'`;

const PAGE = `<!DOCTYPE html><html><head>
<script type="text/javascript" id="ng-event-dispatch-contract">(()=>{window.x=1})();</script>
<link rel="modulepreload" href="chunk.js">
<script type="application/ld+json">{"@type":"Product","name":"</scr"}</script>
</head><body><app-root></app-root>
<script>window.__jsaction_bootstrap(document.body,"ng",["click"]);</script>
<script src="main-ABC.js" type="module"></script>
<script id="ng-state" type="application/json">{"a":1}</script>
<script></script>
<script>document.querySelectorAll('link[data-beasties-media]').forEach(function(l){l.media=l.dataset.beastiesMedia});</script>
</body></html>`;

describe('inlineScriptHashes', () => {
  it('hashes exactly the inline executable scripts (not data blocks, external scripts or empty tags)', () => {
    const hashes = inlineScriptHashes(PAGE);
    expect(hashes).toHaveLength(3);
    expect(hashes).toContain(sha('(()=>{window.x=1})();'));
    expect(hashes).toContain(sha('window.__jsaction_bootstrap(document.body,"ng",["click"]);'));
    expect(hashes).toContain(sha("document.querySelectorAll('link[data-beasties-media]').forEach(function(l){l.media=l.dataset.beastiesMedia});"));
  });

  it('is stable for identical content and different for changed content', () => {
    expect(inlineScriptHashes('<script>a()</script><script>a()</script>')).toHaveLength(1);
    expect(inlineScriptHashes('<script>a()</script>')).not.toEqual(inlineScriptHashes('<script>b()</script>'));
  });

  it('never whitelists an injected script that was not in the rendered page', () => {
    const csp = contentSecurityPolicy(PAGE);
    expect(csp).not.toContain(sha('alert(1)'));
  });
});

describe('contentSecurityPolicy', () => {
  const csp = contentSecurityPolicy(PAGE);
  const directive = (name: string) => csp.split('; ').find((d) => d.startsWith(name + ' ')) ?? '';

  it('allows scripts only from this origin and the hashed inline ones, with no unsafe-inline / unsafe-eval', () => {
    const script = directive('script-src');
    expect(script).toContain("'self'");
    expect(script.match(/'sha256-/g)).toHaveLength(3);
    expect(script).not.toMatch(/unsafe-inline|unsafe-eval|\*|http:/);
  });

  it('forbids plugins, framing, base-tag hijack and foreign form targets', () => {
    expect(directive('object-src')).toBe("object-src 'none'");
    expect(directive('frame-ancestors')).toBe("frame-ancestors 'none'");
    expect(directive('base-uri')).toBe("base-uri 'self'");
    expect(directive('form-action')).toBe("form-action 'self'");
    expect(directive('connect-src')).toBe("connect-src 'self'");
    expect(directive('default-src')).toBe("default-src 'self'");
  });
});

describe('baseSecurityHeaders', () => {
  it('always sends the defensive headers', () => {
    const h = baseSecurityHeaders(false);
    expect(h['X-Content-Type-Options']).toBe('nosniff');
    expect(h['X-Frame-Options']).toBe('DENY');
    expect(h['Referrer-Policy']).toBe('strict-origin-when-cross-origin');
    expect(h['Permissions-Policy']).toContain('camera=()');
  });
  it('sends HSTS only when explicitly enabled', () => {
    expect(baseSecurityHeaders(false)['Strict-Transport-Security']).toBeUndefined();
    expect(baseSecurityHeaders(true)['Strict-Transport-Security']).toContain('max-age=31536000');
  });
});

describe('isSafeUploadPath', () => {
  it.each(['/products/2026/10/abc123.png', '/a.webp', '/x/y/z.jpg'])('accepts %s', (p) => expect(isSafeUploadPath(p)).toBe(true));
  it.each(['/../swagger', '/a/../../health', '/%2e%2e/x', '/a%2fb', '/a b', '/a?x=1', '/a#b', '//evil.com/x', '', '/', '/..', '/a/./b', '/a\\b'])(
    'rejects %j',
    (p) => expect(isSafeUploadPath(p)).toBe(false),
  );
});

describe('appendForwardedFor', () => {
  it('starts a chain with the direct peer', () => expect(appendForwardedFor(undefined, '10.0.0.5')).toBe('10.0.0.5'));
  it('appends to an existing chain instead of overwriting it', () => expect(appendForwardedFor('203.0.113.9', '10.0.0.5')).toBe('203.0.113.9, 10.0.0.5'));
  it('keeps a spoofed leading entry harmless: the real peer is still the last (rightmost) hop the API will evaluate first', () =>
    expect(appendForwardedFor('1.2.3.4', '198.51.100.7').split(', ').at(-1)).toBe('198.51.100.7'));
  it('copes with repeated headers and unknown peers', () => expect(appendForwardedFor(['1.1.1.1', '2.2.2.2'], undefined)).toBe('1.1.1.1,2.2.2.2, unknown'));
});

describe('bodyLimitFor', () => {
  it('allows image uploads more room than ordinary JSON', () => {
    expect(bodyLimitFor('/api/v1/admin/uploads/images')).toBe('6mb');
    expect(bodyLimitFor('/api/v1/orders')).toBe('1mb');
  });
});
