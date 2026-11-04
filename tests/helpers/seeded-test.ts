import { test as base } from '@playwright/test';
import { resetToSeed } from './seed-azurite-fixtures';

export { expect } from '@playwright/test';
export type { APIRequestContext, ConsoleMessage, Page } from '@playwright/test';

export const test = base.extend<{ seedReset: void }>({
  seedReset: [async ({}, use) => {
    await resetToSeed();
    await use();
  }, { auto: true }],
});
