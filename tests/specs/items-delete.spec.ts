import { expect, test } from '../helpers/seeded-test';
import { loginAsAuthenticatedUser } from '../helpers/auth';
import { ItemsPage } from '../pages/items-page';

test.beforeEach(async ({ context }) => {
  await loginAsAuthenticatedUser(context);
});

test('deleting a row removes it, and it stays gone after a reload', async ({ page }) => {
  const items = new ItemsPage(page);
  const name = 'Fixture Newest';

  await items.goto();
  const row = items.rowByName(name);
  await expect(row).toBeVisible();
  const id = await items.idOf(row);

  await items.deleteRowById(id);
  await expect(items.rowByName(name)).toHaveCount(0);

  // Reload proves the delete round-tripped to the blob store, not just client state.
  await page.reload();
  await expect(items.rowByName(name)).toHaveCount(0);
});
