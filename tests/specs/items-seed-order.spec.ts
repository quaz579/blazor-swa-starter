import { expect, test } from '../helpers/seeded-test';
import { ItemsPage } from '../pages/items-page';

test('lists seeded items newest-first', async ({ page }) => {
  const items = new ItemsPage(page);
  const oldest = 'Fixture Oldest';
  const middle = 'Fixture Middle';
  const newest = 'Fixture Newest';

  // Seeded out of chronological order so the assertion exercises the API's
  // own sort-by-createdAt, not the order fixtures happened to upload in.
  await items.goto();
  await expect(items.list).toBeVisible();
  await expect(items.rows).toHaveCount(3);

  const names = await items.allNames();
  expect(names).toEqual([newest, middle, oldest]);
});
