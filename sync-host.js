// Host sync relay service for @local/dsh-pet-whale
// Coordinates active session across connected UI clients & daemon process control,
// and integrates DeepSeek account authentication & balance inquiries.

export function setupSyncRelay(ctx, petManager) {
  let relay = null;

  // Broadcast helper to notify pet and clients of account updates
  const broadcastAccount = async () => {
    const snapshot = await getAccountSnapshot(ctx);
    if (petManager?.sendPetMessage) {
      petManager.sendPetMessage(`account:info:${JSON.stringify(snapshot)}\n`);
    }
    relay?.broadcastSse({ type: 'account', data: snapshot });
    return snapshot;
  };

  // Listen to credentials or sign-out changes
  try {
    ctx.on('credentials/record-updated', () => {
      broadcastAccount().catch(() => {});
    });
    ctx.on('deepseek-account/signed-out', () => {
      broadcastAccount().catch(() => {});
    });
  } catch (e) {}

  // webServer can appear after this plugin starts. Cordis also re-enters
  // this scope when the service is replaced, disposing the old routes first.
  const stop = ctx.inject(['webServer'], scope => {
    const current = createSyncRelay(scope, petManager, broadcastAccount);
    relay = current;
    scope.effect(() => () => {
      current.dispose();
      if (relay === current) relay = null;
    });
  });

  return {
    getLastSelection: () => relay?.getLastSelection() ?? null,
    getAccountSnapshot: () => getAccountSnapshot(ctx),
    handleLogin: () => triggerLogin(ctx),
    handleLogout: () => triggerLogout(ctx),
    broadcastAccount,
    dispose: () => stop.dispose()
  };
}

export async function getAccountSnapshot(ctx) {
  try {
    const account = ctx.get('deepseekAccount');
    if (!account || typeof account.getState !== 'function') {
      return { authenticated: false, user: null, balance: null };
    }
    const state = account.getState();
    const isAuthed = state && (state.status === 'credential-stored' || state.status === 'authenticated');
    if (!isAuthed) {
      return { authenticated: false, user: null, balance: null };
    }

    const clientMetadata = {
      version: '0.2.0-rc.2',
      locale: ctx.locale?.getSnapshot?.()?.active || 'zh',
      timezoneOffsetSeconds: -(new Date()).getTimezoneOffset() * 60
    };

    const [profileRes, balanceRes] = await Promise.allSettled([
      account.getProfile ? account.getProfile(clientMetadata) : Promise.resolve(null),
      account.getBalance ? account.getBalance(clientMetadata) : Promise.resolve(null)
    ]);

    let user = { name: '已登录用户', contact: '' };
    if (profileRes.status === 'fulfilled' && profileRes.value) {
      const p = profileRes.value.value || profileRes.value;
      if (p) {
        user = {
          name: p.name || p.contact || '已登录用户',
          contact: p.contact || ''
        };
      }
    }

    let balance = { currency: 'CNY', normal: '0.00', bonus: '0.00', total: '0.00' };
    if (balanceRes.status === 'fulfilled' && balanceRes.value) {
      const b = balanceRes.value;
      const normalList = Array.isArray(b.value) ? b.value : (Array.isArray(b) ? b : []);
      const bonusList = Array.isArray(b.bonusWallets) ? b.bonusWallets : [];
      const normalItem = normalList[0] || {};
      const bonusItem = bonusList[0] || {};
      const normalVal = parseFloat(normalItem.balance || '0') || 0;
      const bonusVal = parseFloat(bonusItem.balance || '0') || 0;
      const currency = normalItem.currency || bonusItem.currency || 'CNY';
      balance = {
        currency,
        normal: normalVal.toFixed(2),
        bonus: bonusVal.toFixed(2),
        total: (normalVal + bonusVal).toFixed(2)
      };
    }

    return {
      authenticated: true,
      user,
      balance
    };
  } catch (err) {
    return { authenticated: false, user: null, balance: null, error: err.message };
  }
}

