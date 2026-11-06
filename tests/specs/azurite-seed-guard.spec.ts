import { expect, test } from '../helpers/seeded-test';
import { assertSeedManifest } from '../helpers/seed-azurite-fixtures';
import manifest from '../seed-manifest.json';

test('reset returns the exact manifest through BASE_URL', async ({ request }) => {
  const response = await request.post('/api/__test/reset-to-seed');
  expect(response.status()).toBe(200);
  assertSeedManifest(await response.json());
});

test('manifest checks reject extra keys, missing stores, and wrong counts', () => {
  const stores = [
    ...Object.keys(manifest.tables).map((name) => `table:${name}`),
    ...Object.keys(manifest.blobs).map((name) => `blob:${name}`),
  ].sort();
  const wrongBlobs: Record<string, number> = { ...manifest.blobs };
  const key = Object.keys(wrongBlobs)[0] ?? 'unregistered';
  wrongBlobs[key] = (wrongBlobs[key] ?? 0) + 1;
  expect(() => assertSeedManifest({ ...manifest, stores, extra: true })).toThrow(/mismatch/);
  expect(() => assertSeedManifest(manifest)).toThrow(/mismatch/);
  expect(() => assertSeedManifest({ ...manifest, stores, blobs: wrongBlobs })).toThrow(/mismatch/);
});

test('reset rejects non-loopback forwarded hops and GET', async ({ request }) => {
  for (const value of ['203.0.113.1', '127.0.0.1, 203.0.113.1']) {
    const response = await request.post('/api/__test/reset-to-seed', { headers: { 'X-Forwarded-For': value } });
    expect(response.status()).toBe(404);
    expect(await response.text()).toBe('');
  }
  expect([404, 405]).toContain((await request.get('/api/__test/reset-to-seed')).status());
});
