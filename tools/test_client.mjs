import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const requests = [];
const errors = [];
const entries = {};
const cleanups = [];
let plugin;
let responseStatus = 200;
let stream;
const jsx = { jsx: (type, props) => ({ type, props }), jsxs: (type, props) => ({ type, props }) };
vm.runInNewContext(readFileSync(new URL('../client.js', import.meta.url), 'utf8'), {
  window: {
    __ModuleLoader__: { load({ factory }) {
      plugin = factory(name => name === 'react' ? { useState: () => [false, () => {}] } : jsx);
    } },
    addEventListener() {}, removeEventListener() {}
  },
  fetch: async (url, init) => {
    requests.push({ url, init });
    return { ok: responseStatus === 200, status: responseStatus, json: async () => ({ ok: true }) };
  },
  EventSource: class {
    constructor(url) { this.url = url; stream = this; }
    close() { this.closed = true; }
  },
  console: { warn: (...args) => errors.push(args), error: (...args) => errors.push(args) },
  setTimeout: callback => callback(),
  setInterval: () => 123,
  clearInterval: () => {}
});
const services = {
  uiWorkspace: { selection: { subscribe: () => () => {}, getSnapshot: () => ({ sessionId: 'initial-session' }) } },
  commandUi: { register: command => { entries.command = command; return () => {}; } },
  shortcuts: { register: shortcut => { entries.shortcut = shortcut; return () => {}; } },
  slots: {
    inject: (_name, callback) => callback(),
    register: (_options, component) => { entries.sidebar = component; }
  }
};
const ctx = {
  get: name => services[name],
  slots: services.slots,
  inject: (_names, callback) => callback(ctx),
  effect: setup => cleanups.push(setup())
};
plugin.apply(ctx);
await new Promise(resolve => setImmediate(resolve));
assert.equal(requests[0].url, '/pet-whale/sync');
assert.equal(JSON.parse(requests[0].init.body).sessionId, 'initial-session');
assert.equal(stream.url, '/pet-whale/sync');
await entries.command.ui.run();
await entries.shortcut.run();
entries.sidebar().props.onClick();
await new Promise(resolve => setImmediate(resolve));
assert.equal(requests.filter(request => request.url === '/pet-whale/launch').length, 3);
assert.ok(requests.every(request => request.init.method === 'POST' && request.init.credentials === 'include'));
assert.equal(errors.length, 0);
responseStatus = 405;
await entries.command.ui.run();
assert.match(String(errors[0][1]), /HTTP 405/);
for (const cleanup of cleanups) cleanup?.();
assert.equal(stream.closed, true);
console.log('PASS: initial selection, all three launch entrypoints, HTTP failure reporting and SSE cleanup');
