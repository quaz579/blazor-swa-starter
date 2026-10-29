import { test } from '@playwright/test';

test('A page wait timeout hang', async ({ page }) => {
  test.setTimeout(0);
  await page.goto('/');
  await page.waitForTimeout(3_600_000);
});

test('B unresolved promise hang', async ({ page }) => {
  test.setTimeout(0);
  await page.goto('/');
  await new Promise(() => {});
});
