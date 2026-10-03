import { expect, test } from './support/fixtures';

test('PC builder flags a CPU / motherboard socket mismatch', async ({ page }) => {
  await page.goto('/builder');
  await expect(page.getByRole('heading', { name: 'PC Builder', level: 1 })).toBeVisible();

  const row = (label: string) => page.locator('li.row').filter({ has: page.getByRole('heading', { name: new RegExp(`^${label}`) }) });

  async function pick(slotLabel: string, query: string, partName: string, withCompatFilter: boolean): Promise<void> {
    await page.getByRole('button', { name: `Choose ${slotLabel}` }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    const toggle = dialog.getByRole('checkbox', { name: 'Compatible only' });
    if (withCompatFilter) {
      // The "Compatible only" filter appears once the server has evaluated the parts already chosen.
      await expect(toggle).toBeChecked();
      await toggle.uncheck();
      await expect(dialog.getByTestId('compat-note')).toContainText('Compatibility filter is off');
    }
    await dialog.getByRole('searchbox').fill(query);
    await dialog.getByRole('button', { name: new RegExp(`^Add ${partName}`) }).click();
    await expect(dialog).toBeHidden();
  }

  await test.step('AM5 CPU + LGA1700 motherboard', async () => {
    await pick('Processor', 'Ryzen 5 7600', 'AMD Ryzen 5 7600', false);
    await expect(row('Processor')).toContainText('AMD Ryzen 5 7600');
    await pick('Motherboard', 'Z790', 'Gigabyte Z790 AORUS ELITE AX', true);
    await expect(row('Motherboard')).toContainText('Z790');
  });

  await test.step('the summary reports an incompatible build with a socket error', async () => {
    const errors = page.locator('[data-severity="Error"]');
    await expect(errors).toBeVisible();
    await expect(errors).toContainText(/socket/i);
    await expect(page.getByTestId('incompat-warn')).toBeVisible();
    await expect(page.getByTestId('total')).toContainText('৳');
  });
});
