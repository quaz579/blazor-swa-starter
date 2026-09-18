import { expect, test } from '@playwright/test';
import { validateAzuriteConnectionString } from '../helpers/seed-azurite-fixtures';

test('fixture seeder rejects non-Azurite storage targets', () => {
  expect(() =>
    validateAzuriteConnectionString(
      'DefaultEndpointsProtocol=https;AccountName=production;AccountKey=fake;EndpointSuffix=core.windows.net',
    ),
  ).toThrow(/Azurite/);
});

test('fixture seeder accepts the standard local Azurite target', () => {
  expect(() => validateAzuriteConnectionString('UseDevelopmentStorage=true')).not.toThrow();
});
