import assert from 'node:assert/strict';
import { setupSyncRelay, getAccountSnapshot } from '../sync-host.js';
import { PetManager } from '../native-host.js';

// Test 1: Mock server registration
const routes = new Map();
const mockServer = {
  register: (route) => {
    if (routes.has(route.path)) throw new Error(`duplicate route ${route.path}`);
    routes.set(route.path, route);
    return () => routes.delete(route.path);
  }
};

let activate;
let cleanup;
let stopped = false;
const mockCtx = {
  inject(names, callback) {
    assert.deepEqual(names, ['webServer', 'connection']);
    activate = () => {
      assert.equal(stopped, false);
      callback({
        get: name => name === 'webServer' ? mockServer : name === 'connection' ? { admit: () => ({ peer: {} }) } : null,
        effect: setup => { cleanup = setup(); }
      });
    };
    return { dispose() { stopped = true; cleanup?.(); } };
  }
};

// Mock PetManager
let launchCalls = 0;
let isAliveVal = false;
const mockPetManager = {
  isAlive: () => isAliveVal,
  launchOrWake: () => {
    launchCalls++;
    const wasAlive = isAliveVal;
    isAliveVal = true;
    return { ok: true, status: wasAlive ? 'woken' : 'launched' };
  }
};

const relay = setupSyncRelay(mockCtx, mockPetManager);
assert.equal(routes.size, 0, 'Wait for webServer instead of silently abandoning registration');
assert.equal(relay.getLastSelection(), null);
activate();
assert.ok(routes.has('/pet-whale/sync'), '/pet-whale/sync route exists');
assert.ok(routes.has('/pet-whale/launch'), '/pet-whale/launch route exists');
assert.ok(routes.has('/pet-whale/status'), '/pet-whale/status route exists');

// Test 2: Initially no selection
assert.equal(relay.getLastSelection(), null, 'Initially last selection should be null');

// Test 3: POST /pet-whale/sync
const syncRoute = routes.get('/pet-whale/sync');
assert.ok(syncRoute, 'sync route exists');

let sseWritten = [];
const mockSseRes = {
  writeHead: () => {},
  write: (data) => sseWritten.push(data),
  on: () => {}
};

await syncRoute.handler({ method: 'GET', on: () => {} }, mockSseRes);

// Simulate POST from a client
let reqEvents = {};
const mockReq = {
  method: 'POST',
  on: (event, handler) => { reqEvents[event] = handler; }
};
let postStatus = 0;
const mockPostRes = {
  writeHead: (status) => { postStatus = status; },
  end: () => {}
};

syncRoute.handler(mockReq, mockPostRes);
reqEvents['data'](JSON.stringify({ clientId: 'win1', sessionId: 'sess-123', timestamp: 100 }));
reqEvents['end']();

assert.equal(postStatus, 200, 'POST response should be 200');
assert.deepEqual(relay.getLastSelection(), { clientId: 'win1', sessionId: 'sess-123', timestamp: 100 });
assert.ok(sseWritten.some(msg => msg.includes('sess-123')), 'SSE listener should receive session broadcast');

// Test 4: Launch route (re-summoning the pet)
const launchRoute = routes.get('/pet-whale/launch');
assert.ok(launchRoute, 'POST /pet-whale/launch route exists');
let launchResBody = '';
await launchRoute.handler({ method: 'POST' }, {
  writeHead: () => {},
  end: (data) => { launchResBody = data; }
});
assert.equal(launchCalls, 1, 'Should call launchOrWake once');
assert.match(launchResBody, /"status":"launched"/);

// Second launch when already alive should wake
await launchRoute.handler({ method: 'POST' }, {
  writeHead: () => {},
  end: (data) => { launchResBody = data; }
});
assert.equal(launchCalls, 2, 'Should call launchOrWake twice');
assert.match(launchResBody, /"status":"woken"/);

// HTTP success must wait for the native acknowledgement; failures use 503.
const originalLaunch = mockPetManager.launchOrWake;
let acknowledge;
mockPetManager.launchOrWake = () => new Promise(resolve => { acknowledge = resolve; });
let ackStatus;
const waitingLaunch = launchRoute.handler({ method: 'POST' }, {
  writeHead: status => { ackStatus = status; },
  end: data => { launchResBody = data; }
});
assert.equal(ackStatus, undefined);
acknowledge({ ok: false, error: 'No visible window acknowledgement' });
await waitingLaunch;
assert.equal(ackStatus, 503);
assert.match(launchResBody, /No visible window acknowledgement/);
mockPetManager.launchOrWake = originalLaunch;

