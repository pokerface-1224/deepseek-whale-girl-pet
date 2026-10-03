/** Pre-install check: manifest shape, declared files, module syntax. */
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));

const problems = [];

if (!pkg.name || !pkg.version) problems.push('manifest needs name and version');
if (pkg.dsh?.bundle?.patch !== './cordis.patch.yml') problems.push('dsh.bundle.patch must point at cordis.patch.yml');
for (const rel of ['index.js', 'sync-host.js', 'client.js', 'native-host.js', 'desktop-pet/WhalePet.exe', 'desktop-pet/Microsoft.Web.WebView2.Core.dll', 'desktop-pet/Microsoft.Web.WebView2.WinForms.dll', 'desktop-pet/WebView2Loader.dll', 'desktop-pet/WebView2-LICENSE.txt', 'desktop-pet/src/MiniPanel.cs', 'desktop-pet/art/front.png', 'desktop-pet/art/side.png', 'desktop-pet/art/back.png', 'desktop-pet/art/thinking.png', 'desktop-pet/art/daydreaming.png', 'desktop-pet/art/slacking.png', 'desktop-pet/art/bored.png', 'desktop-pet/art/playing.png', 'cordis.patch.yml', 'icon.png', 'locale/en.json', 'locale/zh.json']) {
  if (!fs.existsSync(path.join(root, rel))) problems.push('missing file ' + rel);
}

const iconSize = fs.statSync(path.join(root, 'icon.png')).size;
if (iconSize > 256 * 1024) problems.push('icon exceeds 256 KiB: ' + iconSize);

const patch = fs.readFileSync(path.join(root, 'cordis.patch.yml'), 'utf8');
if (!patch.includes("name: '@local/dsh-pet-whale'")) problems.push('patch row name must equal the package name');

for (const locale of ['en', 'zh']) {
  const dict = JSON.parse(fs.readFileSync(path.join(root, 'locale', locale + '.json'), 'utf8'));
  if (!dict['meta.title']) problems.push('locale/' + locale + '.json needs meta.title');
}

const node = process.execPath;
for (const rel of ['index.js', 'sync-host.js', 'client.js', 'native-host.js']) {
  try {
    execFileSync(node, ['--check', path.join(root, rel)], { stdio: 'pipe' });
  } catch (error) {
    problems.push(rel + ' failed syntax check: ' + String(error.stderr || error.message));
  }
}

if (problems.length) {
  console.log('FAIL');
  for (const problem of problems) console.log(' - ' + problem);
  process.exit(1);
}
console.log('OK: manifest, declared files, module syntax, patch row and locale dictionaries all check out.');
console.log('OK: native-only package, no Harness client entry.');
