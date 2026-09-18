import { expect, test } from '@playwright/test';
import { HomePage } from '../pages/home-page';
import { ItemsPage } from '../pages/items-page';

test.describe('desktop nav', () => {
  test('the Items link is visible and reachable, and Home nav works from there', async ({ page }) => {
    const home = new HomePage(page);
    await home.goto();

    await expect(home.nav.items).toBeVisible();
    await home.nav.items.click();

    await expect(page).toHaveURL(/\/items$/);
    const items = new ItemsPage(page);
    await expect(items.root).toBeVisible();

    await expect(items.nav.home).toBeVisible();
    await items.nav.home.click();

    await expect(page).toHaveURL(/\/$/);
    await expect(home.root).toBeVisible();
  });
});

test.describe('mobile nav', () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test('the Items link is reachable only after opening the hamburger toggler', async ({ page }) => {
    const home = new HomePage(page);
    await home.goto();

    // Below the 641px breakpoint the sidebar stays Bootstrap-collapsed until toggled.
    await expect(home.nav.items).not.toBeVisible();

    await home.nav.toggler.click();
    await expect(home.nav.items).toBeVisible();
    await home.nav.items.click();

    await expect(page).toHaveURL(/\/items$/);
    const items = new ItemsPage(page);
    await expect(items.root).toBeVisible();

    await expect(items.nav.home).not.toBeVisible();
    await items.nav.toggler.click();
    await expect(items.nav.home).toBeVisible();
    await items.nav.home.click();

    await expect(page).toHaveURL(/\/$/);
    await expect(home.root).toBeVisible();
  });
});
