// Optional compatibility check against an installed Harness's actual cookie/fence implementation.
// Uses fresh in-memory test secrets; never reads the user's credentials or contacts a running host.
import assert from 'node:assert/strict';
import { openSync, readSync, closeSync } from 'node:fs';
import { createHash, createHmac, randomBytes, timingSafeEqual } from 'node:crypto';
import vm from 'node:vm';
import { setupSyncRelay } from '../sync-host.js';

const archive = process.argv[2];
assert.ok(archive, 'Usage: node tools/test_harness_auth.mjs <Harness resources/app.asar>');
const file = openSync(archive, 'r');
let source;
try {
  const prefix = Buffer.alloc(16);
  readSync(file, prefix, 0, 16, 0);
  const json = Buffer.alloc(prefix.readUInt32LE(12));
  readSync(file, json, 0, json.length, 16);
  let entry = JSON.parse(json.toString());
  for (const segment of 'dsh/node_modules/@deepseek-ai/dsh-client-connection/lib/index.js'.split('/'))
    entry = entry.files[segment];
  assert.equal(entry.unpacked, undefined, 'Connection module moved outside ASAR; update this compatibility check');
  const bytes = Buffer.alloc(entry.size);
  readSync(file, bytes, 0, bytes.length, 8 + prefix.readUInt32LE(4) + Number(entry.offset));
  source = bytes.toString();
} finally { closeSync(file); }

// Execute only the shipped pure auth helpers, isolating unrelated Cordis/RPC dependencies.
function region(name) {
  const start = source.indexOf(`//#region lib/types/${name}.js`);
  assert.notEqual(start, -1, `Harness auth region missing: ${name}`);
  return source.slice(start, source.indexOf('//#endregion', start));
}
function hostMethod(name) {
  const method = source.match(new RegExp(`\\t${name}\\(request\\) \\{([\\s\\S]*?)\\n\\t\\}`));
  assert.ok(method, `Harness admission method missing: ${name}`);
  return `${name}(request) {${method[1]}\n}`;
}
let now = Date.now();
class TestDate extends Date { static now() { return now; } }
const sandbox = {
  Buffer, URL, Headers, Date: TestDate, createHash, createHmac, randomBytes, timingSafeEqual,
  credentialKey: () => 'isolated-test-browser-session'
};
vm.runInNewContext([
  region('loopback-hostname'), region('api-request-trust'), region('browser-auth'),
  `globalThis.fixtures = { BrowserAuth, ${hostMethod('requestRejection')}, ${hostMethod('admit')} };`
].join('\n'), sandbox);
const { fixtures } = sandbox;
const browserAuth = new fixtures.BrowserAuth({}, randomBytes(32), 1);
const connection = {
  trustedHosts: [], browserAuth, operator: {},
  requestRejection: fixtures.requestRejection, admit: fixtures.admit
};
const authority = '127.0.0.1:19387';
const origin = `http://${authority}`;
let minted;
let exchangeStatus;
browserAuth.authorizeIndex({
  method: 'GET', url: new URL(browserAuth.authenticatedUrl(origin)).pathname + new URL(browserAuth.authenticatedUrl(origin)).search,
  headers: { host: authority }
}, {
  writeHead(status, headers) { exchangeStatus = status; minted = headers['set-cookie']; }, end() {}
});
assert.equal(exchangeStatus, 303);
assert.match(minted, /HttpOnly; SameSite=Strict/);
const cookie = minted.split(';')[0];

const routes = new Map();
const server = { register(route) { routes.set(route.path, route); return () => routes.delete(route.path); } };
let cleanup;
let profileReads = 0;
const account = {
  async getState() { profileReads++; return { status: 'authenticated' }; },
  async getProfile() { return { value: { name: 'Fixture user', contact: 'audit@example.invalid' } }; },
  async getBalance() { return []; }
};
const ctx = {
  get(name) { return { webServer: server, connection, deepseekAccount: account }[name]; },
  on() {}, effect(setup) { cleanup = setup(); },
  inject(names, activate) { activate(this); return { dispose() { cleanup(); } }; }
};
const relay = setupSyncRelay(ctx, null);
async function request(path, headers, method = 'GET') {
  const res = { status: 0, body: '', headers: {},
    setHeader(name, value) { this.headers[name] = value; },
    writeHead(status, values) { this.status = status; Object.assign(this.headers, values); },
    end(body) { this.body = body || ''; }
  };
  await routes.get(path).handler({ method, url: path, headers }, res);
  return res;
}
try {
  const path = '/pet-whale/account';
  assert.equal((await request(path, { host: authority })).status, 401);
  assert.equal((await request(path, { host: authority, cookie: cookie + 'tampered' })).status, 401);
  assert.equal((await request(path, { host: authority, cookie, origin: 'https://foreign.example.invalid' })).status, 403);
  assert.equal((await request(path, { host: 'foreign.example.invalid', cookie })).status, 403);
  assert.equal((await request(path, { host: authority, cookie, 'sec-fetch-site': 'cross-site' })).status, 403);
  assert.equal(profileReads, 0, 'Rejected real-cookie requests must not read account data');
  const authorized = await request(path, { host: authority, cookie, origin });
  assert.equal(authorized.status, 200);
  assert.deepEqual(JSON.parse(authorized.body).user, { name: 'Fixture user' });
  assert.equal(authorized.headers['Access-Control-Allow-Origin'], undefined);
  assert.equal((await request('/pet-whale/account/logout', { host: authority, cookie }, 'GET')).status, 405);
  now += 86400001;
  assert.equal((await request(path, { host: authority, cookie })).status, 401);
  assert.equal(profileReads, 1);
  console.log('PASS: installed Harness token exchange, signed Cookie validation, tampering/expiry, Host/Origin fences and protected pet route compatibility');
} finally { relay.dispose(); }
