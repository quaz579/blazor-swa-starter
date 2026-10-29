import { test } from '@playwright/test';

test('B unresolved promise timeout proof', async ({ page }) => {
  test.setTimeout(0);
  await page.goto('/');
  await new Promise(() => {});
});
