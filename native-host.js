import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

// An open stdin pipe leases the window to this plugin. Closing the pipe also
// handles abrupt Host termination without leaving an orphan desktop pet.
export function startDesktopPet({ platform = process.platform, launch = spawn, warn = console.warn } = {}) {
  if (platform !== 'win32') return () => {};
  let child;
  try {
    child = launch(fileURLToPath(new URL('./desktop-pet/WhalePet.exe', import.meta.url)), ['--host-pipe'], {
      windowsHide: true,
      stdio: ['pipe', 'ignore', 'ignore'],
    });
    child.on('error', error => warn(`[pet-whale] desktop launch failed: ${error.message}`));
    child.stdin?.on('error', () => {}); // User may close the pet first.
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
