import { expect, test, Page } from '@playwright/test';
import { HomePage } from '../pages/home-page';
import { ItemsPage } from '../pages/items-page';

const viewports = [
  { name: 'desktop 1280', size: { width: 1280, height: 800 } },
  { name: 'phone 390', size: { width: 390, height: 844 } },
];

const INJECTED_FAILURE = /Failed to load resource: net::ERR_FAILED/;

function collectConsoleProblems(page: Page): string[] {
  const problems: string[] = [];
  page.on('console', (msg) => {
    if (msg.type() === 'error' && !INJECTED_FAILURE.test(msg.text())) {
      problems.push(msg.text());
    }
  });
  page.on('pageerror', (err) => problems.push(err.message));
  return problems;
}

for (const { name, size } of viewports) {
  test.describe(name, () => {
    test.use({ viewport: size });

    test('an aborted GET /api/items shows the inline error state, not the framework overlay', async ({ page }) => {
      const problems = collectConsoleProblems(page);
      await page.route('**/api/items', (route) =>
        route.request().method() === 'GET' ? route.abort('failed') : route.continue(),
      );

      const items = new ItemsPage(page);
      await items.goto();

      await expect(items.error).toBeVisible();
      await expect(items.error).toContainText('Could not reach the API');
      await expect(page.locator('#blazor-error-ui')).toBeHidden();
      await expect(items.nameInput).toBeVisible();
      expect(problems).toEqual([]);
    });

    test('a malformed 200 body from GET /api/items shows the inline error state', async ({ page }) => {
      const problems = collectConsoleProblems(page);
      await page.route('**/api/items', (route) =>
        route.request().method() === 'GET'
          ? route.fulfill({ status: 200, contentType: 'application/json', body: '{not json' })
          : route.continue(),
      );

      const items = new ItemsPage(page);
      await items.goto();

      await expect(items.error).toContainText('unexpected response');
      await expect(page.locator('#blazor-error-ui')).toBeHidden();
      expect(problems).toEqual([]);
    });

    test('an aborted health check shows unavailable instead of crashing the home page', async ({ page }) => {
      const problems = collectConsoleProblems(page);
      await page.route('**/api/health', (route) => route.abort('failed'));

      const home = new HomePage(page);
      await home.goto();

      await expect(home.apiHealth).toHaveText('unavailable');
      await expect(page.locator('#blazor-error-ui')).toBeHidden();
      expect(problems).toEqual([]);
    });
  });
}
