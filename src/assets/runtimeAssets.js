export const items = {};

let runtimeManifest = null;

/**
 * Applies extractor output to the objects that the existing EAM components
 * already import. There is intentionally no bundled item fallback.
 */
export function mergeRuntimeAssets(manifest) {
    if (
        !manifest
        || typeof manifest !== "object"
        || !manifest.items
        || !manifest.renderSheetDataUrl
        || !manifest.buildHash
    ) {
        return false;
    }

    Object.assign(items, manifest.items);
    runtimeManifest = manifest;
    return true;
}
export function getRuntimeAssetCacheKey() {
    return runtimeManifest?.buildHash || "unavailable";
}

export function resolveRuntimeAssetSource(source) {
    if (
        source === "renders.png"
        || source === "/renders.png"
    ) {
        if (!runtimeManifest?.renderSheetDataUrl) {
            throw new Error("Live item render assets have not been loaded.");
        }
        return runtimeManifest.renderSheetDataUrl;
    }

    return source;
}
