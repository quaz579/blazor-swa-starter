import { request } from '@playwright/test';
import { isDeepStrictEqual } from 'node:util';
import manifest from '../seed-manifest.json';
import { ports } from './ports';

/** The fixture response must exactly match the checked-in seed manifest. */
export function assertSeedManifest(value: unknown): void {
  const expectedStores = [
    ...Object.keys(manifest.tables).map((name) => `table:${name}`),
    ...Object.keys(manifest.blobs).map((name) => `blob:${name}`),
  ].sort();
  // Fixtures reset through the API rather than writing storage directly.
  // The API owns the loopback and runtime guards; this check prevents
  // tests from continuing with an unexpected fixture dataset.
  if (!isDeepStrictEqual(value, { ...manifest, stores: expectedStores })) {
    throw new Error(`Seed manifest mismatch: expected ${JSON.stringify({ ...manifest, stores: expectedStores })}, received ${JSON.stringify(value)}`);
  }
}

/** Restores the fixed Item JSON blobs through the application seed hook. */
export async function resetToSeed(): Promise<void> {
  const client = await request.newContext({ baseURL: process.env.BASE_URL ?? `http://localhost:${ports.SWA_PORT}` });
  try {
    /**
     * Clears registered business stores and restores their declared seed.
     * The app leaves auth tables and host containers alone; tests never
     * enumerate or delete storage themselves.
     */
    const response = await client.post('/api/__test/reset-to-seed');
    const body = await response.text();
    if (response.status() !== 200) throw new Error(`Seed reset failed: HTTP ${response.status()}: ${body}`);
    try { assertSeedManifest(JSON.parse(body)); }
    catch (error) { throw new Error(`Seed reset response: ${body}: ${error}`); }
  } finally { await client.dispose(); }
}
