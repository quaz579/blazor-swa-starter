import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, mkdirSync, readFileSync, writeFileSync, cpSync, rmSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
import { writeVersionMarker } from './write-version-marker.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const read = path => readFileSync(resolve(root, path), 'utf8');

function fixture(action) {
  mkdirSync(resolve(root, '.azurite/verification'), { recursive: true });
  const dir = mkdtempSync(resolve(root, '.azurite/verification/version-'));
  try {
    mkdirSync(resolve(dir, 'src/App.Web/wwwroot'), { recursive: true });
    writeFileSync(resolve(dir, 'src/App.Web/wwwroot/index.html'), read('src/App.Web/wwwroot/index.html'));
    return action(dir);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

test('WriteVersionMarker_CiMetadata_StampsMatchingHtmlAndJson', () => fixture(dir => {
  writeFileSync(resolve(dir, 'starter-version'), 'v0.1.12\n');
  const marker = writeVersionMarker(dir, { VERSION_SHA: 'a'.repeat(40), GITHUB_RUN_ID: '123', GITHUB_RUN_ATTEMPT: '2' });
  assert.deepEqual(marker, {
    sha: 'a'.repeat(40), starter_tag: 'v0.1.12', built_at: marker.built_at, workflow_run_id: '123', run_attempt: '2',
  });
  assert.equal(new Date(marker.built_at).toISOString(), marker.built_at);
  assert.deepEqual(JSON.parse(readFileSync(resolve(dir, 'src/App.Web/obj/version-marker/version.json'), 'utf8')), marker);
  assert.match(readFileSync(resolve(dir, 'src/App.Web/obj/version-marker/index.html'), 'utf8'), new RegExp(`content="${marker.sha}"`));
  assert.equal(readFileSync(resolve(dir, 'src/App.Web/wwwroot/index.html'), 'utf8'), read('src/App.Web/wwwroot/index.html'));
}));

test('WriteVersionMarker_LegacyOutputs_RemovesOldMarkersAndPreservesOtherAssets', () => fixture(dir => {
  const web = resolve(dir, 'src/App.Web');
  mkdirSync(resolve(web, '.version'), { recursive: true });
  writeFileSync(resolve(web, '.version/index.html'), 'legacy host');
  writeFileSync(resolve(web, 'wwwroot/version.json'), '{"sha":"legacy"}');
  writeFileSync(resolve(web, 'wwwroot/other.json'), '{"keep":true}');
  const marker = writeVersionMarker(dir, { VERSION_SHA: 'b'.repeat(40) });
  assert.equal(existsSync(resolve(web, 'wwwroot/version.json')), false);
  assert.equal(existsSync(resolve(web, '.version')), false);
  assert.equal(readFileSync(resolve(web, 'wwwroot/other.json'), 'utf8'), '{"keep":true}');
  assert.deepEqual(JSON.parse(readFileSync(resolve(web, 'obj/version-marker/version.json'), 'utf8')), marker);
  assert.doesNotThrow(() => writeVersionMarker(dir, { VERSION_SHA: 'b'.repeat(40) }));
}));

test('WriteVersionMarker_LocalCheckout_UsesHeadAndLocalRun', () => fixture(dir => {
  const marker = writeVersionMarker(dir, {});
  assert.equal(marker.sha, execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim());
  assert.equal(marker.built_at, new Date(execFileSync('git', ['log', '-1', '--format=%cI'], { cwd: root, encoding: 'utf8' }).trim()).toISOString());
  const firstJson = readFileSync(resolve(dir, 'src/App.Web/obj/version-marker/version.json'), 'utf8');
  assert.deepEqual(writeVersionMarker(dir, {}), marker);
  assert.equal(readFileSync(resolve(dir, 'src/App.Web/obj/version-marker/version.json'), 'utf8'), firstJson);
  assert.equal(marker.workflow_run_id, 'local');
  assert.equal(marker.run_attempt, '0');
  assert.equal(marker.starter_tag, 'unknown');
  writeFileSync(resolve(dir, 'starter-version'), ' \n');
  assert.equal(writeVersionMarker(dir, {}).starter_tag, 'unknown');
  writeFileSync(resolve(dir, '.git'), 'gitdir: missing\n');
  const fallback = writeVersionMarker(dir, {});
  assert.equal(fallback.sha, 'local');
  assert.equal(fallback.built_at, '1970-01-01T00:00:00.000Z');
}));

test('EnablePocAuth_VersionRoute_PreservesAnonymousNoStoreAndFallbackExclusion', () => fixture(dir => {
  for (const path of ['scripts/enable-poc-auth.mjs', 'src/App.Web/wwwroot/appsettings.json', 'src/App.Web/wwwroot/staticwebapp.config.json']) {
    mkdirSync(dirname(resolve(dir, path)), { recursive: true });
    cpSync(resolve(root, path), resolve(dir, path));
  }
  mkdirSync(resolve(dir, 'tests/helpers'), { recursive: true });
  writeFileSync(resolve(dir, 'tests/helpers/factory-poc-auth.json'), JSON.stringify({ users: [{ username: 'test', password: 'Example123', roles: ['user'] }] }));
  mkdirSync(resolve(dir, 'src/App.Api/PocAuth'), { recursive: true });
  const check = () => {
    const config = JSON.parse(readFileSync(resolve(dir, 'src/App.Web/wwwroot/staticwebapp.config.json'), 'utf8'));
    const route = config.routes.find(route => route.route === '/version.json');
    assert.equal(route.headers['Cache-Control'], 'no-store');
    assert.equal(route.allowedRoles, undefined);
    assert.ok(config.navigationFallback.exclude.includes('/version.json'));
    return route;
  };
  const before = check();
  execFileSync(process.execPath, [resolve(dir, 'scripts/enable-poc-auth.mjs')]);
  assert.deepEqual(check(), before);
}));

for (const file of ['service-worker.js', 'service-worker.published.js']) {
  test(`OnFetch_${file}_VersionIsNetworkOnlyEvenOffline`, async () => {
    const listeners = {};
    const requests = [];
    const cacheAccess = () => { throw new Error('Version request accessed cache'); };
    let offline = false;
    const context = vm.createContext({
      URL, console,
      self: {
        origin: 'https://app.test', assetsManifest: { version: 'test', assets: [] },
        importScripts() {}, addEventListener(name, handler) { listeners[name] = handler; },
      },
      caches: { open: cacheAccess, keys: cacheAccess },
      fetch: async (...args) => {
        requests.push(args);
        if (offline) throw new Error('offline');
        return 'network';
      },
    });
    vm.runInContext(read(`src/App.Web/wwwroot/${file}`), context);
    if (file.includes('published')) {
      assert.equal(vm.runInContext("offlineAssetsExclude.some(pattern => pattern.test('version.json'))", context), true);
    }
    const request = { url: 'https://app.test/version.json?check=1', method: 'GET', mode: 'navigate' };
    let response;
    listeners.fetch({ request, respondWith(value) { response = value; } });
    assert.equal(await response, 'network');
    assert.equal(requests[0][1].cache, 'no-store');
    offline = true;
    listeners.fetch({ request, respondWith(value) { response = value; } });
    await assert.rejects(response, /offline/);
    assert.equal(requests.length, 2);
  });
}
