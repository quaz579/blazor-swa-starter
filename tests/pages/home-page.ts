import { Locator, Page } from '@playwright/test';

/** Page Object Model for the home page (`/`). */
export class HomePage {
  readonly page: Page;
  readonly root: Locator;
  readonly apiHealth: Locator;

  constructor(page: Page) {
    this.page = page;
    this.root = page.getByTestId('home-page');
    this.apiHealth = page.getByTestId('api-health');
  }

  async goto(): Promise<void> {
    await this.page.goto('/');
  }
}
