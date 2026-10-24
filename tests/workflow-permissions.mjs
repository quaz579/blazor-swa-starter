import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { parse } from 'yaml';

export function checkPermissions(workflow, source) {
  for (const [name, job] of Object.entries(workflow.jobs)) {
    const permissions = Object.hasOwn(job, 'permissions') ? job.permissions : workflow.permissions;
    assert.ok(permissions && typeof permissions === 'object' && !Array.isArray(permissions),
      `${source}: ${name} must declare explicit permission scopes`);
    for (const [scope, access] of Object.entries(permissions)) {
      assert.ok(['read', 'write', 'none'].includes(access), `${source}: ${name}: invalid ${scope}`);
      assert.ok(access !== 'write' || scope === 'pull-requests',
        `${source}: ${name}: unnecessary write permission ${scope}`);
    }
    const steps = job.steps ?? [];
    if (steps.some(step => step.uses?.startsWith('actions/checkout@'))) {
      assert.equal(permissions.contents, 'read', `${source}: ${name}: checkout needs contents: read`);
    }
    if (steps.some(step => step.uses?.startsWith('Azure/static-web-apps-deploy@')
        && step.with?.action === 'upload' && step.with?.repo_token)) {
      assert.equal(permissions['pull-requests'], 'write',
        `${source}: ${name}: SWA preview comments need pull-requests: write`);
    }
  }
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const directory = new URL('../.github/workflows/', import.meta.url);
  for (const name of readdirSync(directory).filter(name => /\.ya?ml$/.test(name))) {
    checkPermissions(parse(readFileSync(new URL(name, directory), 'utf8')), name);
  }
  console.log('All app workflow jobs declare least-privilege permissions.');
}
