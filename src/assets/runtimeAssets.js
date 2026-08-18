import items from "./constants";

let runtimeManifest = null;

/**
 * Applies extractor output to the objects that the existing EAM components
 * already import. This keeps the bundled assets as a safe offline fallback.
 */
export function mergeRuntimeAssets(manifest) {
    if (!manifest || typeof manifest !== "object" || !manifest.items) {
        return false;
    }

    Object.assign(items, manifest.items);
    runtimeManifest = manifest;
    return true;
}
export function getRuntimeAssetCacheKey() {
    return runtimeManifest?.buildHash || "bundled";
}

export function resolveRuntimeAssetSource(source) {
    if (
        runtimeManifest?.renderSheetDataUrl
        && (source === "renders.png" || source === "/renders.png")
    ) {
        return runtimeManifest.renderSheetDataUrl;
    }

    return source;
}
