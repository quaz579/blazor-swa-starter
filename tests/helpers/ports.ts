import { readFileSync } from 'node:fs';
import * as path from 'node:path';

/** Numeric ports for the local/E2E dev stack, sourced from `scripts/ports.env` — the single source of truth (see CONTRACT.md). */
export interface Ports {
  SWA_PORT: number;
  WEB_PORT: number;
  API_PORT: number;
  AZURITE_BLOB_PORT: number;
  AZURITE_QUEUE_PORT: number;
  AZURITE_TABLE_PORT: number;
}

const REQUIRED_KEYS: readonly (keyof Ports)[] = [
  'SWA_PORT',
  'WEB_PORT',
  'API_PORT',
  'AZURITE_BLOB_PORT',
  'AZURITE_QUEUE_PORT',
  'AZURITE_TABLE_PORT',
];

function parsePortsEnv(filePath: string): Ports {
  let raw: string;
  try {
    raw = readFileSync(filePath, 'utf-8');
  } catch (err) {
    throw new Error(`Could not read ports file at ${filePath}: ${(err as Error).message}`);
  }

  const values: Partial<Record<keyof Ports, number>> = {};
  for (const line of raw.split('\n')) {
    const trimmed = line.trim();
    if (trimmed === '' || trimmed.startsWith('#')) {
      continue;
    }
    const eq = trimmed.indexOf('=');
    if (eq === -1) {
      continue;
    }
    const key = trimmed.slice(0, eq).trim() as keyof Ports;
    const value = trimmed.slice(eq + 1).trim();
    const parsed = Number(value);
    if (Number.isFinite(parsed)) {
      values[key] = parsed;
    }
  }

  const missing = REQUIRED_KEYS.filter((key) => values[key] === undefined);
  if (missing.length > 0) {
    throw new Error(
      `ports.env at ${filePath} is missing or has an invalid value for: ${missing.join(', ')}`,
    );
  }

  return values as Ports;
}

const PORTS_ENV_PATH = path.resolve(__dirname, '..', '..', 'scripts', 'ports.env');

export const ports: Ports = parsePortsEnv(PORTS_ENV_PATH);
