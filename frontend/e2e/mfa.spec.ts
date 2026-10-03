import { expect, test, PASSWORD, uniqueEmail } from './support/fixtures';
import { RegisterPage } from './support/pages';
import { totp } from './support/totp';

const ADMIN_EMAIL = 'admin@techbazar.bd';
const ADMIN_PASSWORD = 'AdminPassw0rd!';   // set in backend/tests/TechBazar.E2EHost

async function signIn(page: import('@playwright/test').Page, email: string, password: string, returnUrl?: string): Promise<void> {
  await page.goto(returnUrl ? `/login?returnUrl=${encodeURIComponent(returnUrl)}` : '/login');
  await page.locator('#email').fill(email);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

/** Runs the setup flow on /account/security; returns the base32 secret and the recovery codes. TOTP step -1 is consumed. */
async function enrol(page: import('@playwright/test').Page): Promise<{ secret: string; codes: string[] }> {
  await page.locator('#mfa-setup').click();
  const secret = ((await page.locator('#mfa-secret').textContent()) ?? '').replace(/\s/g, '');
  expect(secret).toMatch(/^[A-Z2-7]{32}$/);
  await expect(page.locator('qr-code svg path, app-qr-code svg path').first()).toBeAttached();   // QR drawn client-side
  await page.locator('#enable-code').fill(totp(secret, -1));
  await page.locator('#enable-submit').click();
  const items = page.locator('[data-testid="recovery-code"]');
  await expect(items).toHaveCount(10);
  const codes = (await items.allTextContents()).map((t) => t.trim());
  expect(codes[0]).toMatch(/^[A-Z2-9]{5}-[A-Z2-9]{5}$/);
  await expect(page.locator('#dismiss-codes')).toBeDisabled();   // must acknowledge first
  await page.locator('#ack-codes').check();
  await page.locator('#dismiss-codes').click();
  await expect(page.locator('[data-testid="mfa-enabled"]')).toBeVisible();
  await expect(page.locator('[data-testid="recovery-code"]')).toHaveCount(0);   // shown once
  return { secret, codes };
}

test.describe('two-step verification', () => {
  test('a customer opts in, signs out, then needs the code (and a replayed code is refused)', async ({ page }) => {
    const email = uniqueEmail('mfa');
    await new RegisterPage(page).register({ fullName: 'Mfa Customer', email });
    await expect(page).toHaveURL('/');
    await page.goto('/account/security');
    const { secret } = await enrol(page);

    // sign out everywhere ended every old session; the next sign-in needs the second factor
    await page.context().clearCookies();
    await page.evaluate(() => localStorage.clear());
    await signIn(page, email, PASSWORD);
    await expect(page.locator('#mfa-code')).toBeVisible();
    await expect(page).toHaveURL(/\/login/);

    // wrong code: stays on the step with an error
    await page.locator('#mfa-code').fill('000000');
    await page.locator('#mfa-submit').click();
    await expect(page.locator('#mfa-error')).toBeVisible();

    await page.locator('#mfa-code').fill(totp(secret, 0));
    await page.locator('#mfa-submit').click();
    await expect(page).toHaveURL('/');

    // the same code cannot be used again, even though it is still inside its validity window
    await page.context().clearCookies();
    await page.evaluate(() => localStorage.clear());
    await signIn(page, email, PASSWORD);
    await page.locator('#mfa-code').fill(totp(secret, 0));
    await page.locator('#mfa-submit').click();
    await expect(page.locator('#mfa-error')).toBeVisible();
    await expect(page).toHaveURL(/\/login/);
  });

  test('a recovery code signs in once', async ({ page }) => {
    const email = uniqueEmail('mfarec');
    await new RegisterPage(page).register({ fullName: 'Mfa Recovery', email });
    await expect(page).toHaveURL('/');
    await page.goto('/account/security');
    const { codes } = await enrol(page);
    await page.context().clearCookies();
    await page.evaluate(() => localStorage.clear());

    await signIn(page, email, PASSWORD);
    await page.locator('#mfa-toggle').click();
    await page.locator('#mfa-recovery').fill(codes[0]!.toLowerCase());
    await page.locator('#mfa-submit').click();
    await expect(page).toHaveURL('/');

    await page.context().clearCookies();
    await page.evaluate(() => localStorage.clear());
    await signIn(page, email, PASSWORD);
    await page.locator('#mfa-toggle').click();
    await page.locator('#mfa-recovery').fill(codes[0]!);
    await page.locator('#mfa-submit').click();
    await expect(page.locator('#mfa-error')).toBeVisible();
  });

  test('the admin panel needs MFA: password only -> sent to enrol -> admin panel opens, and survives a re-login with the code', async ({ page }) => {
    await signIn(page, ADMIN_EMAIL, ADMIN_PASSWORD, '/admin');
    // not enrolled yet: the guard sends the admin to the security page with an explanation
    await expect(page).toHaveURL(/\/account\/security\?reason=admin-mfa/);
    await expect(page.locator('[data-testid="admin-mfa-notice"]')).toBeVisible();

    const { secret } = await enrol(page);
    await page.goto('/admin');
    await expect(page).toHaveURL(/\/admin/);
    await expect(page.getByRole('heading', { name: /dashboard/i }).first()).toBeVisible();

    // admins cannot switch it off
    await page.goto('/account/security');
    await expect(page.locator('#disable-open')).toHaveCount(0);

    await page.context().clearCookies();
    await page.evaluate(() => localStorage.clear());
    await signIn(page, ADMIN_EMAIL, ADMIN_PASSWORD, '/admin');
    await page.locator('#mfa-code').fill(totp(secret, 0));
    await page.locator('#mfa-submit').click();
    await expect(page).toHaveURL(/\/admin/);
    await expect(page.getByRole('heading', { name: /dashboard/i }).first()).toBeVisible();
  });
});
