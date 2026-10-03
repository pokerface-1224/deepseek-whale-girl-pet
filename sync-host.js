// Host sync relay service for @local/dsh-pet-whale
// Coordinates active session across connected UI clients (Main Window, MiniPanel)

export function setupSyncRelay(ctx) {
  const server = ctx.get('webServer');
  if (!server || typeof server.register !== 'function') {
    return {
      getLastSelection: () => null,
      dispose: () => {}
    };
  }

  let lastSelection = null;
  const clients = new Set();

  // 1. GET /api/pet-whale/sync -> SSE stream
  const getDisposer = server.register({
    kind: 'exact',
    path: '/api/pet-whale/sync',
    method: 'GET',
    handler: async (req, res) => {
      res.writeHead(200, {
        'Content-Type': 'text/event-stream',
        'Cache-Control': 'no-cache, no-transform',
        'Connection': 'keep-alive'
      });

      // Send initial state if available
      if (lastSelection) {
        res.write(`data: ${JSON.stringify(lastSelection)}\n\n`);
      }

      clients.add(res);

      req.on('close', () => {
        clients.delete(res);
      });
    }
  });

  // 2. POST /api/pet-whale/sync -> Update selection & broadcast
  const postDisposer = server.register({
    kind: 'exact',
    path: '/api/pet-whale/sync',
    method: 'POST',
    handler: async (req, res) => {
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
          res.writeHead(200, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({ ok: true }));
        } catch (err) {
          res.writeHead(400, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({ error: 'Invalid JSON' }));
        }
      });
    }
  });

  return {
    getLastSelection: () => lastSelection,
    dispose: () => {
      if (typeof getDisposer === 'function') getDisposer();
      if (typeof postDisposer === 'function') postDisposer();
      for (const client of clients) {
        try {
          client.end();
        } catch (e) {}
      }
      clients.clear();
    }
  };
}
