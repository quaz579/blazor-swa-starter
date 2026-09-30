#!/usr/bin/env node
import { createHmac, pbkdf2Sync } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const fixturePath = resolve(root, 'tests/helpers/factory-poc-auth.json');
const seedPath = resolve(root, 'src/App.Api/PocAuth/PocAuthSeed.g.cs');
const webSettingsPath = resolve(root, 'src/App.Web/wwwroot/appsettings.json');
const swaConfigPath = resolve(root, 'src/App.Web/wwwroot/staticwebapp.config.json');
const ITERATIONS = 220_000;

const { users } = JSON.parse(readFileSync(fixturePath, 'utf8'));
if (!Array.isArray(users) || users.length === 0) {
  console.error(`${fixturePath} must contain a non-empty "users" array`);
  process.exit(1);
}

const literal = (s) => JSON.stringify(s);
const rows = users.map(({ username, displayName, password, roles }) => {
  const salt = createHmac('sha256', 'poc-auth-seed').update(username.trim().toLowerCase()).digest().subarray(0, 16);
  const hash = pbkdf2Sync(Buffer.from(password, 'utf8'), salt, ITERATIONS, 32, 'sha512');
  const roleList = (roles ?? []).map(literal).join(', ');
  return `        new PocSeedUser(${literal(username)}, ${literal(displayName ?? username)}, [${roleList}], ${literal(salt.toString('base64'))}, ${literal(hash.toString('base64'))}, ${ITERATIONS}),`;
});

writeFileSync(
  seedPath,
  `namespace App.Api.PocAuth;

public static class PocAuthSeed
{
    public static readonly IReadOnlyList<PocSeedUser> Users =
    [
${rows.join('\n')}
    ];
}
`,
);

const settings = JSON.parse(readFileSync(webSettingsPath, 'utf8'));
settings.PocAuth = { ...(settings.PocAuth ?? {}), Enabled: true };
writeFileSync(webSettingsPath, `${JSON.stringify(settings, null, 2)}\n`);

const swaConfig = JSON.parse(readFileSync(swaConfigPath, 'utf8'));
swaConfig.routes = (swaConfig.routes ?? []).filter((route) => route.allowedRoles === undefined);
writeFileSync(swaConfigPath, `${JSON.stringify(swaConfig, null, 2)}\n`);

console.log(`POC auth enabled with ${users.length} seeded user(s): ${seedPath}`);
