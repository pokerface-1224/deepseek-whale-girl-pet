/** Host-only plugin: manages the native Windows desktop pet. */
import { startDesktopPet } from './native-host.js';

export function apply(ctx) {
  // Cordis disposes this effect on plugin disable/reload.
  // Native startup failure must never prevent Harness from booting.
  try {
    ctx.effect(() => startDesktopPet({ getPanelUrl() {
      const server = ctx.get('webServer');
      const connection = ctx.get('connection');
      if (!server?.port || !connection?.authenticatedUrl) throw new Error('DSH web connection is not ready');
      return connection.authenticatedUrl(`http://127.0.0.1:${server.port}`);
    } }));
  } catch (error) {
    console.warn('[pet-whale] desktop lifecycle unavailable:', error);
  }
}
