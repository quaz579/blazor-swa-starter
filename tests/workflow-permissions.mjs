import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { parse } from 'yaml';

const directory = new URL('../.github/workflows/', import.meta.url);
for (const name of readdirSync(directory).filter(name => /\.ya?ml$/.test(name))) {
  const workflow = parse(readFileSync(new URL(name, directory), 'utf8'));
  for (const [jobName, job] of Object.entries(workflow.jobs)) {
    assert.ok(job.permissions && typeof job.permissions === 'object'
      && !Array.isArray(job.permissions), `${name}: ${jobName} must declare permissions`);
  }
}
console.log('All app workflow jobs declare permissions.');
