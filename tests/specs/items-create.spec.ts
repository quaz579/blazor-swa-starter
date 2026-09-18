import { expect, test } from '@playwright/test';
import { clearItems } from '../helpers/seed-azurite-fixtures';
import { ItemsPage } from '../pages/items-page';

test.beforeEach(async () => {
  await clearItems();
});

test('creating an item through the form shows it, and it survives a reload', async ({ page }) => {
  const items = new ItemsPage(page);
  await items.goto();

  const name = `Fixture Create ${Date.now()}`;
  await items.addItem(name, 'created by the create-item spec');

  const row = items.rowByName(name);
  await expect(row).toBeVisible();
  await expect(row.getByTestId('item-description')).toHaveText('created by the create-item spec');

  // Reload proves the round trip through the blob store, not just client-side state.
  await page.reload();
  await expect(items.rowByName(name)).toBeVisible();
});