// Test 5: Status route
const statusRoute = routes.get('/pet-whale/status');
assert.ok(statusRoute, 'GET /pet-whale/status route exists');
let statusResBody = '';
await statusRoute.handler({ method: 'GET' }, {
  writeHead: () => {},
  end: (data) => { statusResBody = data; }
});
assert.match(statusResBody, /"running":true/);

// Test 6: Account route
const accountRoute = routes.get('/pet-whale/account');
assert.ok(accountRoute, 'GET /pet-whale/account route exists');
let accountResBody = '';
await accountRoute.handler({ method: 'GET' }, {
  writeHead: () => {},
  end: (data) => { accountResBody = data; }
});
assert.match(accountResBody, /"authenticated":false/);

// Test 6b: Account snapshot with async authenticated service
const authedCtx = {
  // Cordis throws on undeclared service properties; the snapshot must not touch them.
  get locale() { throw new Error('cannot get property "locale" without inject'); },
  get(name) {
    if (name === 'deepseekAccount') {
      return {
        async getState() {
          return { status: 'credential-stored' };
        },
        async getProfile() {
          return { status: 'ready', value: { id: 'u123', name: '测试小鲸鱼', contact: '13800000000' } };
        },
        async getBalance() {
          return {
            status: 'ready',
            value: [{ currency: 'CNY', balance: '88.50' }],
            bonusWallets: [{ currency: 'CNY', balance: '12.00' }]
          };
        }
      };
    }
    return mockCtx.get(name);
  }
};
const authedSnapshot = await getAccountSnapshot(authedCtx);
assert.equal(authedSnapshot.authenticated, true);
assert.equal(authedSnapshot.user.name, '测试小鲸鱼');
assert.equal(Object.hasOwn(authedSnapshot.user, 'contact'), false, 'Contact is not needed by the pet');
assert.equal(authedSnapshot.balance.normal, '88.50');
assert.equal(authedSnapshot.balance.bonus, '12.00');
assert.equal(authedSnapshot.balance.total, '100.50');

// Real Harness profiles can have no identity.name and only a phone/email contact.
for (const [profile, expected] of [
  [{ name: null, contact: '13800000000' }, '138****0000'],
  [{ name: '  ', contact: ' +86 138-0000-0000 ' }, '+86 138****0000'],
  [{ contact: 'audit@example.invalid' }, 'a***@example.invalid'],
  [{ contact: '鲸鱼@example.invalid' }, '鲸***@example.invalid'],
  [{ contact: '138****0000' }, '138****0000'],
  [{ contact: 'a***@example.invalid' }, 'a***@example.invalid'],
  [{ name: '13800000000' }, '138****0000'],
  [{ name: 'audit@example.invalid' }, 'a***@example.invalid'],
  [{ name: '小鲸鱼🐳', contact: '13800000000' }, '小鲸鱼🐳'],
  [{ contact: 'unknown-contact' }, 'u***t'],
  [{ contact: 'ab' }, '***'],
  [{ name: {}, contact: null }, '已登录用户'],
  [{}, '已登录用户']
]) {
  for (const wrapped of [false, true]) {
    const snapshot = await getAccountSnapshot({ get: () => ({
      getState: async () => ({ status: 'credential-stored' }),
      getProfile: async () => wrapped ? { status: 'ready', value: profile } : profile
    }) });
    assert.deepEqual(snapshot.user, { name: expected });
    if (profile.contact && profile.contact.trim() !== expected) {
      assert.equal(JSON.stringify(snapshot).includes(profile.contact.trim()), false, 'Raw contact must not leave host');
    }
  }
}
const profileFailure = await getAccountSnapshot({ get: () => ({
  getState: async () => ({ status: 'credential-stored' }),
  getProfile: async () => { throw new Error('Fixture profile unavailable'); }
}) });
assert.deepEqual(profileFailure.user, { name: '已登录用户' });

// Test 7: PetManager class structure test
const pm = new PetManager({ platform: 'linux' });
assert.equal(pm.isAlive(), false);
const res = await pm.launchOrWake();
assert.equal(res.ok, false, 'Non-win32 should not launch');

relay.dispose();
assert.equal(routes.size, 0, 'Disposal should remove all routes');

// Service replacement must remove old routes and register on the new scope.
stopped = false;
const restarting = setupSyncRelay(mockCtx, mockPetManager);
activate();
cleanup();
assert.equal(routes.size, 0);
activate();
assert.equal(routes.size, 14);
restarting.dispose();

// Registration errors must surface, and partial registrations must be undone.
stopped = false;
const occupied = { path: '/pet-whale/launch' };
routes.set(occupied.path, occupied);
setupSyncRelay(mockCtx, mockPetManager);
assert.throws(activate, /duplicate route/);
assert.deepEqual([...routes.values()], [occupied]);
console.log('PASS: Host sync relay & daemon control (SSE + POST + launch + status + dispose)');
