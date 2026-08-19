import { invoke } from "@tauri-apps/api/core";
import { mergeRuntimeAssets } from "../assets/runtimeAssets";

/**
 * Loads item assets extracted from the installed Realm client through the
 * Rust sidecar bridge. The sidecar never downloads game files itself.
 */
export async function refreshRuntimeAssets(force = false) {
    const manifest = await invoke("refresh_asset_cache", { force });
    if (!mergeRuntimeAssets(manifest)) {
        throw new Error("The live asset manifest was incomplete.");
    }
    return manifest;
}
