import assert from 'node:assert/strict';
import { EventEmitter, once } from 'node:events';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { PetManager, startDesktopPet } from '../native-host.js';

function fakeChild() {
  const child = new EventEmitter();
  child.exitCode = null;
  child.stdin = new EventEmitter();
  child.stdin.write = () => true;
  child.stdin.end = () => { child.exitCode = 0; child.emit('exit', 0, null); };
  child.stdout = new EventEmitter();
  child.stdout.setEncoding = () => {};
  child.stderr = new EventEmitter();
  child.kill = () => { child.killed = true; child.emit('exit', null, 'SIGTERM'); };
  return child;
}
let launches = 0;
const unsupported = startDesktopPet({ platform: 'linux', launch() { launches++; } });
assert.equal((await unsupported.manager.launchOrWake()).ok, false);
unsupported.stop();
assert.equal(launches, 0);
const child = fakeChild();
const manager = new PetManager({ platform: 'win32', launch: () => child });
let completed = false;
const starting = manager.launchOrWake().then(result => { completed = true; return result; });
await Promise.resolve();
assert.equal(completed, false, 'spawn alone must not report success');
child.stdout.emit('data', 'pet:rea');
assert.equal(completed, false);
child.stdout.emit('data', 'dy\r\n');
assert.deepEqual(await starting, { ok: true, status: 'launched' });
let wakeCommand;
child.stdin.write = command => { wakeCommand = command; return true; };
const waking = manager.launchOrWake();
assert.match(wakeCommand, /^wake:\d+\n$/);
child.stdout.emit('data', `pet:awake:${wakeCommand.trim().slice(5)}\n`);
assert.deepEqual(await waking, { ok: true, status: 'woken' });
const waiting = manager.launchOrWake();
child.stdin.emit('error', new Error('EPIPE'));
assert.match((await waiting).error, /EPIPE/);
manager.stop();
manager.stop();
assert.equal((await manager.launchOrWake()).ok, false);
for (const mode of ['spawn-error', 'exit-zero', 'exit-error', 'forwarded', 'timeout']) {
  const child = fakeChild();
  const pet = new PetManager({ platform: 'win32', launch: () => child, warn: () => {}, timeoutMs: 20 });
  const result = pet.launchOrWake();
  if (mode === 'spawn-error') child.emit('error', new Error('ENOENT'));
  if (mode === 'exit-zero') child.emit('exit', 0, null);
  if (mode === 'exit-error') {
    child.stderr.emit('data', 'Existing pet did not acknowledge wake-up');
    child.emit('exit', 1, null);
  }
  if (mode === 'forwarded') {
    child.stdout.emit('data', 'pet:forwarded\n');
    child.emit('exit', 0, null);
  }
  const reply = await result;
  assert.equal(reply.ok, mode === 'forwarded', mode);
  if (mode === 'exit-error') assert.match(reply.error, /Existing pet/);
  if (mode === 'timeout') assert.equal(child.killed, true);
  pet.stop();
}
const transport = fakeChild();
const responses = [];
transport.stdin.write = message => responses.push(message);
const panel = startDesktopPet({ platform: 'win32', launch: () => transport,
  getPanelUrl: () => 'http://127.0.0.1:19387/?test-auth=fixture',
  getLastSelection: () => ({ sessionId: 'session-one' }) });
transport.stdout.emit('data', 'pet:ready\npet:mini-');
transport.stdout.emit('data', 'panel\n');
assert.equal(responses[0], 'panel:http://127.0.0.1:19387/?test-auth=fixture&initialSession=session-one\n');
panel.stop();
console.log('PASS: readiness, wake acknowledgement, startup/pipe/exit/timeout failures and panel framing');
if (process.argv.includes('--native')) {
  assert.equal(process.platform, 'win32');
  const pet = new PetManager();
  const duplicate = new PetManager();
  try {
    assert.deepEqual(await pet.launchOrWake(), { ok: true, status: 'launched' });
    const windowTest = readFileSync(new URL('./test_pet_window.ps1', import.meta.url), 'utf8');
    const inspect = mode => execFileSync('powershell.exe', ['-NoProfile', '-Command', `& {\n${windowTest}\n} -PetId ${pet.child.pid} -Mode ${mode}`], { windowsHide: true, stdio: 'pipe' });
    inspect('hide');
    assert.deepEqual(await pet.launchOrWake(), { ok: true, status: 'woken' });
    inspect('visible');
    inspect('hide');
    assert.deepEqual(await duplicate.launchOrWake(), { ok: true, status: 'woken' });
    inspect('visible');
    const exited = once(pet.child, 'exit');
    pet.stop();
    assert.equal((await exited)[0], 0);
    console.log('PASS: real native window, hidden-window pipe wake, acknowledged duplicate wake and shutdown');
  } finally { pet.stop(); duplicate.stop(); }
}
