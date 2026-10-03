import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { basename } from 'node:path';

// Daemon-controlled manager for the Windows desktop pet process.
export class PetManager {
  constructor({ platform = process.platform, launch = spawn, warn = console.warn, getPanelUrl, getLastSelection } = {}) {
    this.platform = platform;
    this.launch = launch;
    this.warn = warn;
    this.getPanelUrl = getPanelUrl;
    this.getLastSelection = getLastSelection;
    this.child = null;
    this.stopped = false;
  }

  isAlive() {
    return Boolean(this.child && !this.child.killed && this.child.exitCode === null);
  }

  startChild() {
    if (this.platform !== 'win32' || this.stopped) return false;
    try {
      const child = this.launch(fileURLToPath(new URL('./desktop-pet/WhalePet.exe', import.meta.url)), ['--host-pipe'], {
        windowsHide: true,
        env: {
          ...process.env,
          DSH_WHALE_HARNESS_EXE: [process.execPath, process.env.DSH_DESKTOP_NODE_EXECUTABLE]
            .find(path => path && /^deepseek harness\.exe$/i.test(basename(path))) || '',
        },
        stdio: ['pipe', 'pipe', 'ignore'],
      });

      this.child = child;

      child.on('error', error => {
        this.warn(`[pet-whale] desktop process error: ${error.message}`);
        if (this.child === child) this.child = null;
      });

      child.on('exit', () => {
        if (this.child === child) this.child = null;
      });

      child.stdin?.on('error', () => {}); // User may close or kill the pet first.

      let pending = '';
      child.stdout?.setEncoding('utf8');
      child.stdout?.on('data', chunk => {
        pending += chunk;
        const lines = pending.split('\n');
        pending = lines.pop().slice(-4096);
        for (const line of lines) {
          if (line.trim() !== 'pet:mini-panel') continue;
          try {
            const url = new URL(this.getPanelUrl());
            if (url.protocol !== 'http:' || url.hostname !== '127.0.0.1') throw new Error('Invalid local endpoint');
            const selection = typeof this.getLastSelection === 'function' ? this.getLastSelection() : null;
            if (selection?.sessionId) {
              url.searchParams.set('initialSession', selection.sessionId);
            }
            child.stdin?.write(`panel:${url.href}\n`);
          } catch {
            child.stdin?.write('panel-error:DSH connection unavailable\n');
          }
        }
      });
      return true;
    } catch (error) {
      this.warn(`[pet-whale] desktop launch failed: ${error.message}`);
      return false;
    }
  }

  launchOrWake() {
    if (this.isAlive()) {
      try {
        this.child.stdin?.write('wake\n');
        return { ok: true, status: 'woken' };
      } catch (e) {
        // Stdin broken, restart
      }
    }
    const started = this.startChild();
    return { ok: started, status: started ? 'launched' : 'failed' };
  }

  stop() {
    this.stopped = true;
    if (this.child) {
      try {
        this.child.stdin?.end();
      } catch (e) {}
      this.child = null;
    }
  }
}

export function startDesktopPet(options = {}) {
  const manager = new PetManager(options);
  manager.startChild();
  return {
    manager,
    stop: () => manager.stop()
  };
}
