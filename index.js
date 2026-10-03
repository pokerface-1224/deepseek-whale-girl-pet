/** Host plugin: manages native Windows desktop pet and UI session synchronization. */
import { startDesktopPet } from './native-host.js';
import { setupSyncRelay } from './sync-host.js';

export function apply(ctx) {
  // Cordis disposes this effect on plugin disable/reload.
  // Native startup failure must never prevent Harness from booting.
  try {
    let syncRelay = null;

    const petController = startDesktopPet({
      getPanelUrl() {
        const server = ctx.get('webServer');
        const connection = ctx.get('connection');
        if (!server?.port || !connection?.authenticatedUrl) throw new Error('DSH web connection is not ready');
        return connection.authenticatedUrl(`http://127.0.0.1:${server.port}`);
      },
      getLastSelection: () => syncRelay?.getLastSelection()
    });

    syncRelay = setupSyncRelay(ctx, petController.manager);

    ctx.effect(() => () => {
      petController.stop();
      return syncRelay.dispose();
    });
  } catch (error) {
    console.warn('[pet-whale] desktop lifecycle unavailable:', error);
  }
}
