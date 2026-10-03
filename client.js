// @local/dsh-pet-whale client module
// Synchronizes active session & provides UI entrypoints (sidebar button, /whale-girl command, shortcuts)

window.__ModuleLoader__.load({
  id: "@local/dsh-pet-whale",
  factory: (require) => {
    var module = { exports: {} };
    var exports = module.exports;

    let react, jsx;
    try {
      react = require("react");
      jsx = require("react/jsx-runtime");
    } catch (e) {}

    const inject = ["uiWorkspace"];

    // Whale-girl Icon inspired by character sheet & maid apron badge
    function WhaleIcon({ size = 18, color = "#2563eb" }) {
      if (!jsx) return null;
      return jsx.jsxs("svg", {
        width: size,
        height: size,
        viewBox: "0 0 24 24",
        fill: "none",
        xmlns: "http://www.w3.org/2000/svg",
        style: { verticalAlign: "middle" },
        children: [
          // Fountain splash from blowhole
          jsx.jsx("path", {
            d: "M11.5 2C11.5 3.2 10 4.2 8.5 4.2M12.5 2C12.5 3.2 14 4.2 15.5 4.2",
            stroke: "#38bdf8",
            strokeWidth: "1.6",
            strokeLinecap: "round"
          }),
          // Cute whale body (deep sea maid blue)
          jsx.jsx("path", {
            d: "M3.5 13C3.5 9.2 6.8 6 11 6C15.8 6 19.5 9.8 19.5 14.5C19.5 16.5 18 18 15.5 18C10.5 18 3.5 16.2 3.5 13Z",
            fill: color
          }),
          // White apron / belly curve
          jsx.jsx("path", {
            d: "M5.5 13.5C5.5 11.2 7.8 9.5 10.5 9.5C12.8 9.5 14.8 10.8 14.8 12.8C14.8 15 11.5 16.5 7.8 16.5C6.3 16.5 5.5 15.2 5.5 13.5Z",
            fill: "#f0f9ff"
          }),
          // Cute anime eye with highlight
          jsx.jsx("circle", { cx: "7.8", cy: "11.2", r: "1.2", fill: "#0f172a" }),
          jsx.jsx("circle", { cx: "8.2", cy: "10.9", r: "0.45", fill: "#ffffff" }),
          // Blush
          jsx.jsx("ellipse", { cx: "6.5", cy: "12.6", rx: "0.8", ry: "0.5", fill: "#f472b6", opacity: 0.85 }),
          // Fluked tail fins
          jsx.jsx("path", {
            d: "M19 13.2C20.8 11.8 22.2 10 22.8 9M19 14.5C20.8 16 22.2 17.5 22.8 18.5",
            stroke: color,
            strokeWidth: "2.2",
            strokeLinecap: "round"
          }),
          // Maid ribbon accent
          jsx.jsx("path", {
            d: "M15 5.2L16.8 4M15 4L16.8 5.2",
            stroke: "#38bdf8",
            strokeWidth: "1.4",
            strokeLinecap: "round"
          })
        ]
      });
    }

    // Sidebar footer action component
    function WhaleSidebarAction() {
      if (!react || !jsx) return null;
      const [clicking, setClicking] = react.useState(false);

      const triggerLaunch = () => {
        setClicking(true);
        fetch("/pet-whale/launch", { method: "POST", credentials: "include" })
          .catch(() => {})
          .finally(() => {
            setTimeout(() => setClicking(false), 300);
          });
      };

      return jsx.jsx("button", {
        type: "button",
        title: "召唤/唤醒鲸鱼娘桌宠 (Alt+W)",
        onClick: triggerLaunch,
        style: {
          display: "inline-flex",
          alignItems: "center",
          justifyContent: "center",
          width: "32px",
          height: "32px",
          borderRadius: "8px",
          border: "none",
          background: clicking ? "rgba(59, 130, 246, 0.2)" : "transparent",
          cursor: "pointer",
          padding: "4px",
          transition: "transform 0.15s ease, background-color 0.15s ease",
          transform: clicking ? "scale(0.9)" : "scale(1)"
        },
        children: jsx.jsx(WhaleIcon, { size: 20 })
      });
    }

    function apply(ctx) {
      const clientId = "client_" + Math.random().toString(36).slice(2, 10);
      let applyingRemote = false;
      let lastSessionId = null;

      // 1. Session Synchronization
      const uiWorkspace = ctx.get("uiWorkspace");
      if (uiWorkspace && uiWorkspace.selection) {
        const reportSelection = (selection) => {
          if (applyingRemote) return;
          const sessionId = selection?.sessionId;
          if (!sessionId || sessionId === lastSessionId) return;
          lastSessionId = sessionId;

          fetch("/pet-whale/sync", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            credentials: "include",
            body: JSON.stringify({
              clientId,
              sessionId,
              subagentAddress: selection.subagentAddress,
              timestamp: Date.now()
            })
          }).catch(() => {});
        };

        const unsub = uiWorkspace.selection.subscribe((selection) => {
          reportSelection(selection);
        });

        try {
          const init = uiWorkspace.selection.getSnapshot?.();
          if (init?.sessionId) {
            lastSessionId = init.sessionId;
            reportSelection(init);
          }
        } catch (e) {}

        // SSE for remote session updates
        let eventSource = null;
        try {
          eventSource = new EventSource("/pet-whale/sync");
          eventSource.onmessage = (event) => {
            if (!event?.data) return;
            try {
              const data = JSON.parse(event.data);
              if (data.clientId === clientId) return;
              if (!data.sessionId || data.sessionId === lastSessionId) return;

              lastSessionId = data.sessionId;
              applyingRemote = true;
              try {
                const target = data.subagentAddress || data.sessionId;
                if (typeof uiWorkspace.openSession === "function") {
                  uiWorkspace.openSession(target);
                }
              } finally {
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

      // 2. Register Slash Command: /whale-girl
      ctx.inject(["commandUi"], (scope) => {
        try {
          const commands = scope.get("commandUi");
          if (commands && typeof commands.register === "function") {
            scope.effect(() => commands.register({
              name: "whale-girl",
              label: () => "唤醒/召唤鲸鱼娘桌宠",
              icon: WhaleIcon,
              available: () => true,
              ui: {
                kind: "action",
                run: () => {
                  fetch("/pet-whale/launch", { method: "POST", credentials: "include" }).catch(() => {});
                }
              }
            }), "pet-whale: slash command /whale-girl");
          }
        } catch (e) {
          console.warn("[pet-whale] commandUi registration skipped:", e);
        }
      });

      // 3. Register Global Shortcut: Alt+W
      ctx.inject(["shortcuts"], (scope) => {
        try {
          const shortcuts = scope.get("shortcuts");
          if (shortcuts && typeof shortcuts.register === "function") {
            scope.effect(() => shortcuts.register({
              id: "whale-girl.launch",
              label: () => "召唤鲸鱼娘桌宠",
              aliases: ["summon whale girl", "wake whale pet"],
              defaults: {
                "desktop:windows": { code: "KeyW", modifiers: ["alt"] }
              },
              run: () => {
                fetch("/pet-whale/launch", { method: "POST", credentials: "include" }).catch(() => {});
              }
            }), "pet-whale: shortcut Alt+W");
          }
        } catch (e) {
          console.warn("[pet-whale] shortcuts registration skipped:", e);
        }
      });

      // 4. Register Permanent Sidebar Footer Action Icon
      ctx.inject(["slots"], (scope) => {
        try {
          if (scope.slots && typeof scope.slots.register === "function") {
            scope.slots.inject("sidebar.footer.action", () => scope.slots.register({
              name: "sidebar.footer.action",
              id: "whale-girl-pet"
            }, WhaleSidebarAction));
          }
        } catch (e) {
          console.warn("[pet-whale] sidebar slot registration skipped:", e);
        }
      });
    }

    exports.inject = inject;
    exports.apply = apply;
    return module.exports;
  }
});
