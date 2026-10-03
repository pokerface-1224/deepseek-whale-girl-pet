/** Host-only plugin: manages the native Windows desktop pet. */
import { startDesktopPet } from './native-host.js';

export function apply(ctx) {
  // Cordis disposes this effect on plugin disable/reload.
  // Native startup failure must never prevent Harness from booting.
  try {
    ctx.effect(() => startDesktopPet());
  } catch (error) {
    console.warn('[pet-whale] desktop lifecycle unavailable:', error);
  }
}
