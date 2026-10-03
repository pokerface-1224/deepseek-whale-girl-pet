import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { basename } from 'node:path';

// An open stdin pipe leases the window to this plugin. Closing the pipe also
// handles abrupt Host termination without leaving an orphan desktop pet.
export function startDesktopPet({ platform = process.platform, launch = spawn, warn = console.warn, getPanelUrl } = {}) {
  if (platform !== 'win32') return () => {};
  let child;
  try {
    child = launch(fileURLToPath(new URL('./desktop-pet/WhalePet.exe', import.meta.url)), ['--host-pipe'], {
      windowsHide: true,
      env: {
        ...process.env,
        // Pass the actual carrier executable; never hard-code an install path.
        DSH_WHALE_HARNESS_EXE: [process.execPath, process.env.DSH_DESKTOP_NODE_EXECUTABLE]
          .find(path => path && /^deepseek harness\.exe$/i.test(basename(path))) || '',
      },
      stdio: ['pipe', 'pipe', 'ignore'],
    });
    child.on('error', error => warn(`[pet-whale] desktop launch failed: ${error.message}`));
    child.stdin?.on('error', () => {}); // User may close the pet first.
    let pending = '';
    child.stdout?.setEncoding('utf8');
    child.stdout?.on('data', chunk => {
      pending += chunk;
      const lines = pending.split('\n');
      pending = lines.pop().slice(-4096);
      for (const line of lines) {
        if (line.trim() !== 'pet:mini-panel') continue;
        try {
          const url = new URL(getPanelUrl());
          if (url.protocol !== 'http:' || url.hostname !== '127.0.0.1') throw new Error('Invalid local endpoint');
          // Authentication stays in the private parent/child pipe, never logs or argv.
          child.stdin.write(`panel:${url.href}\n`);
        } catch {
          child.stdin.write('panel-error:DSH connection unavailable\n');
        }
      }
    });
  } catch (error) {
    warn(`[pet-whale] desktop launch failed: ${error.message}`);
  }
  let stopped = false;
  return () => {
    if (stopped) return;
    stopped = true;
    child?.stdin?.end();
  };
}
