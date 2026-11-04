import { execFileSync } from 'node:child_process';
import { existsSync, lstatSync, mkdirSync, readFileSync, readdirSync, realpathSync, rmdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

function isTracked(root, path) {
  try {
    execFileSync('git', ['ls-files', '--error-unmatch', '--', path], { cwd: root, stdio: 'ignore' });
    return true;
  } catch {
    return false;
  }
}

function isLegacyJson(path) {
  try {
    if (!lstatSync(path).isFile()) return false;
    const value = JSON.parse(readFileSync(path, 'utf8'));
    return value !== null && !Array.isArray(value) && typeof value === 'object'
      && Object.keys(value).sort().join(',') === 'built_at,run_attempt,sha,starter_tag,workflow_run_id'
      && typeof value.sha === 'string' && /^(?:[a-f0-9]{40}|local)$/.test(value.sha);
  } catch {
    return false;
  }
}

export function writeVersionMarker(root, env = process.env) {
  let sha = env.VERSION_SHA;
  if (!sha) {
    try {
      sha = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
    } catch {
      sha = 'local';
    }
  }
  if (!/^(?:[a-f0-9]{40}|local)$/.test(sha)) throw new Error('Expected a full commit SHA or local');
  let builtAt = '1970-01-01T00:00:00.000Z';
  if (env.VERSION_SHA) {
    builtAt = new Date().toISOString();
  } else {
    try {
      builtAt = new Date(execFileSync('git', ['log', '-1', '--format=%cI'], { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim()).toISOString();
    } catch {}
  }
  let starterTag = 'unknown';
  try {
    starterTag = readFileSync(resolve(root, 'starter-version'), 'utf8').trim() || 'unknown';
  } catch (error) {
    if (error.code !== 'ENOENT') throw error;
  }
  const marker = {
    sha,
    starter_tag: starterTag,
    built_at: builtAt,
    workflow_run_id: env.VERSION_SHA ? (env.GITHUB_RUN_ID || 'local') : 'local',
    run_attempt: env.VERSION_SHA ? (env.GITHUB_RUN_ATTEMPT || '0') : '0',
  };
  const web = resolve(root, 'src/App.Web');
  const template = readFileSync(resolve(web, 'wwwroot/index.html'), 'utf8');
  const meta = /<meta name="app-version" content="[^"]*" \/>/;
  if (!meta.test(template)) throw new Error('Missing app-version meta placeholder');
  const legacyJson = resolve(web, 'wwwroot/version.json');
  if (existsSync(legacyJson)) {
    if (!isTracked(root, legacyJson) && isLegacyJson(legacyJson)) rmSync(legacyJson);
    else console.error(`Warning: preserving ${legacyJson}; it is tracked or is not a generated legacy version marker.`);
  }
  const legacyDir = resolve(web, '.version');
  const legacyIndex = resolve(legacyDir, 'index.html');
  if (existsSync(legacyDir)) {
    if (lstatSync(legacyDir).isDirectory()) {
      if (existsSync(legacyIndex) && lstatSync(legacyIndex).isFile() && !isTracked(root, legacyIndex)
          && /<meta name="app-version" content="(?:[a-f0-9]{40}|local)" \/>/.test(readFileSync(legacyIndex, 'utf8'))) {
        rmSync(legacyIndex);
      }
      if (readdirSync(legacyDir).length === 0) rmdirSync(legacyDir);
      else console.error(`Warning: preserving remaining user files in ${legacyDir}.`);
    } else console.error(`Warning: preserving ${legacyDir}; it is not a generated legacy directory.`);
  }
  mkdirSync(resolve(web, 'obj/version-marker'), { recursive: true });
  writeFileSync(resolve(web, 'obj/version-marker/index.html'), template.replace(meta, `<meta name="app-version" content="${sha}" />`));
  writeFileSync(resolve(web, 'obj/version-marker/version.json'), `${JSON.stringify(marker)}\n`);
  return marker;
}

if (process.argv[1] && existsSync(process.argv[1]) && realpathSync(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
  console.log(JSON.stringify(writeVersionMarker(root)));
}
