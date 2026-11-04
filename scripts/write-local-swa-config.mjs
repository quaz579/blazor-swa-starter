import { readFileSync, mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(fileURLToPath(new URL('..', import.meta.url)));
const config = JSON.parse(readFileSync(resolve(root, 'src/App.Web/wwwroot/staticwebapp.config.json'), 'utf8'));
config.routes = config.routes.filter((route) => route.route !== '/api/__test/*');
const target = resolve(root, '.azurite/local-swa');
mkdirSync(target, { recursive: true });
writeFileSync(resolve(target, 'staticwebapp.config.json'), `${JSON.stringify(config, null, 2)}\n`);
