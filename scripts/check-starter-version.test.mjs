import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { checkStarterVersion } from './check-starter-version.mjs';

const script = resolve(dirname(fileURLToPath(import.meta.url)), 'check-starter-version.mjs');

function fixture(contents, action) {
  const dir = mkdtempSync(resolve(tmpdir(), 'starter-version-'));
  const file = resolve(dir, 'starter-version');
  try {
    if (contents !== undefined) writeFileSync(file, contents);
    return action((...args) => spawnSync(process.execPath, [script, '--file', file, ...args], { cwd: dir, encoding: 'utf8' }));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

test('CheckStarterVersion_MatchingTag_ReturnsVersion', () => {
  assert.equal(checkStarterVersion('v0.1.13\n', 'v0.1.13'), 'v0.1.13');
  fixture('v0.1.13\n', run => {
    const result = run('--tag', 'v0.1.13');
    assert.equal(result.status, 0);
    assert.equal(result.stdout, 'OK: starter-version v0.1.13 matches tag v0.1.13\n');
    assert.equal(result.stderr, '');
  });
});

test('CheckStarterVersion_MismatchedTag_FailsWithClearError', () => fixture('v0.1.13', run => {
  const result = run('--tag', 'v0.1.14');
  assert.equal(result.status, 1);
  assert.match(result.stderr, /starter-version "v0\.1\.13" does not match tag "v0\.1\.14"/);
  assert.equal(result.stdout, '');
}));

test('CheckStarterVersion_MissingFile_FailsWithClearError', () => fixture(undefined, run => {
  const result = run();
  assert.equal(result.status, 1);
  assert.match(result.stderr, /Starter version check failed:.*ENOENT.*starter-version/);
}));

test('CheckStarterVersion_EmptyFile_FailsWithClearError', () => {
  for (const contents of ['', ' \t\r\n']) {
    fixture(contents, run => {
      const result = run();
      assert.equal(result.status, 1);
      assert.match(result.stderr, /starter-version is empty/);
    });
  }
});

test('CheckStarterVersion_BadFormat_FailsWithClearError', () => {
  for (const contents of ['0.1.13', 'v0.1', 'v0.1.13-rc1', 'v0.1.13\nv0.1.14', 'v0.x.13']) {
    assert.throws(() => checkStarterVersion(contents), /must match/);
    fixture(contents, run => {
      const result = run();
      assert.equal(result.status, 1);
      assert.match(result.stderr, /starter-version must match v<major>\.<minor>\.<patch>/);
    });
  }
});

test('CheckStarterVersion_SurroundingWhitespaceAndTrailingNewline_AcceptsVersion', () => fixture(' \tv0.1.13 \r\n', run => {
  const result = run('--tag', 'v0.1.13');
  assert.equal(result.status, 0);
  assert.match(result.stdout, /^OK: starter-version v0\.1\.13 matches tag v0\.1\.13\n$/);
}));

test('CheckStarterVersion_NoTag_PassesFormatOnly', () => fixture('v12.34.56', run => {
  const result = run();
  assert.equal(result.status, 0);
  assert.equal(result.stdout, 'OK: starter-version v12.34.56\n');
  assert.equal(result.stderr, '');
}));

test('CheckStarterVersion_TagWithWhitespace_RequiresExactMatch', () => {
  assert.throws(() => checkStarterVersion('v0.1.13\n', 'v0.1.13 '), /does not match tag/);
});
