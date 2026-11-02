import { expect, Page } from '@playwright/test';

export async function gotoApp(page: Page, path: string): Promise<void> {
  await page.goto(path);
  await expect(page.locator('#app main')).toBeVisible({ timeout: 60_000 });
}
