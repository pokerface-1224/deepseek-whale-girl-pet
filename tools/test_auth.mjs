import assert from 'node:assert/strict';
import { mock } from 'node:test';
import { setupSyncRelay, getAccountSnapshot } from '../sync-host.js';

const origin = 'http://127.0.0.1:19387';
const fixtureCookie = 'audit-session=valid-fixture';
let cookieValid = true;
let failAdmission = false;
let effects = 0;
const connection = {
  admit(req) {
    if (failAdmission) throw new Error('unavailable');
    // Model the inspected Harness admit() contract, not DeepSeek account login.
    const headers = req.headers || {};
    if (headers.host !== '127.0.0.1:19387' || headers['sec-fetch-site'] === 'cross-site'
      || (headers.origin && headers.origin !== origin)) return { rejection: 403 };
    return cookieValid && headers.cookie === fixtureCookie ? { peer: {} } : { rejection: 401 };
  }
};
const account = {
  async getState() { effects++; return { status: 'authenticated' }; },
  async getProfile() { return { value: { contact: 'audit@example.invalid' } }; },
  async getBalance() { return { value: [{ balance: '10.00', currency: 'CNY' }] }; },
  async signOut() { effects++; }
};
const routes = new Map();
const server = {
  register(route) { routes.set(route.path, route); return () => routes.delete(route.path); }
};
let cleanup;
const ctx = {
  get(name) { return name === 'webServer' ? server : name === 'connection' ? connection : name === 'deepseekAccount' ? account : null; },
  on() {},
  effect(fn) { cleanup = fn(); },
  inject(names, activate) { assert.deepEqual(names, ['webServer', 'connection']); activate(this); return { dispose() { cleanup(); } }; }
};
const headers = { host: '127.0.0.1:19387', origin, cookie: fixtureCookie };
function response() {
  return {
    status: 0, headers: {}, body: '', writes: [], ended: false,
    setHeader(name, value) { this.headers[name] = value; },
    writeHead(status, values) { this.status = status; Object.assign(this.headers, values); },
    write(value) { this.writes.push(value); },
    end(value) { this.ended = true; this.body = value || ''; },
    on() {}, flushHeaders() {}
  };
}
async function request(path, method = 'GET', requestHeaders = headers, extra = {}) {
  const res = response();
  await routes.get(path).handler({ method, headers: requestHeaders, ...extra }, res);
  return res;
}

mock.timers.enable({ apis: ['setInterval'] });
const relay = setupSyncRelay(ctx, {
  launchOrWake() { effects++; return { ok: true }; },
  isAlive() { effects++; return true; },
  sendPetMessage() { effects++; }
});
try {
  assert.equal(routes.size, 14);
  for (const path of routes.keys()) {
    const method = /\/(sync|status|account)$/.test(path) ? 'GET' : 'POST';
    for (const cookie of [undefined, 'audit-session=forged-fixture', 'audit-session=expired-fixture']) {
      const res = await request(path, method, { host: headers.host, cookie });
      assert.equal(res.status, 401, path);
      assert.equal(res.headers['Cache-Control'], 'no-store');
      assert.equal(res.headers['Access-Control-Allow-Origin'], undefined);
    }
    for (const malicious of [{ origin: 'https://foreign.example.invalid' }, { origin: 'null' },
      { host: 'foreign.example.invalid' }, { 'sec-fetch-site': 'cross-site' }]) {
      assert.equal((await request(path, method, { ...headers, ...malicious })).status, 403, path);
    }
  }
  assert.equal(effects, 0, 'Denied requests must not read the profile or change state');
  assert.equal((await request('/pet-whale/account', 'GET', { host: headers.host }, { url: '/pet-whale/account?token=fixture' })).status, 401,
    'Launch-token URLs do not authenticate API requests');

  let res = await request('/pet-whale/account');
  assert.equal(res.status, 200);
  const snapshot = JSON.parse(res.body);
  assert.deepEqual(snapshot.user, { name: '已登录用户' });
  assert.equal(snapshot.balance.total, '10.00');
  assert.equal(res.body.includes('audit@example.invalid'), false, 'Contact must not become the display name');
  assert.equal(res.headers['Access-Control-Allow-Origin'], undefined);
  assert.equal(res.headers['Cache-Control'], 'no-store');
  const effectCount = effects;
  assert.equal((await request('/pet-whale/launch')).status, 405);
  assert.equal((await request('/pet-whale/account/logout')).status, 405);
  assert.equal(effects, effectCount);
  assert.equal((await request('/pet-whale/launch', 'POST')).status, 200);
  assert.equal((await request('/pet-whale/account/logout', 'POST')).status, 200);
  // Native/non-browser requests still need a valid cookie but may omit Origin.
  assert.equal((await request('/pet-whale/status', 'GET', { host: headers.host, cookie: fixtureCookie })).status, 200);

  const stream = await request('/pet-whale/sync');
  assert.equal(stream.status, 200);
  await relay.broadcastAccount();
  assert.equal(stream.writes.length, 1);
  assert.equal(stream.writes[0].includes('audit@example.invalid'), false);
  cookieValid = false;
  await relay.broadcastAccount();
  assert.equal(stream.ended, true);
  assert.equal(stream.writes.length, 1, 'Expired SSE gets no further private data');
  assert.equal((await request('/pet-whale/sync')).status, 401);
  cookieValid = true;
  const idleStream = await request('/pet-whale/sync');
  cookieValid = false;
  mock.timers.tick(30000);
  assert.equal(idleStream.ended, true, 'Idle SSE closes without needing a broadcast');
  cookieValid = true;

  failAdmission = true;
  assert.equal((await request('/pet-whale/account')).status, 503);
  failAdmission = false;
  const admit = connection.admit;
  connection.admit = undefined;
  assert.equal((await request('/pet-whale/account')).status, 503);
  connection.admit = admit;
  const openStream = await request('/pet-whale/sync');
  relay.dispose();
  assert.equal(openStream.ended, true);
  assert.equal(routes.size, 0);
  assert.deepEqual((await getAccountSnapshot(ctx)).user, { name: '已登录用户' });
  console.log('PASS: all 14 routes deny missing/forged/expired credentials and foreign origins; authorized actions, private-data minimization, SSE expiry and auth-service failures');
} finally {
  relay.dispose();
  mock.timers.reset();
}
