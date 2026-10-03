// @local/dsh-pet-whale client module
// Synchronizes current active session between Deepseek Harness main window and MiniPanel.

window.__ModuleLoader__.load({
  id: "@local/dsh-pet-whale",
  factory: (require) => {
    var module = { exports: {} };
    var exports = module.exports;

    const inject = ["uiWorkspace"];

    function apply(ctx) {
      const clientId = "client_" + Math.random().toString(36).slice(2, 10);
      let applyingRemote = false;
      let lastSessionId = null;

      const uiWorkspace = ctx.get("uiWorkspace");
      if (!uiWorkspace || !uiWorkspace.selection) {
        console.warn("[pet-whale-sync] uiWorkspace not available, sync disabled");
        return;
      }

      // 1. Report local session change to Host
      const reportSelection = (selection) => {
        if (applyingRemote) return;
        const sessionId = selection?.sessionId;
        if (!sessionId || sessionId === lastSessionId) return;
        lastSessionId = sessionId;

        fetch("/api/pet-whale/sync", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            clientId,
            sessionId,
            subagentAddress: selection.subagentAddress,
            timestamp: Date.now()
          })
        }).catch((err) => {
          // Silent ignore network errors during boot or teardown
        });
      };

      // Subscribe to local selection store
      const unsub = uiWorkspace.selection.subscribe((selection) => {
        reportSelection(selection);
      });

      // Report current initial selection
      try {
        const init = uiWorkspace.selection.getSnapshot?.();
        if (init?.sessionId) {
          lastSessionId = init.sessionId;
          reportSelection(init);
        }
      } catch (e) {}

      // 2. Listen to SSE events from Host to apply remote selection
      let eventSource = null;
      try {
        eventSource = new EventSource("/api/pet-whale/sync");
        eventSource.onmessage = (event) => {
          if (!event?.data) return;
          try {
            const data = JSON.parse(event.data);
            if (data.clientId === clientId) return; // Ignore self updates
            if (!data.sessionId || data.sessionId === lastSessionId) return;

            lastSessionId = data.sessionId;
            applyingRemote = true;
            try {
              const target = data.subagentAddress || data.sessionId;
              if (typeof uiWorkspace.openSession === "function") {
                uiWorkspace.openSession(target);
              }
            } finally {
              // Reset flag on next tick to allow store settlement
              setTimeout(() => {
                applyingRemote = false;
              }, 50);
            }
          } catch (err) {
            console.error("[pet-whale-sync] Failed to parse sync event:", err);
          }
        };
      } catch (err) {
        console.warn("[pet-whale-sync] Failed to connect SSE sync:", err);
      }

      // 3. Listen to window postMessage (native WebView2 fallback injection)
      const onWindowMessage = (event) => {
        try {
          const data = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
          if (data?.type === "pet-whale-sync" && data.sessionId && data.sessionId !== lastSessionId) {
            lastSessionId = data.sessionId;
            applyingRemote = true;
            try {
              if (typeof uiWorkspace.openSession === "function") {
                uiWorkspace.openSession(data.sessionId);
              }
            } finally {
              setTimeout(() => {
                applyingRemote = false;
              }, 50);
            }
          }
        } catch (e) {}
      };
      window.addEventListener("message", onWindowMessage);

      ctx.effect(() => () => {
        if (unsub) unsub();
        if (eventSource) eventSource.close();
        window.removeEventListener("message", onWindowMessage);
      });
    }

    exports.inject = inject;
    exports.apply = apply;
    return module.exports;
  }
});
