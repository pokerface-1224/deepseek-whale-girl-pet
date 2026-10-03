// Pass the installed Harness Cordis module path to exercise its real lifecycle.
import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';
import { resolve } from 'node:path';
import { createServer } from 'node:http';
import { once } from 'node:events';
import { setTimeout as delay } from 'node:timers/promises';
import { setupSyncRelay } from '../sync-host.js';

const { Context } = await import(pathToFileURL(resolve(process.argv[2])));
const ctx = new Context();
const routes = new Map();
let launches = 0;
const relay = setupSyncRelay(ctx, {
  launchOrWake: () => ({ ok: true, status: ++launches === 1 ? 'launched' : 'woken' }),
  isAlive: () => launches > 0
});
const webServer = {
  register(route) {
    assert.equal(routes.has(route.path), false);
    routes.set(route.path, route);
    return () => routes.delete(route.path);
  }
};
const serve = () => ctx.plugin(scope => scope.provide('webServer', webServer));
async function waitForRoutes(count) {
  for (let i = 0; i < 100 && routes.size !== count; i++) await delay(10);
  assert.equal(routes.size, count);
}
const http = createServer((req, res) => {
  const route = routes.get(new URL(req.url, 'http://localhost').pathname);
  if (route) route.handler(req, res);
  else { res.writeHead(req.method === 'POST' ? 405 : 404); res.end(); }
});
http.listen(0, '127.0.0.1');
await once(http, 'listening');
const url = `http://127.0.0.1:${http.address().port}/pet-whale`;
let provider;
let reader;
try {
  assert.equal((await fetch(`${url}/launch`, { method: 'POST' })).status, 405);
  provider = serve();
  await provider;
  await waitForRoutes(6);
  for (const status of ['launched', 'woken']) {
    const response = await fetch(`${url}/launch`, { method: 'POST' });
    assert.equal(response.status, 200);
    assert.equal((await response.json()).status, status);
  }
  // No selection yet: headers must arrive before any SSE message exists.
  const stream = await fetch(`${url}/sync`, { signal: AbortSignal.timeout(5000) });
  assert.equal(stream.status, 200);
  reader = stream.body.getReader();
  const selection = { clientId: 'integration', sessionId: 'session-one' };
  const response = await fetch(`${url}/sync`, { method: 'POST', body: JSON.stringify(selection) });
  assert.equal(response.status, 200);
  assert.match(new TextDecoder().decode((await reader.read()).value), /session-one/);
  assert.deepEqual(relay.getLastSelection(), selection);
  await provider.dispose();
  await waitForRoutes(0);
  assert.equal((await reader.read()).done, true, 'Service removal closes SSE');
  provider = serve();
  await provider;
  await waitForRoutes(6);
  assert.equal((await fetch(`${url}/launch`, { method: 'POST' })).status, 200);
  await relay.dispose();
  await waitForRoutes(0);
  console.log('PASS: real Cordis delayed service, HTTP launch/wake, SSE, service replacement and disposal');
} finally {
  await reader?.cancel();
  await relay.dispose();
  await provider?.dispose();
  http.closeAllConnections();
  await new Promise(resolve => http.close(resolve));
}
