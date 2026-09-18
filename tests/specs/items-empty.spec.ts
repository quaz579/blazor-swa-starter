import { expect, test } from '@playwright/test';
import { clearItems } from '../helpers/seed-azurite-fixtures';
import { ItemsPage } from '../pages/items-page';

test.beforeEach(async () => {
  await clearItems();
});

test('shows the empty state when the container has no items', async ({ page }) => {
  const items = new ItemsPage(page);
  await items.goto();

  await expect(items.empty).toBeVisible();
  await expect(items.list).not.toBeVisible();
  await expect(items.error).not.toBeVisible();
});
