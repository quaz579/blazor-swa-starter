import { expect, test } from '@playwright/test';
import { HomePage } from '../pages/home-page';
import { ItemsPage } from '../pages/items-page';

// Read-only, no seeding: this file is safe to run against a deployed preview
// environment, which inherits production app settings and therefore
// production storage (CI's smoke-preview job). No POST/DELETE anywhere here.

test('home page loads and reports API health', { tag: '@smoke' }, async ({ page }) => {
  const home = new HomePage(page);
  await home.goto();

  await expect(home.root).toBeVisible();
  await expect(home.apiHealth).toHaveText('ok');
});

test('items page renders either a list or the empty state', { tag: '@smoke' }, async ({ page }) => {
  const items = new ItemsPage(page);
  await items.goto();

  await expect(items.root).toBeVisible();
  await expect(items.error).not.toBeVisible();
  await expect(items.list.or(items.empty)).toBeVisible();
});
