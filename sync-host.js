// Host sync relay service for @local/dsh-pet-whale
// Coordinates active session across connected UI clients & daemon process control

export function setupSyncRelay(ctx, petManager) {
  let relay = null;
  // webServer can appear after this plugin starts. Cordis also re-enters
  // this scope when the service is replaced, disposing the old routes first.
  const stop = ctx.inject(['webServer'], scope => {
    const current = createSyncRelay(scope, petManager);
    relay = current;
    scope.effect(() => () => {
      current.dispose();
      if (relay === current) relay = null;
    });
  });
  return {
    getLastSelection: () => relay?.getLastSelection() ?? null,
    dispose: () => stop.dispose()
  };
}

function createSyncRelay(ctx, petManager) {
  const server = ctx.get('webServer');
  if (!server || typeof server.register !== 'function') {
    throw new Error('[pet-whale] webServer.register unavailable');
  }

  let lastSelection = null;
  const clients = new Set();
  const disposers = [];

  const handleSseGet = (req, res) => {
    res.writeHead(200, {
      'Content-Type': 'text/event-stream',
      'Cache-Control': 'no-cache, no-transform',
      'Connection': 'keep-alive',
      'Access-Control-Allow-Origin': '*'
    });

    if (lastSelection) {
      res.write(`data: ${JSON.stringify(lastSelection)}\n\n`);
    }

    clients.add(res);
    res.flushHeaders?.();
    res.on('close', () => {
      clients.delete(res);
    });
  };

  const handleSyncPost = (req, res) => {
    let body = '';
    req.on('data', chunk => {
      body += chunk;
    });

    req.on('end', () => {
      try {
        const payload = JSON.parse(body);
        if (payload && payload.sessionId) {
          lastSelection = payload;
          const message = `data: ${JSON.stringify(payload)}\n\n`;
          for (const client of clients) {
            try {
              client.write(message);
            } catch (e) {
              clients.delete(client);
            }
          }
        }
        res.writeHead(200, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
        res.end(JSON.stringify({ ok: true }));
      } catch (err) {
        res.writeHead(400, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: 'Invalid JSON' }));
      }
    });
  };

  const handleSync = async (req, res) => {
    if (req.method === 'GET') {
      return handleSseGet(req, res);
    }
    if (req.method === 'POST') {
      return handleSyncPost(req, res);
    }
    res.writeHead(200, { 'Access-Control-Allow-Origin': '*' });
    res.end();
  };

  const handleLaunch = async (req, res) => {
    try {
      if (!petManager) {
        res.writeHead(503, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
        res.end(JSON.stringify({ error: 'Pet manager unavailable' }));
        return;
      }
      const result = await petManager.launchOrWake();
      res.writeHead(result.ok ? 200 : 503, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
      res.end(JSON.stringify(result));
    } catch (err) {
      res.writeHead(500, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: err.message }));
    }
  };

  const handleStatus = async (req, res) => {
    const running = petManager ? petManager.isAlive() : false;
    res.writeHead(200, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
    res.end(JSON.stringify({ running }));
  };

  const handleTask = async (req, res) => {
    let body = '';
    req.on('data', chunk => {
      body += chunk;
    });

    req.on('end', () => {
      try {
        const payload = JSON.parse(body || '{}');
        const isWorking = Boolean(payload?.isWorking);
        if (petManager) {
          petManager.sendPetMessage(isWorking ? "task:start\n" : "task:end\n");
        }
        res.writeHead(200, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
        res.end(JSON.stringify({ ok: true, isWorking }));
      } catch (err) {
        res.writeHead(400, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: err.message }));
      }
    });
  };

  // Register exactly one handler per path (webserver throws on duplicate paths)
  try {
    for (const [name, handler] of [['sync', handleSync], ['launch', handleLaunch], ['status', handleStatus], ['task', handleTask]]) {
      for (const path of [`/pet-whale/${name}`, `/api/pet-whale/${name}`]) {
        disposers.push(server.register({ kind: 'exact', path, handler }));
      }
    }
  } catch (error) {
    for (const dispose of disposers.reverse()) dispose();
    throw error;
  }

  return {
    getLastSelection: () => lastSelection,
    dispose: () => {
      for (const dispose of disposers) {
        try {
          if (typeof dispose === 'function') dispose();
        } catch (e) {}
      }
      for (const client of clients) {
        try {
          client.end();
        } catch (e) {}
      }
      clients.clear();
    }
  };
}
