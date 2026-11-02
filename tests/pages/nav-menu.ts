import { Locator, Page } from '@playwright/test';

/** Page Object Model for the `NavMenu` sidebar, present on every layout page. */
export class NavMenu {
  readonly page: Page;
  readonly home: Locator;
  readonly toggler: Locator;

  constructor(page: Page) {
    this.page = page;
    this.home = page.getByTestId('nav-home');
    this.toggler = page.getByTestId('nav-toggler');
  }
}
