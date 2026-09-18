import { expect, test } from '@playwright/test';
import { clearItems, seedItems } from '../helpers/seed-azurite-fixtures';
import { ItemsPage } from '../pages/items-page';

test.beforeEach(async () => {
  await clearItems();
});

test('lists seeded items newest-first', async ({ page }) => {
  const items = new ItemsPage(page);
  const now = Date.now();
  const oldest = `Fixture Oldest ${now}`;
  const middle = `Fixture Middle ${now}`;
  const newest = `Fixture Newest ${now}`;

  // Seeded out of chronological order so the assertion exercises the API's
  // own sort-by-createdAt, not the order fixtures happened to upload in.
  await seedItems([
    { name: middle, createdAt: new Date(now - 60_000).toISOString() },
    { name: newest, createdAt: new Date(now - 30_000).toISOString() },
    { name: oldest, createdAt: new Date(now - 90_000).toISOString() },
  ]);

  await items.goto();
  await expect(items.list).toBeVisible();
  await expect(items.rows).toHaveCount(3);

  const names = await items.allNames();
  expect(names).toEqual([newest, middle, oldest]);
});
