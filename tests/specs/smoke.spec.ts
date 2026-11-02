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