export async function triggerLogin(ctx) {
  try {
    const account = ctx.get('deepseekAccount');
    if (!account) throw new Error('Account service unavailable');
    const server = ctx.get('webServer');
    const callbackOrigin = server?.port ? `http://127.0.0.1:${server.port}` : 'http://127.0.0.1';
    const clientMetadata = {
      version: '0.2.0-rc.2',
      locale: ctx.locale?.getSnapshot?.()?.active || 'zh',
      timezoneOffsetSeconds: -(new Date()).getTimezoneOffset() * 60
    };

    if (typeof account.watch === 'function') {
      const stream = account.watch(AbortSignal.timeout(10000));
      (async () => {
        try {
          for await (const frame of stream) {
            const authUrl = frame?.value?.attempt?.authorizeUrl || frame?.attempt?.authorizeUrl;
            if (authUrl) {
              const { exec } = await import('node:child_process');
              exec(`start "" "${authUrl}"`);
              break;
            }
          }
        } catch {}
      })();
    }

    if (typeof account.startSignIn === 'function') {
      await account.startSignIn(clientMetadata, callbackOrigin, 'desktop');
      return { ok: true };
    }
    throw new Error('startSignIn not supported');
  } catch (err) {
    return { ok: false, error: err.message };
  }
}

export async function triggerLogout(ctx) {
  try {
    const account = ctx.get('deepseekAccount');
    if (!account) throw new Error('Account service unavailable');
    const clientMetadata = {
      version: '0.2.0-rc.2',
      locale: ctx.locale?.getSnapshot?.()?.active || 'zh',
      timezoneOffsetSeconds: -(new Date()).getTimezoneOffset() * 60
    };
    if (typeof account.signOut === 'function') {
      await account.signOut(clientMetadata);
      return { ok: true };
    }
    throw new Error('signOut not supported');
  } catch (err) {
    return { ok: false, error: err.message };
  }
}

function createSyncRelay(ctx, petManager, broadcastAccount) {
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

  const handleAccount = async (req, res) => {
    try {
      if (req.method === 'GET') {
        const snapshot = await getAccountSnapshot(ctx);
        res.writeHead(200, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
        res.end(JSON.stringify({ ok: true, ...snapshot }));
        return;
      }
      res.writeHead(405, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
      res.end(JSON.stringify({ error: 'Method not allowed' }));
    } catch (err) {
      res.writeHead(500, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: err.message }));
    }
  };

  const handleAccountLogin = async (req, res) => {
    try {
      if (req.method === 'POST') {
        const result = await triggerLogin(ctx);
        res.writeHead(result.ok ? 200 : 500, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
        res.end(JSON.stringify(result));
        return;
      }
      res.writeHead(405, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
      res.end(JSON.stringify({ error: 'Method not allowed' }));
    } catch (err) {
      res.writeHead(500, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: err.message }));
    }
  };

  const handleAccountLogout = async (req, res) => {
    try {
      if (req.method === 'POST') {
        const result = await triggerLogout(ctx);
        res.writeHead(result.ok ? 200 : 500, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
        res.end(JSON.stringify(result));
        return;
      }
      res.writeHead(405, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
      res.end(JSON.stringify({ error: 'Method not allowed' }));
    } catch (err) {
      res.writeHead(500, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: err.message }));
    }
  };

  // Register exactly one handler per path (webserver throws on duplicate paths)
  try {
    for (const [name, handler] of [
      ['sync', handleSync],
      ['launch', handleLaunch],
      ['status', handleStatus],
      ['task', handleTask],
      ['account', handleAccount],
      ['account/login', handleAccountLogin],
      ['account/logout', handleAccountLogout]
    ]) {
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
    broadcastSse: (data) => {
      const message = `data: ${JSON.stringify(data)}\n\n`;
      for (const client of clients) {
        try { client.write(message); } catch { clients.delete(client); }
      }
    },
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
