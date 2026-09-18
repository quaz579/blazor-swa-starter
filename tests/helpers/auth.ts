import { BrowserContext } from '@playwright/test';
import { ports } from './ports';

/**
 * With no `auth` block in staticwebapp.config.json, the SWA CLI emulator
 * decodes `StaticWebAppsAuthCookie` as plain base64 JSON straight into the
 * `ClientPrincipal` it checks `allowedRoles` against
 * (static-web-apps-cli/dist/core/utils/cookie.js `decodeCookie`,
 * dist/msha/routes-engine/rules/routes.js `isRouteRequiringUserRolesCheck`) —
 * no signing/encryption locally. Setting the cookie directly is equivalent to
 * driving `/.auth/login/<provider>`'s mock form (which does the same base64
 * encode client-side) without the extra page navigation.
 */
interface MockClientPrincipal {
  identityProvider: string;
  userId: string;
  userDetails: string;
  userRoles: string[];
  claims: unknown[];
}

/**
 * Establishes an authenticated emulator session for `context` by setting the
 * mock `StaticWebAppsAuthCookie`, equivalent to a manual `/.auth/login/aad`
 * submission. Local-emulator only — never use against a deployed preview.
 */
export async function loginAsAuthenticatedUser(
  context: BrowserContext,
  userDetails = 'e2e-test-user@example.com',
): Promise<void> {
  const principal: MockClientPrincipal = {
    identityProvider: 'aad',
    userId: 'e2e-test-user-id',
    userDetails,
    userRoles: ['anonymous', 'authenticated'],
    claims: [],
  };
  const cookieValue = Buffer.from(JSON.stringify(principal)).toString('base64');

  await context.addCookies([
    {
      name: 'StaticWebAppsAuthCookie',
      value: cookieValue,
      url: `http://localhost:${ports.SWA_PORT}`,
    },
  ]);
}
