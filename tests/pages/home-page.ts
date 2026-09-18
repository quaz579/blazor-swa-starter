import { Locator, Page } from '@playwright/test';
import { NavMenu } from './nav-menu';

/** Page Object Model for the home page (`/`). */
export class HomePage {
  readonly page: Page;
  readonly root: Locator;
  readonly apiHealth: Locator;
  readonly nav: NavMenu;

  constructor(page: Page) {
    this.page = page;
    this.root = page.getByTestId('home-page');
    this.apiHealth = page.getByTestId('api-health');
    this.nav = new NavMenu(page);
  }

  async goto(): Promise<void> {
    await this.page.goto('/');
  }
}
