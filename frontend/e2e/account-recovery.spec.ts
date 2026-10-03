import { expect, test, PASSWORD, uniqueEmail } from './support/fixtures';
import { emailsTo, tokenOf, waitForLink } from './support/mailbox';
import { RegisterPage } from './support/pages';

const API = `http://localhost:${process.env['E2E_API_PORT'] ?? '5180'}`;
const NEW_PASSWORD = 'Brand9New!pass';

test.describe('email verification', () => {
  test('register -> mail -> link verifies once; banner disappears; the same link is then invalid', async ({ page, request }) => {
    const email = uniqueEmail('verify');
    await new RegisterPage(page).register({ fullName: 'Verify Me', email });
    await expect(page).toHaveURL('/');

    // Unverified users are nudged in the account area.
    await page.goto('/account/profile');
    const banner = page.getByRole('region', { name: 'Email verification' });
    await expect(banner).toBeVisible();
    await expect(banner).toContainText(email);

    const link = await waitForLink(request, email, 'verify-email');
    expect(link).toContain('#token=');

    await page.goto(link);
    await expect(page.getByRole('heading', { name: 'Email verified' })).toBeVisible();
    await expect(page).toHaveURL(/\/verify-email$/);   // the fragment was removed from the address bar
    expect(await page.evaluate(() => location.hash)).toBe('');
    // Signed in in this browser: the profile is refreshed, so no "sign in" prompt is offered.
    await expect(page.getByRole('link', { name: 'Sign in' })).toHaveCount(0);

    await page.goto('/account/profile');
    await expect(page.getByRole('navigation', { name: 'Account', exact: true })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Email verification' })).toHaveCount(0);

    // Single use.
    await page.goto(link);
    await expect(page.getByRole('heading', { name: 'This link is invalid or has expired' })).toBeVisible();
    await expect(page).toHaveURL(/\/verify-email$/);
  });

  test('a garbage link shows the invalid state; "resend" gives a signed-in unverified user a fresh working link', async ({ page, request }) => {
    const email = uniqueEmail('resend');
    await new RegisterPage(page).register({ fullName: 'Resend Me', email });
    await expect(page).toHaveURL('/');
    const first = await waitForLink(request, email, 'verify-email');

    await page.goto('/verify-email#token=not-a-real-token');
    await expect(page.getByRole('heading', { name: 'This link is invalid or has expired' })).toBeVisible();
    await page.getByRole('button', { name: 'Send me a new link' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'new link' })).toBeVisible();
    await expect(page.getByRole('button', { name: /Send me a new link \(\d+s\)/ })).toBeDisabled();

    const second = await waitForLink(request, email, 'verify-email', [first]);
    await page.goto(second);
    await expect(page.getByRole('heading', { name: 'Email verified' })).toBeVisible();
  });
});

test.describe('password reset', () => {
  test('forgot -> neutral answer -> link -> weak rejected, same link works -> login notice -> old password dead -> other sessions signed out -> link single-use', async ({
    page,
    browser,
    request,
  }) => {
    const email = uniqueEmail('reset');
    const stranger = uniqueEmail('nobody');

    // A second browser, signed in before the reset.
    const other = await browser.newContext();
    const otherPage = await other.newPage();
    await new RegisterPage(otherPage).register({ fullName: 'Reset Me', email });
    await expect(otherPage).toHaveURL('/');
    await otherPage.goto('/account/profile');
    await expect(otherPage.getByRole('navigation', { name: 'Account', exact: true })).toBeVisible();

    // Forgot password: identical neutral answer for an unknown and a known address.
    const neutral: string[] = [];
    for (const address of [stranger, email]) {
      await page.goto('/forgot-password');
      await page.getByLabel('Email').fill(address);
      await page.getByRole('button', { name: 'Send reset link' }).click();
      const status = page.getByRole('status').filter({ hasText: 'If an account exists' });
      await expect(status).toBeVisible();
      neutral.push((await status.innerText()).trim());
      await expect(page.getByRole('heading', { name: 'Check your email' })).toBeFocused();
    }
    expect(neutral[0]).toBe(neutral[1]);
    expect(neutral[0]).not.toContain(email);

    const link = await waitForLink(request, email, 'reset-password');
    expect(await emailsTo(request, stranger), 'no mail for an unknown address').toEqual([]);

    // The link: token is read, then removed from the address bar.
    await page.goto(link);
    await expect(page.getByRole('heading', { name: 'Choose a new password' })).toBeVisible();
    await expect(page).toHaveURL(/\/reset-password$/);
    expect(await page.evaluate(() => location.hash)).toBe('');

    // Weak password: rejected (client rules mirror the server) and the form stays usable.
    await page.getByLabel('New password', { exact: true }).fill('weakpass');
    await page.getByLabel('Confirm new password').fill('weakpass');
    await page.getByRole('button', { name: 'Set new password' }).click();
    await expect(page.getByText('Add an uppercase letter.')).toBeVisible();
    await expect(page).toHaveURL(/\/reset-password$/);

    // The server rejects a weak password too, and does NOT spend the token (called directly, bypassing the form's checks).
    const weak = await request.post(`${API}/api/v1/auth/reset-password`, { data: { token: tokenOf(link), newPassword: 'weakpass' } });
    expect(weak.status()).toBe(400);
    expect(((await weak.json()) as { errors?: Record<string, string[]> }).errors?.['newPassword']?.length).toBeGreaterThan(0);

    // Strong password with the SAME link.
    await page.getByLabel('New password', { exact: true }).fill(NEW_PASSWORD);
    await page.getByLabel('Confirm new password').fill(NEW_PASSWORD);
    await page.getByRole('button', { name: 'Set new password' }).click();
    await expect(page).toHaveURL(/\/login\?reset=1$/);
    await expect(page.getByRole('status').filter({ hasText: 'password has been changed' })).toBeVisible();

    // Never auto-login; the old password is dead, the new one works.
    await page.getByLabel('Email').fill(email);
    await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page.getByRole('alert')).toContainText('Invalid email or password');
    await page.getByLabel('Password', { exact: true }).fill(NEW_PASSWORD);
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page).toHaveURL('/');

    // The session that existed before the reset is gone.
    await otherPage.reload();
    await otherPage.goto('/account/profile');
    await expect(otherPage).toHaveURL(/\/login/);
    await other.close();

    // Single use. The page cannot know a token is spent until the server is asked, so the form shows first and the answer swaps it.
    await page.goto(link);
    await page.getByLabel('New password', { exact: true }).fill('Another9Pass!');
    await page.getByLabel('Confirm new password').fill('Another9Pass!');
    await page.getByRole('button', { name: 'Set new password' }).click();
    await expect(page.getByRole('heading', { name: 'This link is invalid or has expired' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Request a new link' })).toBeVisible();
    await expect(page).toHaveURL(/\/reset-password$/);
  });

  test('a reset link without a token shows the invalid state and a way to request a new one', async ({ page }) => {
    await page.goto('/reset-password');
    await expect(page.getByRole('heading', { name: 'This link is invalid or has expired' })).toBeVisible();
    await page.getByRole('link', { name: 'Request a new link' }).click();
    await expect(page).toHaveURL('/forgot-password');
    await page.getByRole('link', { name: 'Back to sign in' }).click();
    await expect(page.getByRole('link', { name: 'Forgot your password?' })).toBeVisible();
  });
});
