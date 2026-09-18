import * as crypto from 'node:crypto';
import { BlobServiceClient, ContainerClient } from '@azure/storage-blob';

const connectionString = process.env.AZURITE_CONNECTION_STRING ?? 'UseDevelopmentStorage=true';
const containerName = 'app-data';
const itemsPrefix = 'items';

/** A fixture `Item` to seed. `id`/`createdAt` default when omitted. */
export interface SeedItem {
  id?: string;
  name: string;
  description?: string | null;
  createdAt?: string;
}

// Fixtures are written directly to blob storage, bypassing the API's own
// safeguards, so this guard is the only thing standing between a mistyped
// env var and seeding/wiping a real storage account. Never widen it.
export function validateAzuriteConnectionString(value: string): void {
  if (value !== 'UseDevelopmentStorage=true') {
    throw new Error('Fixture seeding is restricted to the standard local Azurite account (Azurite emulator only).');
  }
}

function getContainerClient(): ContainerClient {
  validateAzuriteConnectionString(connectionString);
  const service = BlobServiceClient.fromConnectionString(connectionString);
  return service.getContainerClient(containerName);
}

/** Seeds `Item` JSON blobs at `items/{id}.json`, matching `BlobJsonStore<Item>` exactly (camelCase, `Web` serializer defaults). */
export async function seedItems(items: SeedItem[]): Promise<void> {
  const container = getContainerClient();
  await container.createIfNotExists();

  await Promise.all(
    items.map(async (item) => {
      const id = item.id ?? crypto.randomUUID().replace(/-/g, '');
      const body = {
        id,
        name: item.name,
        description: item.description ?? null,
        createdAt: item.createdAt ?? new Date().toISOString(),
      };
      const blob = container.getBlockBlobClient(`${itemsPrefix}/${id}.json`);
      const payload = JSON.stringify(body);
      await blob.upload(payload, Buffer.byteLength(payload), {
        blobHTTPHeaders: { blobContentType: 'application/json' },
      });
    }),
  );
}

/**
 * Deletes every seeded item blob but leaves the `app-data` container itself
 * in place — `BlobJsonStore.ListKeysAsync` 404s on a missing container, which
 * `GetItems` turns into a 500 rather than an empty list.
 */
export async function clearItems(): Promise<void> {
  const container = getContainerClient();
  await container.createIfNotExists();

  for await (const blob of container.listBlobsFlat({ prefix: `${itemsPrefix}/` })) {
    await container.getBlockBlobClient(blob.name).deleteIfExists();
  }
}
