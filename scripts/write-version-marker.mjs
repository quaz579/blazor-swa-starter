import { execFileSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

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
  let starterTag = 'unknown';
  try {
    starterTag = readFileSync(resolve(root, 'starter-version'), 'utf8').trim() || 'unknown';
  } catch (error) {
    if (error.code !== 'ENOENT') throw error;
  }
  const marker = {
    sha,
    starter_tag: starterTag,
    built_at: new Date().toISOString(),
    workflow_run_id: env.VERSION_SHA ? (env.GITHUB_RUN_ID || 'local') : 'local',
    run_attempt: env.VERSION_SHA ? (env.GITHUB_RUN_ATTEMPT || '0') : '0',
  };
  const web = resolve(root, 'src/App.Web');
  const template = readFileSync(resolve(web, 'wwwroot/index.html'), 'utf8');
  const meta = /<meta name="app-version" content="[^"]*" \/>/;
  if (!meta.test(template)) throw new Error('Missing app-version meta placeholder');
  mkdirSync(resolve(web, '.version'), { recursive: true });
  writeFileSync(resolve(web, '.version/index.html'), template.replace(meta, `<meta name="app-version" content="${sha}" />`));
  writeFileSync(resolve(web, 'wwwroot/version.json'), `${JSON.stringify(marker)}\n`);
  return marker;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
  console.log(JSON.stringify(writeVersionMarker(root)));
}
