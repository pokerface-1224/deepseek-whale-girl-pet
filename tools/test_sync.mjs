import assert from 'node:assert/strict';
import { setupSyncRelay } from '../sync-host.js';
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
    assert.deepEqual(names, ['webServer']);
    activate = () => {
      assert.equal(stopped, false);
      callback({
        get: name => name === 'webServer' ? mockServer : null,
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

// Test 6: PetManager class structure test
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
assert.equal(routes.size, 6);
restarting.dispose();

// Registration errors must surface, and partial registrations must be undone.
stopped = false;
const occupied = { path: '/pet-whale/launch' };
routes.set(occupied.path, occupied);
setupSyncRelay(mockCtx, mockPetManager);
assert.throws(activate, /duplicate route/);
assert.deepEqual([...routes.values()], [occupied]);
console.log('PASS: Host sync relay & daemon control (SSE + POST + launch + status + dispose)');
