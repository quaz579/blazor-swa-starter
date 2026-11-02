const connectionString = process.env.AZURITE_CONNECTION_STRING ?? 'UseDevelopmentStorage=true';

// Fixtures are written directly to blob storage, bypassing the API's own
// safeguards, so this guard is the only thing standing between a mistyped
// env var and seeding/wiping a real storage account. Never widen it.
export function validateAzuriteConnectionString(value: string): void {
  if (value !== 'UseDevelopmentStorage=true') {
    throw new Error('Fixture seeding is restricted to the standard local Azurite account (Azurite emulator only).');
  }
}

export { connectionString };
