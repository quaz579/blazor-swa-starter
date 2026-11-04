import { expect, test } from '../helpers/seeded-test';
import { loginAsAuthenticatedUser } from '../helpers/auth';
import { ItemsPage } from '../pages/items-page';

test.beforeEach(async ({ context }) => {
  await loginAsAuthenticatedUser(context);
  const items = await (await context.request.get('/api/items')).json();
  for (const item of items) expect((await context.request.delete(`/api/items/${item.id}`)).status()).toBe(204);
});

test('shows the empty state when the container has no items', async ({ page }) => {
  const items = new ItemsPage(page);
  await items.goto();

  await expect(items.empty).toBeVisible();
  await expect(items.list).not.toBeVisible();
  await expect(items.error).not.toBeVisible();
});
