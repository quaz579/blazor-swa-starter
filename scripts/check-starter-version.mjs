import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';

export function checkStarterVersion(contents, tag) {
  const version = contents.trim();
  if (!version) throw new Error('starter-version is empty');
  if (!/^v\d+\.\d+\.\d+$/.test(version)) {
    throw new Error(`starter-version must match v<major>.<minor>.<patch>; received ${JSON.stringify(version)}`);
  }
  if (tag !== undefined && version !== tag) {
    throw new Error(`starter-version ${JSON.stringify(version)} does not match tag ${JSON.stringify(tag)}`);
  }
  return version;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const { values } = parseArgs({ options: { tag: { type: 'string' }, file: { type: 'string' } } });
    const file = values.file ?? resolve(dirname(fileURLToPath(import.meta.url)), '../starter-version');
    const version = checkStarterVersion(readFileSync(file, 'utf8'), values.tag);
    console.log(`OK: starter-version ${version}${values.tag === undefined ? '' : ` matches tag ${values.tag}`}`);
  } catch (error) {
    console.error(`Starter version check failed: ${error.message}`);
    process.exitCode = 1;
  }
}
