import { expect, test, uniqueEmail } from './support/fixtures';
import { RegisterPage } from './support/pages';

test.describe('route guards', () => {
  test('anonymous visitors are sent to /login with a return URL', async ({ page }) => {
    await page.goto('/checkout');
    await expect(page).toHaveURL(/\/login\?returnUrl=%2Fcheckout$/);
    await expect(page.getByRole('heading', { name: /sign in/i })).toBeVisible();

    await page.goto('/account/orders');
    await expect(page).toHaveURL(/\/login\?returnUrl=%2Faccount%2Forders$/);

    await page.goto('/account');
    await expect(page).toHaveURL(/\/login/);
  });

  test('anonymous visitors cannot open the admin area', async ({ page }) => {
    await page.goto('/admin');
    await expect(page).toHaveURL(/\/login\?returnUrl=%2Fadmin$/);
  });

  test('a customer is bounced from /admin to the storefront', async ({ page }) => {
    await new RegisterPage(page).register({ fullName: 'Guard Customer', email: uniqueEmail('guard') });
    await expect(page).toHaveURL('/');
    await page.goto('/admin');
    await expect(page).toHaveURL('/');
    await expect(page.getByRole('heading', { name: 'Shop by category' })).toBeVisible();
    await expect(page.getByRole('heading', { name: /dashboard/i })).toHaveCount(0);
  });
});
