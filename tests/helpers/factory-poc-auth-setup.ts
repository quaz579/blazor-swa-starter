import { chromium } from '@playwright/test';
import { loginAs } from './poc-auth';

export default async function globalSetup(): Promise<void> {
  const storageState = process.env.FACTORY_POC_STORAGE_STATE;
  const baseURL = process.env.BASE_URL;
  if (!storageState || !baseURL) {
    throw new Error('FACTORY_POC_STORAGE_STATE and BASE_URL are required');
  }
  const browser = await chromium.launch();
  try {
    const context = await browser.newContext({ baseURL });
    const page = await context.newPage();
    await loginAs(page);
    await page.goto('/');
    const navUser = page.getByTestId('nav-user');
    const toggler = page.locator('.navbar-toggler');
    try {
      await page
        .locator('[data-testid="nav-user"]:visible, .navbar-toggler:visible')
        .first()
        .waitFor({ state: 'visible', timeout: 30_000 });
      if (await toggler.isVisible()) {
        await toggler.click();
      }
      await navUser.waitFor({ state: 'visible', timeout: 30_000 });
    } catch {
      throw new Error('the UI does not show signed-in after pocLogin (nav-user is missing)');
    }
    await context.storageState({ path: storageState });
  } finally {
    await browser.close();
  }
}
