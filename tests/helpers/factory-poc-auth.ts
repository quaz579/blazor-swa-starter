import type { BrowserContext } from '@playwright/test';
import { loginAsAuthenticatedUser } from './auth';

export { loginAs as pocLogin } from './poc-auth';

export async function authenticateForProtectedSurfaces(context: BrowserContext): Promise<void> {
  await loginAsAuthenticatedUser(context);
}
