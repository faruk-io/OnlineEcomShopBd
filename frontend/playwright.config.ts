import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end suite: browse, filter, cart, register, checkout (cash on delivery), guards, PC builder. Run: npm run build && npm run e2e
 *
 * Starts two servers:
 *   1. the E2E API host  (backend/tests/TechBazar.E2EHost: the real API pipeline on a throw-away SQLite file; TEST ONLY)
 *   2. the built SSR storefront (`npm run build` must have run first; it serves /api/v1 by proxying to the host)
 *
 * Environment:
 *   CI=1                 retries once, never reuses running servers, adds --no-sandbox
 *   PW_CHROMIUM_PATH     use this Chromium/Chrome binary instead of Playwright's managed one. Only needed in
 *                        sandboxes whose preinstalled browser does not match the Playwright version.
 *                        CI should instead run `npx playwright install --with-deps chromium` (npm run e2e:install).
 *   E2E_API_PORT         API host port (default 5180)      E2E_WEB_PORT  SSR server port (default 4100)
 *   DOTNET_ROLL_FORWARD  set to `Major` only on machines that have a newer .NET SDK but not .NET 9 (projects target net9.0)
 */
const apiPort = process.env['E2E_API_PORT'] ?? '5180';
const webPort = process.env['E2E_WEB_PORT'] ?? '4100';
const ci = !!process.env['CI'];
const chromiumPath = process.env['PW_CHROMIUM_PATH'];

export default defineConfig({
  testDir: './e2e',
  outputDir: './test-results',
  fullyParallel: false, // one shared SQLite database; the journey tests create their own unique users
  workers: 1,
  forbidOnly: ci,
  retries: ci ? 1 : 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [['list'], ['html', { outputFolder: 'playwright-report', open: 'never' }]],
  use: {
    baseURL: `http://localhost:${webPort}`,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'on-first-retry',
    locale: 'en-US',
    launchOptions: {
      ...(chromiumPath ? { executablePath: chromiumPath } : {}),
      ...(chromiumPath || ci ? { args: ['--no-sandbox'] } : {}),
    },
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: 'dotnet run --project ../backend/tests/TechBazar.E2EHost',
      url: `http://localhost:${apiPort}/api/v1/checkout/options`,
      env: { E2E_API_PORT: apiPort, ASPNETCORE_ENVIRONMENT: 'Development' },
      reuseExistingServer: !ci,
      timeout: 180_000,
      stdout: 'ignore',
      stderr: 'pipe',
    },
    {
      command: 'node dist/techbazar-web/server/server.mjs',
      url: `http://localhost:${webPort}/`,
      env: { PORT: webPort, API_URL: `http://localhost:${apiPort}`, NG_ALLOWED_HOSTS: 'localhost' },
      reuseExistingServer: !ci,
      timeout: 60_000,
      stdout: 'ignore',
      stderr: 'pipe',
    },
  ],
});
