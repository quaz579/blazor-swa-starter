import { defineConfig, devices } from '@playwright/test';
import { ports } from './helpers/ports';

export default defineConfig({
  testDir: './specs',

  fullyParallel: false,
  workers: 1,

  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,

  reporter: [
    ['html', { outputFolder: 'playwright-report', open: 'never' }],
    ['list'],
  ],

  // Generous because Blazor WASM's cold boot (first load, no cache) is slow.
  timeout: 90_000,
  expect: {
    timeout: 30_000,
  },

  use: {
    // CI's smoke-preview job passes the deployed preview URL via BASE_URL;
    // otherwise fall back to the local SWA CLI proxy, which is what routes
    // /api/* to the Functions host (the Blazor dev-server port does not).
    baseURL: process.env.BASE_URL ?? `http://localhost:${ports.SWA_PORT}`,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    actionTimeout: 15_000,
    navigationTimeout: 60_000,
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  // No webServer / globalSetup: scripts/start-e2e.sh owns bringing up the
  // stack, both locally and in CI.
});
