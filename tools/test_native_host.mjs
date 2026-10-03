import assert from 'node:assert/strict';
import { EventEmitter, once } from 'node:events';
import { spawn } from 'node:child_process';
import { readFileSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { startDesktopPet } from '../native-host.js';
import { apply } from '../index.js';

let launches = 0;
startDesktopPet({ platform: 'linux', launch() { launches++; } })();
assert.equal(launches, 0);
let ends = 0;
const fake = new EventEmitter();
fake.stdin = new EventEmitter();
fake.stdin.end = () => ends++;
const warnings = [];
const stop = startDesktopPet({ platform: 'win32', launch: () => fake, warn: text => warnings.push(text) });
fake.emit('error', new Error('ENOENT'));
fake.stdin.emit('error', new Error('EPIPE'));
stop(); stop();
assert.equal(ends, 1);
assert.match(warnings[0], /ENOENT/);
startDesktopPet({ platform: 'win32', launch() { throw new Error('denied'); }, warn: text => warnings.push(text) })();
assert.match(warnings[1], /denied/);

const transport = new EventEmitter();
transport.stdin = new EventEmitter();
const responses = [];
transport.stdin.write = value => responses.push(value);
transport.stdin.end = () => {};
transport.stdout = new EventEmitter();
transport.stdout.setEncoding = () => {};
startDesktopPet({ platform: 'win32', launch: () => transport,
  getPanelUrl: () => 'http://127.0.0.1:19387/?test-auth=fixture' });
transport.stdout.emit('data', 'pet:mini-');
assert.equal(responses.length, 0);
transport.stdout.emit('data', 'panel\r\n');
assert.equal(responses[0], 'panel:http://127.0.0.1:19387/?test-auth=fixture\n');
transport.stdout.emit('data', '[pet] unrelated output\n');
assert.equal(responses.length, 1);
const rejected = new EventEmitter();
rejected.stdin = transport.stdin;
rejected.stdout = new EventEmitter();
rejected.stdout.setEncoding = () => {};
startDesktopPet({ platform: 'win32', launch: () => rejected, getPanelUrl: () => 'https://example.com' });
rejected.stdout.emit('data', 'pet:mini-panel\n');
assert.match(responses[1], /^panel-error:/);
console.log('PASS: mini-panel pipe framing, authenticated loopback URL and external endpoint rejection');

if (process.platform === 'win32') {
  const folder = mkdtempSync(join(tmpdir(), 'whale-test-'));
  const log = join(folder, 'startup.log');
  const exe = fileURLToPath(new URL('../desktop-pet/WhalePet.exe', import.meta.url));
  const child = spawn(exe, ['--host-pipe', `--log=${log}`], { windowsHide: true, stdio: ['pipe', 'ignore', 'pipe'] });
  const exited = once(child, 'exit');
  const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
  const deadline = Date.now() + 10000;
  try {
    let output = '';
    while (Date.now() < deadline) {
      try { output = readFileSync(log, 'utf8'); } catch {}
      if (output.includes('shown at')) break;
      await delay(100);
    }
    assert.match(output, /visible=True hwnd=[1-9]/);
    const duplicate = spawn(exe, ['--host-pipe'], { windowsHide: true, stdio: ['pipe', 'ignore', 'ignore'] });
    assert.equal((await once(duplicate, 'exit'))[0], 0);
    assert.equal(child.exitCode, null);
    child.stdin.end();
    const result = await Promise.race([exited, delay(5000).then(() => { throw new Error('pipe close did not stop pet'); })]);
    assert.equal(result[0], 0);
    assert.match(readFileSync(log, 'utf8'), /message loop ended/);
    // Exercise the actual plugin with no webServer: native startup must still run.
    let dispose;
    apply({ effect(callback) { dispose = callback(); }, get() { return undefined; } });
    assert.equal(typeof dispose, 'function');
    await delay(1000);
    dispose();
    await delay(1000);
    console.log('PASS: real native window, single instance, pipe shutdown and Host lifecycle without webServer');
  } finally {
    if (child.exitCode === null) child.kill();
    rmSync(folder, { recursive: true, force: true });
  }
}
console.log('PASS: non-Windows skip, launch failures and idempotent disposal');
