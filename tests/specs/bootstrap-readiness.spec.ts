import { expect, test } from '@playwright/test';
import { HomePage } from '../pages/home-page';

test('home navigation waits for WASM bootstrap before returning', async ({ page }) => {
  let release!: () => void;
  let requested!: () => void;
  const gate = new Promise<void>((resolve) => { release = resolve; });
  const started = new Promise<void>((resolve) => { requested = resolve; });
  await page.route('**/_framework/*.wasm', async (route) => {
    requested();
    await gate;
    await route.continue();
  });
  const home = new HomePage(page);
  let completed = false;
  const navigation = home.goto().then(() => { completed = true; });
  try {
    await started;
    await page.waitForLoadState('load');
    await expect(page.locator('.loading-progress')).toBeVisible();
    expect(completed).toBe(false);
  } finally {
    release();
    await navigation;
  }
  await expect(home.root).toBeVisible();
});
