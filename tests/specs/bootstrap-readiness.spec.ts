import { expect, test } from '@playwright/test';
import { ItemsPage } from '../pages/items-page';

test('items navigation waits for WASM bootstrap before returning', async ({ page }) => {
  let release!: () => void;
  let requested!: () => void;
  const gate = new Promise<void>((resolve) => { release = resolve; });
  const started = new Promise<void>((resolve) => { requested = resolve; });
  await page.route('**/_framework/*.wasm', async (route) => {
    requested();
    await gate;
    await route.continue();
  });
  const items = new ItemsPage(page);
  let completed = false;
  const navigation = items.goto().then(() => { completed = true; });
  try {
    await started;
    await page.waitForLoadState('load');
    await expect(page.locator('.loading-progress')).toBeVisible();
    expect(completed).toBe(false);
  } finally {
    release();
    await navigation;
  }
  await expect(items.root).toBeVisible();
});
