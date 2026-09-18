import { expect, test } from '@playwright/test';
import { ItemsPage } from '../pages/items-page';

test('shows the HTTP status code when the items API fails', async ({ page }) => {
  const items = new ItemsPage(page);

  // Route-intercept rather than breaking the real backend: Worker 2.x
  // preserves the status code it's given, so a forced 500 here exercises the
  // same client error-handling path a real storage failure would.
  await page.route('**/api/items', async (route) => {
    if (route.request().method() === 'GET') {
      await route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'Simulated storage failure' }),
      });
    } else {
      await route.continue();
    }
  });

  await items.goto();

  await expect(items.error).toBeVisible();
  await expect(items.error).toContainText('500');
});
