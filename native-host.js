import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { basename } from 'node:path';

// Daemon-controlled manager for the Windows desktop pet process.
export class PetManager {
  constructor({ platform = process.platform, launch = spawn, warn = console.warn, getPanelUrl, getLastSelection, timeoutMs = 15000 } = {}) {
    this.platform = platform;
    this.launch = launch;
    this.warn = warn;
    this.getPanelUrl = getPanelUrl;
    this.getLastSelection = getLastSelection;
    this.child = null;
    this.stopped = false;
    this.timeoutMs = timeoutMs;
    this.ready = false;
    this.startResult = null;
    this.pending = new Map();
    this.sequence = 0;
  }

  isAlive() {
    return Boolean(this.child && !this.child.killed && this.child.exitCode === null);
  }

  startChild() {
    if (this.platform !== 'win32' || this.stopped) return false;
    this.ready = false;
    let complete;
    this.startResult = new Promise(resolve => { complete = resolve; });
    const timer = setTimeout(() => {
      finish({ ok: false, error: 'Desktop pet did not report a visible window before timeout' });
      child?.kill();
    }, this.timeoutMs);
    let settled = false;
    const finish = result => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      complete(result);
    };
    this.cancelStart = () => finish({ ok: false, error: 'Desktop pet was stopped' });
    let child;
    let stderr = '';
    try {
      child = this.launch(fileURLToPath(new URL('./desktop-pet/WhalePet.exe', import.meta.url)), ['--host-pipe'], {
        windowsHide: true,
        env: {
          ...process.env,
          DSH_WHALE_HARNESS_EXE: [process.execPath, process.env.DSH_DESKTOP_NODE_EXECUTABLE]
            .find(path => path && /^deepseek harness\.exe$/i.test(basename(path))) || '',
        },
        stdio: ['pipe', 'pipe', 'pipe'],
      });

      this.child = child;

      child.on('error', error => {
        this.warn(`[pet-whale] desktop process error: ${error.message}`);
        finish({ ok: false, error: error.message });
        if (this.child === child) this.child = null;
      });

      child.on('exit', (code, signal) => {
        const error = stderr.trim() || `Desktop pet exited before confirmation (code ${code}, signal ${signal})`;
        finish({ ok: false, error });
        for (const request of this.pending.values()) {
          if (request.child === child) request.finish({ ok: false, error });
        }
        if (this.child === child) this.child = null;
      });

      child.stdin?.on('error', error => {
        finish({ ok: false, error: error.message });
        for (const request of this.pending.values()) {
          if (request.child === child) request.finish({ ok: false, error: error.message });
        }
      });
      child.stderr?.on('data', chunk => { stderr = (stderr + chunk).slice(-4096); });

      let pending = '';
      child.stdout?.setEncoding('utf8');
      child.stdout?.on('data', chunk => {
        pending += chunk;
        const lines = pending.split('\n');
        pending = lines.pop().slice(-4096);
        for (const line of lines) {
          const message = line.trim();
          if (message === 'pet:ready' || message === 'pet:forwarded') {
            this.ready = message === 'pet:ready';
            finish({ ok: true, status: this.ready ? 'launched' : 'woken' });
            continue;
          }
          if (message.startsWith('pet:awake:')) {
            const request = this.pending.get(message.slice('pet:awake:'.length));
            if (request?.child === child) request.finish({ ok: true, status: 'woken' });
            continue;
          }
          if (message !== 'pet:mini-panel') continue;
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
      finish({ ok: false, error: error.message });
      return false;
    }
  }

  async launchOrWake() {
    if (this.platform !== 'win32' || this.stopped) return { ok: false, error: 'Desktop pet is unavailable on this platform or has stopped' };
    if (this.isAlive()) {
      if (!this.ready) return this.startResult;
      const child = this.child;
      const id = String(++this.sequence);
      return new Promise(resolve => {
        const timer = setTimeout(() => finish({ ok: false, error: 'Desktop pet did not acknowledge wake-up' }), this.timeoutMs);
        const finish = result => {
          clearTimeout(timer);
          this.pending.delete(id);
          resolve(result);
        };
        this.pending.set(id, { child, finish });
        try {
          if (!child.stdin || child.stdin.destroyed || child.stdin.writableEnded) throw new Error('Desktop pet input pipe is closed');
          child.stdin.write(`wake:${id}\n`, error => { if (error) finish({ ok: false, error: error.message }); });
        } catch (error) { finish({ ok: false, error: error.message }); }
      });
    }
    const started = this.startChild();
    return started || this.startResult ? this.startResult : { ok: false, error: 'Desktop pet is unavailable on this platform or has stopped' };
  }

  stop() {
    this.stopped = true;
    this.cancelStart?.();
    for (const request of this.pending.values()) request.finish({ ok: false, error: 'Desktop pet was stopped' });
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
