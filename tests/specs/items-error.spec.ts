import { expect, test } from '@playwright/test';
import { ItemsPage } from '../pages/items-page';

test('shows the HTTP status code when the items API fails', async ({ page }) => {
  const items = new ItemsPage(page);

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

test('a successful response containing a null item shows an inline error', async ({ page }) => {
  await page.route('**/api/items', (route) => route.fulfill({ json: [null] }));
  const items = new ItemsPage(page);

  await items.goto();

  await expect(items.error).toContainText('null item');
  await expect(page.getByTestId('item-row')).toHaveCount(0);
  await expect(page.locator('#blazor-error-ui')).toBeHidden();
});
