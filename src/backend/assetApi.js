import { invoke } from "@tauri-apps/api/core";
import { mergeRuntimeAssets } from "../assets/runtimeAssets";

/**
 * Fetches the current game build through the Rust sidecar bridge. The app
 * requires either a fresh extraction or a previously extracted local
 * cache. It never silently falls back to the old bundled item assets.
 */
export async function refreshRuntimeAssets(force = false) {
    const manifest = await invoke("refresh_asset_cache", { force });
    if (!mergeRuntimeAssets(manifest)) {
        throw new Error("The live asset manifest was incomplete.");
    }
    return manifest;
}
