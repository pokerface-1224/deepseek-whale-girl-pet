import assert from 'node:assert/strict';
import { setupSyncRelay } from '../sync-host.js';

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

const relay = setupSyncRelay(mockCtx);
assert.equal(routes.size, 2, 'Should register GET and POST routes');

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

relay.dispose();
assert.equal(routes.size, 0, 'Disposal should remove routes');
console.log('PASS: Host sync relay (SSE + POST broadcast + dispose)');
