import { expect, test } from '@playwright/test';
import { clearItems } from '../helpers/seed-azurite-fixtures';
import { ItemsPage } from '../pages/items-page';

test.beforeEach(async () => {
  await clearItems();
});

function countPostsTo(page: import('@playwright/test').Page, path: string): () => number {
  let count = 0;
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes(path)) {
      count++;
    }
  });
  return () => count;
}

test('rejects a blank name client-side and never calls the API', async ({ page }) => {
  const items = new ItemsPage(page);
  await items.goto();
  const postCount = countPostsTo(page, '/api/items');

  await items.addItem('   ');

  await expect(items.validationError).toBeVisible();
  expect(postCount()).toBe(0);
});

test('rejects a name over 100 characters client-side and never calls the API', async ({ page }) => {
  const items = new ItemsPage(page);
  await items.goto();
  const postCount = countPostsTo(page, '/api/items');

  await items.addItem('x'.repeat(101));

  await expect(items.validationError).toBeVisible();
  expect(postCount()).toBe(0);
});
