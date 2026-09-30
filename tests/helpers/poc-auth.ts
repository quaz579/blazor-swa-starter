import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { Page, expect } from '@playwright/test';

export interface PocUser {
  username: string;
  password: string;
  displayName?: string;
  roles?: string[];
}

export const pocAuthFixturePath = resolve(__dirname, 'factory-poc-auth.json');

export function loadPocUsers(): PocUser[] {
  if (!existsSync(pocAuthFixturePath)) {
    return [];
  }
  return JSON.parse(readFileSync(pocAuthFixturePath, 'utf8')).users ?? [];
}

const webSettingsPath = resolve(__dirname, '../../src/App.Web/wwwroot/appsettings.json');

export function pocAuthEnabled(): boolean {
  if (loadPocUsers().length === 0 || !existsSync(webSettingsPath)) {
    return false;
  }
  return JSON.parse(readFileSync(webSettingsPath, 'utf8')).PocAuth?.Enabled === true;
}

export function pocUser(roleOrUsername?: string): PocUser {
  const users = loadPocUsers();
  const match = roleOrUsername
    ? users.find((u) => u.username === roleOrUsername || u.roles?.includes(roleOrUsername))
    : users[0];
  if (!match) {
    throw new Error(`No POC auth fixture user for "${roleOrUsername ?? 'default'}" in ${pocAuthFixturePath}`);
  }
  return match;
}

export async function loginAs(page: Page, user: PocUser | string = pocUser()): Promise<PocUser> {
  const resolved = typeof user === 'string' ? pocUser(user) : user;
  const response = await page.context().request.post('/api/auth/login', {
    data: { username: resolved.username, password: resolved.password },
  });
  expect(response.status(), `POST /api/auth/login for ${resolved.username}`).toBe(200);
  return resolved;
}
