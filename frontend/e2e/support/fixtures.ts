import { test as base, expect, type Page } from '@playwright/test';

/**
 * Shared test object. The auto fixture `consoleErrors` records browser console errors and uncaught page errors;
 * specs that care call `consoleErrors.assertNone()`.
 */
export interface ConsoleErrors {
  readonly all: string[];
  assertNone(): void;
}

// Noise that is not an application error (e.g. browsers asking for a favicon the app does not ship).
const IGNORED = [/favicon/i];

export const test = base.extend<{ consoleErrors: ConsoleErrors }>({
  consoleErrors: [
    async ({ page }, use) => {
      const all: string[] = [];
      page.on('console', (m) => {
        if (m.type() === 'error' && !IGNORED.some((r) => r.test(m.text() + m.location().url))) all.push(`console: ${m.text()}`);
      });
      page.on('pageerror', (e) => all.push(`pageerror: ${e.message}`));
      await use({ all, assertNone: () => expect(all, 'browser console / page errors').toEqual([]) });
    },
    { auto: true },
  ],
});

export { expect };

/** "৳1,25,000" -> 125000 (Bangladeshi digit grouping, first number in the string). */
export function parseBdt(text: string | null | undefined): number {
  const m = /৳\s*([\d,]+(?:\.\d+)?)/.exec(text ?? '');
  if (!m?.[1]) throw new Error(`No ৳ amount in "${text}"`);
  return Number(m[1].replace(/,/g, ''));
}

/** A password that satisfies the register form rules. */
export const PASSWORD = 'Passw0rd!e2e';

export function uniqueEmail(prefix = 'e2e'): string {
  return `${prefix}.${Date.now().toString(36)}${Math.random().toString(36).slice(2, 7)}@example.test`;
}

/** Waits until Angular has hydrated/booted: the header's cart link only works once the app is interactive. */
export async function gotoReady(page: Page, url: string): Promise<void> {
  await page.goto(url);
  await expect(page.getByRole('banner')).toBeVisible();
}

/** 125000 -> "৳1,25,000" (Bangladeshi grouping; independent of the runtime's ICU data). */
export function bdt(n: number): string {
  const s = Math.round(n).toString();
  if (s.length <= 3) return `৳${s}`;
  const head = s.slice(0, -3).replace(/\B(?=(\d{2})+(?!\d))/g, ',');
  return `৳${head},${s.slice(-3)}`;
}
