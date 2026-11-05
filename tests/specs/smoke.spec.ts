import { expect, test } from '@playwright/test';
import { HomePage } from '../pages/home-page';
import { ItemsPage } from '../pages/items-page';

// Read-only, no seeding: this file is safe to run against a deployed preview
// environment, which inherits production app settings and uses separate
// preview storage (CI's smoke-preview job). No POST/DELETE anywhere here.

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

test('served version is anonymous JSON and matches the served HTML', { tag: '@smoke' }, async ({ request }) => {
  const response = await request.get('/version.json');
  expect(response.status()).toBe(200);
  expect(response.headers()['content-type']).toContain('application/json');
  expect(response.headers()['cache-control']).toContain('no-store');
  const marker = await response.json();
  expect(Object.keys(marker).sort()).toEqual(['built_at', 'run_attempt', 'sha', 'starter_tag', 'workflow_run_id']);
  for (const value of Object.values(marker)) expect(typeof value).toBe('string');
  expect(marker.sha).toMatch(/^(?:[a-f0-9]{40}|local)$/);
  expect(marker.starter_tag.length).toBeGreaterThan(0);
  expect(marker.workflow_run_id.length).toBeGreaterThan(0);
  expect(marker.run_attempt).toMatch(/^\d+$/);
  expect(new Date(marker.built_at).toISOString()).toBe(marker.built_at);
  const home = await request.get('/');
  expect(home.status()).toBe(200);
  const html = await home.text();
  expect(html.match(/<meta name="app-version" content="([^"]+)"/u)?.[1]).toBe(marker.sha);
});

test('health reports the expected storage identity', { tag: '@smoke' }, async ({ request }) => {
  const response = await request.get('/api/health');
  expect(response.status(), await response.text()).toBe(200);
  const health = await response.json();
  const expectedMode = process.env.EXPECTED_STORAGE_MODE ?? 'production';
  expect(health.storageMode).toBe(expectedMode);
  expect(health.storageAccount).toMatch(/^[a-z0-9]+$/);
  expect(health.storageIdentity).toMatch(/^(verified|unverified)$/);
  if (process.env.REQUIRE_VERIFIED_STORAGE_IDENTITY === 'true') {
    expect(health.storageIdentity).toBe('verified');
    expect(health.productionStorageAccount).toMatch(/^[a-z0-9]+$/);
    if (expectedMode === 'preview') {
      expect(health.storageAccount).not.toBe(health.productionStorageAccount);
    } else {
      expect(health.storageAccount).toBe(health.productionStorageAccount);
    }
  }
  expect(Object.keys(health).sort()).toEqual([
    'productionStorageAccount', 'status', 'storageAccount', 'storageIdentity', 'storageMode', 'utc',
  ]);
});
