// Host sync relay service for @local/dsh-pet-whale
// Coordinates active session across connected UI clients & daemon process control

export function setupSyncRelay(ctx, petManager) {
  const server = ctx.get('webServer');
  if (!server || typeof server.register !== 'function') {
    return {
      getLastSelection: () => null,
      dispose: () => {}
    };
  }

  let lastSelection = null;
  const clients = new Set();
  const disposers = [];

  const handleSseGet = async (req, res) => {
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
    req.on('close', () => {
      clients.delete(res);
    });
  };

  const handleSyncPost = async (req, res) => {
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

  const handleLaunchPost = async (req, res) => {
    try {
      if (!petManager) {
        res.writeHead(503, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
        res.end(JSON.stringify({ error: 'Pet manager unavailable' }));
        return;
      }
      const result = petManager.launchOrWake();
      res.writeHead(200, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
      res.end(JSON.stringify(result));
    } catch (err) {
      res.writeHead(500, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: err.message }));
    }
  };

  const handleStatusGet = async (req, res) => {
    const running = petManager ? petManager.isAlive() : false;
    res.writeHead(200, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
    res.end(JSON.stringify({ running }));
  };

  // Register clean non-API routes (bypasses connection /api 401 interceptor)
  for (const path of ['/pet-whale/sync', '/api/pet-whale/sync']) {
    disposers.push(server.register({ kind: 'exact', path, method: 'GET', handler: handleSseGet }));
    disposers.push(server.register({ kind: 'exact', path, method: 'POST', handler: handleSyncPost }));
  }
  for (const path of ['/pet-whale/launch', '/api/pet-whale/launch']) {
    disposers.push(server.register({ kind: 'exact', path, method: 'POST', handler: handleLaunchPost }));
  }
  for (const path of ['/pet-whale/status', '/api/pet-whale/status']) {
    disposers.push(server.register({ kind: 'exact', path, method: 'GET', handler: handleStatusGet }));
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
