import { expect, test, uniqueEmail } from './support/fixtures';
import { RegisterPage } from './support/pages';

/**
 * Browser-level proof of the hardening in Phase 4: what a real Chromium enforces (CSP), what JavaScript can and cannot see
 * (HttpOnly refresh cookie) and that sessions still survive a reload through that cookie.
 */
test.describe('security headers & CSP', () => {
  test('HTML responses carry the defensive headers and a hash-based CSP, and nothing leaks the stack', async ({ request }) => {
    const res = await request.get('/', { headers: { 'accept-encoding': 'br, gzip' } });
    expect(res.status()).toBe(200);
    const h = res.headers();
    expect(h['x-content-type-options']).toBe('nosniff');
    expect(h['x-frame-options']).toBe('DENY');
    expect(h['referrer-policy']).toBe('strict-origin-when-cross-origin');
    expect(h['permissions-policy']).toContain('camera=()');
    expect(h['x-powered-by']).toBeUndefined();

    const csp = h['content-security-policy'] ?? '';
    expect(csp).toContain("default-src 'self'");
    expect(csp).toContain("object-src 'none'");
    expect(csp).toContain("frame-ancestors 'none'");
    const scriptSrc = csp.split('; ').find((d) => d.startsWith('script-src')) ?? '';
    expect(scriptSrc).toMatch(/'sha256-[A-Za-z0-9+/=]+'/);   // the inline hydration / event-replay scripts are allow-listed by hash
    expect(scriptSrc).not.toMatch(/unsafe-inline|unsafe-eval/);
  });

  test('the page runs under the CSP without a single violation, and the CSP really blocks injected script', async ({ page, consoleErrors }) => {
    const violations: string[] = [];
    page.on('console', (m) => { if (/Content Security Policy|Refused to/i.test(m.text())) violations.push(m.text()); });
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Shop by category' })).toBeVisible();
    // let hydration + event replay finish, then prove the app is interactive under the policy
    await page.getByRole('link', { name: /cart/i }).first().click();
    await expect(page).toHaveURL(/\/cart$/);
    expect(violations, 'CSP violations caused by the application itself').toEqual([]);
    consoleErrors.assertNone();

    // an attacker who found an injection point: inline <script>, inline event handler, javascript: URL
    const blocked = await page.evaluate(async () => {
      const w = window as unknown as Record<string, unknown>;
      const s = document.createElement('script');
      s.textContent = 'window.__pwnedScript = true';
      document.body.appendChild(s);

      const img = document.createElement('img');
      img.setAttribute('onerror', 'window.__pwnedHandler = true');
      img.src = 'data:image/png;base64,invalid';
      document.body.appendChild(img);

      const a = document.createElement('a');
      a.href = 'javascript:window.__pwnedUrl = true';
      document.body.appendChild(a);
      a.click();
      await new Promise((r) => setTimeout(r, 300));
      return { script: w['__pwnedScript'] === true, handler: w['__pwnedHandler'] === true, url: w['__pwnedUrl'] === true };
    });
    expect(blocked).toEqual({ script: false, handler: false, url: false });
    expect(violations.length, 'the browser reported the blocked attempts').toBeGreaterThan(0);
    violations.length = 0;
  });

  test('scripts and styles are compressed; hashed assets are immutable, unhashed ones revalidate', async ({ request }) => {
    const html = await (await request.get('/')).text();
    const hashed = /src="(\/?main-[A-Za-z0-9_-]+\.js)"/.exec(html)?.[1];
    expect(hashed, 'main bundle referenced by the page').toBeTruthy();
    const js = await request.get('/' + hashed!.replace(/^\//, ''), { headers: { 'accept-encoding': 'gzip' } });
    expect(js.headers()['content-encoding']).toBe('gzip');
    expect(js.headers()['cache-control']).toBe('public, max-age=31536000, immutable');

    const svg = await request.get('/images/placeholders/processor.svg');
    expect(svg.status()).toBe(200);
    expect(svg.headers()['cache-control']).toBe('public, max-age=3600');
  });

  test('the uploads proxy refuses path traversal and non-GET methods', async ({ request }) => {
    expect((await request.get('/uploads/../swagger/index.html')).status()).not.toBe(200);
    expect((await request.get('/uploads/%2e%2e/health')).status()).toBe(404);
    expect((await request.post('/uploads/x.png', { data: 'x' })).status()).toBe(404);
  });
});

test.describe('session cookie', () => {
  test('the refresh token is an HttpOnly cookie JavaScript cannot read, and the session survives a reload through it', async ({ page, context }) => {
    const email = uniqueEmail('cookie');
    await new RegisterPage(page).register({ fullName: 'Cookie Customer', email });
    await expect(page).toHaveURL('/');
    await expect(page.getByText(/Hi, Cookie/)).toBeVisible();

    const cookies = await context.cookies();
    const rt = cookies.find((c) => c.name === 'tb_rt');
    expect(rt, 'refresh cookie is set').toBeTruthy();
    expect(rt).toMatchObject({ httpOnly: true, sameSite: 'Strict', path: '/api/v1/auth' });
    expect(rt!.value.length).toBeGreaterThan(60);

    // page scripts see neither the cookie nor any token in web storage
    const visible = await page.evaluate(() => ({ cookie: document.cookie, storage: JSON.stringify({ ...localStorage, ...sessionStorage }) }));
    expect(visible.cookie).not.toContain('tb_rt');
    expect(visible.storage).not.toMatch(/eyJ|refresh/i);      // no JWT, no refresh token
    expect(visible.storage).toContain('tb.session.v1');        // only the non-secret "a session exists" hint

    // a hard reload loses the in-memory access token; the cookie silently restores the session
    await page.reload();
    await expect(page.getByText(/Hi, Cookie/)).toBeVisible();
    await page.goto('/account/profile');
    await expect(page.getByRole('heading', { name: 'My profile' })).toBeVisible();
  });

  test('logout revokes the session on the server and removes the cookie; a stolen cookie value no longer works', async ({ page, context, request }) => {
    await new RegisterPage(page).register({ fullName: 'Logout Customer', email: uniqueEmail('logout') });
    await expect(page.getByText(/Hi, Logout/)).toBeVisible();
    const stolen = (await context.cookies()).find((c) => c.name === 'tb_rt')!.value;

    await page.getByRole('button', { name: 'Logout' }).click();
    await expect(page.getByRole('link', { name: /login/i }).first()).toBeVisible();
    // the UI updates instantly; the server's Set-Cookie deletion arrives with the (best-effort) logout response a moment later
    await expect.poll(async () => (await context.cookies()).find((c) => c.name === 'tb_rt'), { timeout: 5_000 }).toBeUndefined();

    // replaying the old cookie value server-side (as an attacker holding a copy would) fails: it was revoked, not just deleted
    const replay = await request.post('/api/v1/auth/refresh', { headers: { 'x-refresh-mode': 'cookie', cookie: `tb_rt=${stolen}` }, data: {} });
    expect(replay.status()).toBe(401);

    await page.reload();
    await expect(page.getByText(/Hi, Logout/)).toHaveCount(0);
    await page.goto('/account/profile');
    await expect(page).toHaveURL(/\/login/);
  });

  test('"Sign out on all devices" kills every other session of the account', async ({ browser }) => {
    const email = uniqueEmail('multi');
    const a = await browser.newContext();
    const pageA = await a.newPage();
    await new RegisterPage(pageA).register({ fullName: 'Multi Device', email });
    await expect(pageA.getByText(/Hi, Multi/)).toBeVisible();

    const b = await browser.newContext();             // second device: log in separately
    const pageB = await b.newPage();
    await pageB.goto('/login');
    await pageB.getByLabel('Email').fill(email);
    await pageB.locator('#password').fill('Passw0rd!e2e');
    await pageB.getByRole('button', { name: /sign in|log in/i }).last().click();
    await expect(pageB.getByText(/Hi, Multi/)).toBeVisible();

    await pageA.goto('/account/profile');
    await pageA.getByRole('button', { name: 'Sign out on all devices' }).click();
    await expect(pageA).toHaveURL(/\/login/);

    await pageB.reload();                              // device B's cookie was revoked server-side
    await expect(pageB.getByText(/Hi, Multi/)).toHaveCount(0);
    await a.close();
    await b.close();
  });
});
