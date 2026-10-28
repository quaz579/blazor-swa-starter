import { expect, test } from '@playwright/test';
import { pocAuthEnabled } from '../helpers/poc-auth';

test.describe('POC auth disabled', () => {
  test.skip(pocAuthEnabled(), 'POC auth is enabled');

  test('auth endpoints are inert and no sign-in UI is shown', { tag: '@smoke' }, async ({ page, request }) => {
    expect((await request.get('/api/auth/me')).status()).toBe(404);

    await page.goto('/');
    await expect(page.getByTestId('api-health')).toHaveText('ok');
    await expect(page.getByTestId('nav-login')).toHaveCount(0);
  });
});
