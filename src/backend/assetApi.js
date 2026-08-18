import { invoke } from "@tauri-apps/api/core";
import { mergeRuntimeAssets } from "../assets/runtimeAssets";

/**
 * Fetches the current game build through the Rust sidecar bridge. Failure is
 * deliberately non-fatal: bundled assets keep EAM usable offline or when the
 * optional sidecar has not been built yet.
 */
export async function refreshRuntimeAssets(force = false) {
    try {
        const manifest = await invoke("refresh_asset_cache", { force });
        return mergeRuntimeAssets(manifest);
    } catch (error) {
        console.warn("Runtime asset refresh unavailable; using bundled assets.", error);
        return false;
    }
}
