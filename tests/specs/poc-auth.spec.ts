import { expect, test } from '@playwright/test';
import { loginAs, pocAuthEnabled, pocUser } from '../helpers/poc-auth';

test.describe('POC auth', () => {
  test.skip(!pocAuthEnabled(), 'POC auth is not enabled (PocAuth:Enabled is false in appsettings.json)');

  test.afterEach(async ({ page }) => {
    await page.context().request.post('/api/auth/logout');
  });

  test('health stays anonymous', { tag: '@smoke' }, async ({ request }) => {
    const health = await request.get('/api/health');
    expect(health.status()).toBe(200);
  });

  test('anonymous requests are rejected and a wrong password is refused', { tag: '@smoke' }, async ({ request }) => {
    expect((await request.get('/api/auth/me')).status()).toBe(401);
    expect((await request.delete('/api/items/poc-auth-probe')).status()).toBe(401);

    const user = pocUser();
    const wrong = await request.post('/api/auth/login', { data: { username: user.username, password: `${user.password}-wrong` } });
    expect(wrong.status()).toBe(401);
  });

  test('fixture user logs in, passes the gate, and logs out', { tag: '@smoke' }, async ({ page }) => {
    const user = await loginAs(page);
    const request = page.context().request;

    const cookies = await page.context().cookies();
    const session = cookies.find((c) => c.name === 'poc_session');
    expect(session?.httpOnly).toBe(true);
    expect(session?.sameSite).toBe('Lax');

    const me = await request.get('/api/auth/me');
    expect(me.status()).toBe(200);
    expect((await me.json()).username).toBe(user.username);

    expect((await request.delete('/api/items/poc-auth-probe')).status()).toBe(404);

    expect((await request.post('/api/auth/logout')).status()).toBe(204);
    expect((await request.get('/api/auth/me')).status()).toBe(401);
    expect((await request.delete('/api/items/poc-auth-probe')).status()).toBe(401);
  });

  test('signing in through the login page shows the user in the nav', { tag: '@smoke' }, async ({ page }) => {
    const user = pocUser();
    await page.goto('/login');
    await page.getByTestId('login-username').fill(user.username);
    await page.getByTestId('login-password').fill(user.password);
    await page.getByTestId('login-submit').click();

    await expect(page.getByTestId('nav-user')).toHaveText(user.username);

    await page.getByTestId('nav-logout').click();
    await expect(page.getByTestId('nav-login')).toBeVisible();
  });

  test('a wrong password on the login page shows an error and keeps the page usable', { tag: '@smoke' }, async ({ page }) => {
    const user = pocUser();
    await page.goto('/login');
    await page.getByTestId('login-username').fill(user.username);
    await page.getByTestId('login-password').fill('not-the-password');
    await page.getByTestId('login-submit').click();

    await expect(page.getByTestId('login-error')).toBeVisible();
    await expect(page.getByTestId('nav-login')).toBeVisible();
  });
});
