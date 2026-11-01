import { Page, expect, test } from '@playwright/test';
import { loginAs, pocAuthEnabled, pocUser } from '../helpers/poc-auth';

async function pauseClockBeforeBoot(page: Page) {
  await page.clock.install({ time: new Date('2026-01-01T00:00:00Z') });
  await page.clock.pauseAt(new Date('2026-01-02T00:00:00Z'));
}

async function advanceUntilVisible(page: Page, testId: string) {
  await expect.poll(async () => {
    await page.clock.runFor(1);
    return page.getByTestId(testId).isVisible();
  }, { intervals: [0] }).toBe(true);
}

test.describe('POC auth', () => {
  test.skip(!pocAuthEnabled(), 'POC auth is not enabled (PocAuth:Enabled is false in appsettings.json)');

  test.afterEach(async ({ page }) => {
    await page.context().request.post('/api/auth/logout');
  });

  test('health stays anonymous', { tag: '@prod' }, async ({ request }) => {
    const health = await request.get('/api/health');
    expect(health.status()).toBe(200);
  });

  test('anonymous requests are rejected and a wrong password is refused', { tag: '@prod' }, async ({ request }) => {
    const anonymousMe = await request.get('/api/auth/me');
    expect(anonymousMe.status()).toBe(200);
    expect(await anonymousMe.json()).toEqual({ authenticated: false });
    expect((await request.post('/api/auth/change-password', { data: {} })).status()).toBe(401);

    const user = pocUser();
    const wrong = await request.post('/api/auth/login', { data: { username: user.username, password: `${user.password}-wrong` } });
    expect(wrong.status()).toBe(401);
  });

  test('fixture user logs in, passes the gate, and logs out', { tag: '@prod' }, async ({ page }) => {
    const user = await loginAs(page);
    const request = page.context().request;

    const cookies = await page.context().cookies();
    const session = cookies.find((c) => c.name === 'poc_session');
    expect(session?.httpOnly).toBe(true);
    expect(session?.sameSite).toBe('Lax');

    const me = await request.get('/api/auth/me');
    expect(me.status()).toBe(200);
    expect(await me.json()).toMatchObject({ authenticated: true, username: user.username });

    expect((await request.post('/api/auth/change-password', { data: {} })).status()).toBe(400);

    expect((await request.post('/api/auth/logout')).status()).toBe(204);
    expect(await (await request.get('/api/auth/me')).json()).toEqual({ authenticated: false });
    expect((await request.post('/api/auth/change-password', { data: {} })).status()).toBe(401);
  });

  test('signed-out page loads produce zero console errors', { tag: '@prod' }, async ({ page }) => {
    const problems: string[] = [];
    page.on('console', (message) => message.type() === 'error' && problems.push(message.text()));
    page.on('pageerror', (error) => problems.push(String(error)));

    for (const path of ['/', '/login', '/change-password']) {
      await page.goto(path);
      await expect(page.getByTestId('nav-login')).toBeVisible();
    }

    expect(problems).toEqual([]);
  });

  test('a session created through the loginAs API helper shows the UI signed in after a reload', { tag: '@prod' }, async ({ page }) => {
    const user = await loginAs(page);

    await page.goto('/');
    await expect(page.getByTestId('nav-user')).toHaveText(user.username);
    await page.reload();
    await expect(page.getByTestId('nav-user')).toHaveText(user.username);

    await page.goto('/change-password');
    await expect(page.getByTestId('nav-user')).toHaveText(user.username);
    await expect(page.getByTestId('nav-login')).toHaveCount(0);
  });

  test('signing in through the login page shows the user in the nav', { tag: '@prod' }, async ({ page }) => {
    const user = pocUser();
    await page.goto('/login');
    await page.getByTestId('login-username').fill(user.username);
    await page.getByTestId('login-password').fill(user.password);
    await page.getByTestId('login-submit').click();

    await expect(page.getByTestId('nav-user')).toHaveText(user.username);

    await page.getByTestId('nav-logout').click();
    await expect(page.getByTestId('nav-login')).toBeVisible();
  });

  for (const invalid of [
    { name: 'missing fields', body: {} },
    { name: 'a null role', body: { username: 'fixture-user', roles: [null] } },
    { name: 'a mixed null role', body: { username: 'fixture-user', roles: ['user', null] } },
  ]) {
    test(`an invalid successful login with ${invalid.name} stays signed out and shows an error`, async ({ page }) => {
      await page.route('**/api/auth/login', (route) => route.fulfill({ json: invalid.body }));
      await page.goto('/login');
      await page.getByTestId('login-username').fill('fixture-user');
      await page.getByTestId('login-password').fill('fixture-pass1');
      await page.getByTestId('login-submit').click();

      await expect(page.getByTestId('login-error')).toHaveText('Sign-in failed: the API returned an unexpected response.');
      await expect(page.getByTestId('nav-login')).toBeVisible();
      await expect(page.getByTestId('nav-user')).toHaveCount(0);
      await expect(page.locator('#blazor-error-ui')).toBeHidden();
    });
  }

  test('a delayed anonymous session probe cannot replace a successful sign-in', async ({ page }) => {
    await pauseClockBeforeBoot(page);
    const user = pocUser();
    let releaseProbe!: () => void;
    const gate = new Promise<void>((resolve) => { releaseProbe = resolve; });
    await page.route('**/api/auth/me', async (route) => {
      await gate;
      await route.fulfill({ json: { authenticated: false } });
    });
    try {
      await page.goto('/login');
      await advanceUntilVisible(page, 'login-username');
      await page.getByTestId('login-username').fill(user.username);
      await page.getByTestId('login-password').fill(user.password);
      await page.getByTestId('login-submit').click();
      await advanceUntilVisible(page, 'nav-user');
      await expect(page.getByTestId('nav-user')).toHaveText(user.username);

      const probe = page.waitForResponse('**/api/auth/me');
      releaseProbe();
      await (await probe).finished();
      await expect.poll(async () => {
        await page.clock.runFor(1);
        return page.getByTestId('nav-links').getAttribute('aria-busy');
      }, { intervals: [0] }).toBe('false');

      await expect(page.getByTestId('nav-user')).toHaveText(user.username);
      await expect(page.getByTestId('nav-login')).toHaveCount(0);
      await expect(page.locator('#blazor-error-ui')).toBeHidden();
    } finally {
      releaseProbe();
    }
  });

  for (const path of ['/', '/login', '/change-password']) {
    test(`a failed session probe on ${path} offers Retry and recovers`, async ({ page }, testInfo) => {
      const user = pocUser();
      let attempts = 0;
      await page.route('**/api/auth/me', (route) => {
        attempts++;
        return attempts <= 2
          ? route.fulfill({ status: 503 })
          : route.fulfill({ json: { authenticated: true, username: user.username, roles: user.roles } });
      });
      await page.goto(path);
      const failure = page.getByTestId('auth-probe-failed');
      await expect(failure.first()).toContainText("Couldn't check sign-in");
      await expect(page.getByTestId('nav-login')).toHaveCount(0);
      await expect(page.getByTestId('login-submit')).toHaveCount(0);
      await expect(page.getByTestId('change-password-signin-required')).toHaveCount(0);
      expect(attempts).toBe(2);
      await testInfo.attach("Failed sign-in check", { body: await page.screenshot(), contentType: "image/png" });

      await page.getByTestId('auth-probe-retry').first().click();

      await expect(page.getByTestId('nav-user')).toHaveText(user.username);
      await expect(failure).toHaveCount(0);
      expect(attempts).toBe(3);
      await testInfo.attach("Recovered sign-in", { body: await page.screenshot(), contentType: "image/png" });
      await expect(page.locator('#blazor-error-ui')).toBeHidden();
    });
  }

  for (const failure of ['server', 'network']) {
    test(`a ${failure} logout failure preserves the signed-in UI and offers retry`, async ({ page }) => {
      const user = await loginAs(page);
      await page.goto('/');
      await expect(page.getByTestId('nav-user')).toHaveText(user.username);
      await page.route('**/api/auth/logout', (route) => failure === 'server'
        ? route.fulfill({ status: 500 })
        : route.abort('connectionreset'));

      await page.getByTestId('nav-logout').click();

      await expect(page.getByTestId('nav-logout-error')).toContainText('Try again.');
      await expect(page.getByTestId('nav-user')).toHaveText(user.username);
      await expect(page.getByTestId('nav-logout')).toBeEnabled();
      expect((await (await page.context().request.get('/api/auth/me')).json()).authenticated).toBe(true);
      await expect(page.locator('#blazor-error-ui')).toBeHidden();

      await page.unroute('**/api/auth/logout');
      await page.getByTestId('nav-logout').click();

      await expect(page.getByTestId('nav-login')).toBeVisible();
      expect((await (await page.context().request.get('/api/auth/me')).json()).authenticated).toBe(false);
    });
  }

  test('the password page waits for the session probe shared with navigation', async ({ page }) => {
    await pauseClockBeforeBoot(page);
    const user = pocUser();
    let releaseProbe!: () => void;
    const probeGate = new Promise<void>((resolve) => { releaseProbe = resolve; });
    await page.route('**/api/auth/me', async (route) => {
      await probeGate;
      await route.fulfill({ json: { authenticated: true, username: user.username, roles: user.roles } });
    });
    try {
      await page.goto('/change-password');
      await advanceUntilVisible(page, 'change-password-loading');
      await expect(page.getByTestId('change-password-loading')).toBeVisible();
      await expect(page.getByTestId('change-password-signin-required')).toHaveCount(0);
      releaseProbe();
      await advanceUntilVisible(page, 'change-password-submit');
      await expect(page.getByTestId('change-password-submit')).toBeVisible();
      await expect(page.getByTestId('nav-user')).toHaveText(user.username);
    } finally {
      releaseProbe();
    }
  });

  test('a wrong password on the login page shows an error and keeps the page usable', { tag: '@prod' }, async ({ page }) => {
    const user = pocUser();
    await page.goto('/login');
    await page.getByTestId('login-username').fill(user.username);
    await page.getByTestId('login-password').fill('not-the-password');
    await page.getByTestId('login-submit').click();

    await expect(page.getByTestId('login-error')).toBeVisible();
    await expect(page.getByTestId('nav-login')).toBeVisible();
  });
});
