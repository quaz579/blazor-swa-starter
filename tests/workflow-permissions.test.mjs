import assert from 'node:assert/strict';
import test from 'node:test';
import { checkPermissions } from './workflow-permissions.mjs';

test('missing permissions fail under a read-only enterprise default', () => {
  assert.throws(() => checkPermissions({ jobs: { deploy: {} } }, 'missing.yml'),
    /deploy must declare explicit permission scopes/);
});

test('job permissions override deliberately inherited workflow scopes', () => {
  checkPermissions({ permissions: { contents: 'read' }, jobs: { build: {}, select: { permissions: {} } } },
    'inherited.yml');
  assert.throws(() => checkPermissions({ permissions: { contents: 'read' },
    jobs: { build: { permissions: null } } }, 'null.yml'), /explicit permission scopes/);
});

test('broad writes and missing SWA comment permission fail', () => {
  assert.throws(() => checkPermissions({ jobs: { build: { permissions: 'write-all' } } }, 'broad.yml'),
    /explicit permission scopes/);
  assert.throws(() => checkPermissions({ jobs: { build: { permissions: { contents: 'write' } } } },
    'write.yml'), /unnecessary write permission/);
  assert.throws(() => checkPermissions({ jobs: { deploy: { permissions: { contents: 'read' },
    steps: [{ uses: 'Azure/static-web-apps-deploy@v1', with: { action: 'upload', repo_token: 'token' } }]
  } } }, 'comment.yml'), /SWA preview comments/);
});
