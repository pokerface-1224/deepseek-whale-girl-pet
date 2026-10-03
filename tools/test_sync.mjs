import assert from 'node:assert/strict';
import { setupSyncRelay } from '../sync-host.js';
import { PetManager } from '../native-host.js';

// Test 1: Mock server registration and GET / POST sync behavior
const routes = new Map();
const mockServer = {
  register: (route) => {
    routes.set(`${route.method}:${route.path}`, route);
    return () => routes.delete(`${route.method}:${route.path}`);
  }
};

const mockCtx = {
  get: (name) => (name === 'webServer' ? mockServer : null)
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
assert.equal(routes.size, 4, 'Should register GET/POST sync and GET/POST launch/status routes');

// Test 2: Initially no selection
assert.equal(relay.getLastSelection(), null, 'Initially last selection should be null');

// Test 3: POST /api/pet-whale/sync
const postRoute = routes.get('POST:/api/pet-whale/sync');
assert.ok(postRoute, 'POST route exists');

let sseWritten = [];
const mockSseRes = {
  writeHead: () => {},
  write: (data) => sseWritten.push(data),
  on: () => {}
};

const getRoute = routes.get('GET:/api/pet-whale/sync');
assert.ok(getRoute, 'GET route exists');
await getRoute.handler({ on: () => {} }, mockSseRes);

// Simulate POST from a client
let reqEvents = {};
const mockReq = {
  on: (event, handler) => { reqEvents[event] = handler; }
};
let postStatus = 0;
const mockPostRes = {
  writeHead: (status) => { postStatus = status; },
  end: () => {}
};

postRoute.handler(mockReq, mockPostRes);
reqEvents['data'](JSON.stringify({ clientId: 'win1', sessionId: 'sess-123', timestamp: 100 }));
reqEvents['end']();

assert.equal(postStatus, 200, 'POST response should be 200');
assert.deepEqual(relay.getLastSelection(), { clientId: 'win1', sessionId: 'sess-123', timestamp: 100 });
assert.ok(sseWritten.some(msg => msg.includes('sess-123')), 'SSE listener should receive session broadcast');

// Test 4: Launch route (re-summoning the pet)
const launchRoute = routes.get('POST:/api/pet-whale/launch');
assert.ok(launchRoute, 'POST /api/pet-whale/launch route exists');
let launchResBody = '';
await launchRoute.handler({}, {
  writeHead: () => {},
  end: (data) => { launchResBody = data; }
});
assert.equal(launchCalls, 1, 'Should call launchOrWake once');
assert.match(launchResBody, /"status":"launched"/);

// Second launch when already alive should wake
await launchRoute.handler({}, {
  writeHead: () => {},
  end: (data) => { launchResBody = data; }
});
assert.equal(launchCalls, 2, 'Should call launchOrWake twice');
assert.match(launchResBody, /"status":"woken"/);

// Test 5: Status route
const statusRoute = routes.get('GET:/api/pet-whale/status');
assert.ok(statusRoute, 'GET /api/pet-whale/status route exists');
let statusResBody = '';
await statusRoute.handler({}, {
  writeHead: () => {},
  end: (data) => { statusResBody = data; }
});
assert.match(statusResBody, /"running":true/);

// Test 6: PetManager class structure test
const pm = new PetManager({ platform: 'linux' });
assert.equal(pm.isAlive(), false);
const res = pm.launchOrWake();
assert.equal(res.ok, false, 'Non-win32 should not launch');

relay.dispose();
assert.equal(routes.size, 0, 'Disposal should remove all 4 routes');
console.log('PASS: Host sync relay & daemon control (SSE + POST + launch + status + dispose)');
