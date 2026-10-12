import { APIRequestContext, ConsoleMessage, Page, expect, test } from '@playwright/test';
import { loginAs, pocAuthEnabled, pocUser } from '../helpers/poc-auth';

const NEW_PASSWORD = 'Rotated-e2e-pass-7';
const EXPECTED_RESOURCE_FAILURE = /^Failed to load resource: the server responded with a status of (400|500)/;

function watchConsole(page: Page): string[] {
  const problems: string[] = [];
  page.on('console', (message: ConsoleMessage) => {
    if (message.type() === 'error' && !EXPECTED_RESOURCE_FAILURE.test(message.text())) {
      problems.push(message.text());
    }
  });
  page.on('pageerror', (error) => problems.push(String(error)));
  return problems;
}

async function changePassword(request: APIRequestContext, currentPassword: string, newPassword: string) {
  return request.post('/api/auth/change-password', { data: { currentPassword, newPassword } });
}

async function fillAndSubmit(page: Page, current: string, next: string, confirm = next) {
  await page.getByTestId('change-password-current').fill(current);
  await page.getByTestId('change-password-new').fill(next);
  await page.getByTestId('change-password-confirm').fill(confirm);
  await page.getByTestId('change-password-submit').click();
}

test.describe('POC auth change password', () => {
  test.skip(!pocAuthEnabled(), 'POC auth is not enabled (PocAuth:Enabled is false in appsettings.json)');

  test.afterEach(async ({ page }) => {
    const user = pocUser();
    const request = page.context().request;
    const login = await request.post('/api/auth/login', { data: { username: user.username, password: NEW_PASSWORD } });
    if (login.status() === 200) {
      await changePassword(request, NEW_PASSWORD, user.password);
    }
    await request.post('/api/auth/logout');
  });

  test('anonymous change-password is rejected with 401', { tag: '@smoke' }, async ({ request }) => {
    const response = await changePassword(request, 'anything-1', NEW_PASSWORD);
    expect(response.status()).toBe(401);
  });

  test('a signed-in user changes the password, signs out, and signs in with the new one', async ({ page }) => {
    const problems = watchConsole(page);
    const user = await loginAs(page);

    await page.goto('/');
    await page.getByTestId('nav-change-password').click();
    await expect(page.getByTestId('change-password-page')).toBeVisible();

    await fillAndSubmit(page, user.password, NEW_PASSWORD);
    await expect(page.getByTestId('change-password-success')).toBeVisible();
    await expect(page.getByTestId('change-password-error')).toHaveCount(0);

    await page.getByTestId('nav-logout').click();
    await expect(page.getByTestId('nav-login')).toBeVisible();

    await page.goto('/login');
    await page.getByTestId('login-username').fill(user.username);
    await page.getByTestId('login-password').fill(user.password);
    await page.getByTestId('login-submit').click();
    await expect(page.getByTestId('login-error')).toBeVisible();

    await page.getByTestId('login-password').fill(NEW_PASSWORD);
    await page.getByTestId('login-submit').click();
    await expect(page.getByTestId('nav-user')).toHaveText(user.username);

    expect(problems).toEqual([]);
  });

  test('the change-password page asks anonymous visitors to sign in', async ({ page }) => {
    const problems = watchConsole(page);
    await page.goto('/change-password');
    await expect(page.getByTestId('change-password-signin-required')).toBeVisible();
    await expect(page.getByTestId('change-password-submit')).toHaveCount(0);
    expect(problems).toEqual([]);
  });

  test('changing the password signs the same user out everywhere else', async ({ page, playwright, baseURL }) => {
    const user = await loginAs(page);
    const other = await playwright.request.newContext({ baseURL });
    try {
      const otherLogin = await other.post('/api/auth/login', { data: { username: user.username, password: user.password } });
      expect(otherLogin.status()).toBe(200);
      expect((await (await other.get('/api/auth/me')).json()).authenticated).toBe(true);

      expect((await changePassword(page.context().request, user.password, NEW_PASSWORD)).status()).toBe(204);

      expect((await (await other.get('/api/auth/me')).json()).authenticated).toBe(false);
      expect((await (await page.context().request.get('/api/auth/me')).json()).authenticated).toBe(true);
    } finally {
      await other.dispose();
    }
  });

  test('a wrong current password and a weak new password are refused with 400 and a visible message', async ({ page }) => {
    const problems = watchConsole(page);
    const user = await loginAs(page);
    await page.goto('/change-password');

    await fillAndSubmit(page, `${user.password}-wrong`, NEW_PASSWORD);
    await expect(page.getByTestId('change-password-error')).toContainText('current password');

    await fillAndSubmit(page, user.password, 'short1');
    await expect(page.getByTestId('change-password-error')).toContainText('at least');

    await fillAndSubmit(page, user.password, NEW_PASSWORD, 'Mismatch-pass-7');
    await expect(page.getByTestId('change-password-error')).toContainText('do not match');

    expect((await page.context().request.post('/api/auth/login', { data: { username: user.username, password: user.password } })).status()).toBe(200);
    await expect(page.getByTestId('change-password-submit')).toBeEnabled();
    expect(problems).toEqual([]);
  });

  test('a server fault, a malformed body, and an aborted request leave the page usable', async ({ page }) => {
    const problems = watchConsole(page);
    const user = await loginAs(page);
    await page.goto('/change-password');

    await page.route('**/api/auth/change-password', (route) =>
      route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ error: 'Simulated storage failure' }) }));
    await fillAndSubmit(page, user.password, NEW_PASSWORD);
    await expect(page.getByTestId('change-password-error')).toContainText('500');
    await expect(page.getByTestId('change-password-submit')).toBeEnabled();

    await page.unroute('**/api/auth/change-password');
    await page.route('**/api/auth/change-password', (route) =>
      route.fulfill({ status: 400, contentType: 'text/html', body: '<html>not json' }));
    await fillAndSubmit(page, user.password, NEW_PASSWORD);
    await expect(page.getByTestId('change-password-error')).toBeVisible();
    await expect(page.getByTestId('change-password-submit')).toBeEnabled();

    await page.unroute('**/api/auth/change-password');
    await page.route('**/api/auth/change-password', (route) => route.abort('connectionreset'));
    await fillAndSubmit(page, user.password, NEW_PASSWORD);
    await expect(page.getByTestId('change-password-error')).toContainText('Could not reach');
    await expect(page.getByTestId('change-password-submit')).toBeEnabled();

    await page.unroute('**/api/auth/change-password');
    await page.route('**/api/auth/change-password', (route) =>
      route.fulfill({ status: 401, contentType: 'application/json', body: JSON.stringify({ error: 'Sign in required.' }) }));
    await fillAndSubmit(page, user.password, NEW_PASSWORD);
    await expect(page.getByTestId('change-password-signin-required')).toBeVisible();

    expect(problems.filter((p) => !/Failed to load resource|net::ERR_CONNECTION_RESET|status of 401/.test(p))).toEqual([]);
  });
});
