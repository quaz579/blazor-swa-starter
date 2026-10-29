import { test } from '@playwright/test';

test('cancel-path hang proof', async ({ page }) => {
  test.setTimeout(0);
  await page.goto('/');
  await new Promise(() => {});
});
