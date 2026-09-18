import { Locator, Page } from '@playwright/test';

/** Page Object Model for the items page (`/items`). */
export class ItemsPage {
  readonly page: Page;
  readonly root: Locator;
  readonly loading: Locator;
  readonly error: Locator;
  readonly empty: Locator;
  readonly list: Locator;
  readonly rows: Locator;
  readonly nameInput: Locator;
  readonly descriptionInput: Locator;
  readonly addButton: Locator;
  readonly validationError: Locator;

  constructor(page: Page) {
    this.page = page;
    this.root = page.getByTestId('items-page');
    this.loading = page.getByTestId('items-loading');
    this.error = page.getByTestId('items-error');
    this.empty = page.getByTestId('items-empty');
    this.list = page.getByTestId('items-list');
    this.rows = page.getByTestId('item-row');
    this.nameInput = page.getByTestId('item-name-input');
    this.descriptionInput = page.getByTestId('item-description-input');
    this.addButton = page.getByTestId('item-add-button');
    this.validationError = page.getByTestId('item-validation-error');
  }

  async goto(): Promise<void> {
    await this.page.goto('/items');
  }

  rowById(id: string): Locator {
    return this.page.locator(`[data-testid="item-row"][data-item-id="${id}"]`);
  }

  rowByName(name: string): Locator {
    return this.rows.filter({ has: this.page.getByTestId('item-name').getByText(name, { exact: true }) });
  }

  async idOf(row: Locator): Promise<string> {
    return (await row.getAttribute('data-item-id')) ?? '';
  }

  async allNames(): Promise<string[]> {
    return this.rows.getByTestId('item-name').allTextContents();
  }

  async addItem(name: string, description?: string): Promise<void> {
    await this.nameInput.fill(name);
    if (description !== undefined) {
      await this.descriptionInput.fill(description);
    }
    await this.addButton.click();
  }

  async deleteRowById(id: string): Promise<void> {
    await this.rowById(id).getByTestId('item-delete-button').click();
  }
}
