import { useEffect, useState } from "react";
import { ColorContextProvider } from "eam-commons-js";
import { onStartUp, setApiHwidHash } from "./utils/startUpUtils";
import useHWID from "./hooks/useHWID";
import { heartBeat } from "./backend/eamApi";
import MainProviders from "./MainProviders";
import { invoke } from "@tauri-apps/api/core";
import { refreshRuntimeAssets } from "./backend/assetApi";

function App() {
    const [hasTriggeredStartup, setHasTriggeredStartup] = useState(false);
    const [assetsReady, setAssetsReady] = useState(false);
    const [assetError, setAssetError] = useState(null);
    const [assetRetry, setAssetRetry] = useState(0);
    const [repairingAssets, setRepairingAssets] = useState(false);
    const { hwid } = useHWID();

    useEffect(() => {
        onStartUp();
        let disposed = false;
        let heartBeatInterval = null;
        const startHeartbeat = () => {
            if (disposed) return;
            heartBeatInterval = setInterval(() => {
                heartBeat();
            }, 59_000);
        };

        invoke('get_user_data_by_key', { key: 'analytics' })
            .then(response => {
                if (response) {
                    try {
                        if (response.dataValue) {
                            const analytics = JSON.parse(response.dataValue);
                            if (analytics && analytics.optOut) {
                                console.log("You have opt-out of analytics. 😭");
                                return null;
                            }
                        }
                    } catch (error) {
                        console.error(error);
                    }
                }

                startHeartbeat();
            })
            .catch(() => {
                startHeartbeat();
            });

        return () => {
            disposed = true;
            if (heartBeatInterval) {
                clearInterval(heartBeatInterval);
            }
        };
    }, []);

    useEffect(() => {
        let cancelled = false;
        setAssetsReady(false);
        setAssetError(null);

        refreshRuntimeAssets()
            .then(() => {
                if (!cancelled) setAssetsReady(true);
            })
            .catch((error) => {
                if (!cancelled) {
                    setAssetsReady(true);
                    setAssetError(error?.message || "Unable to load live game assets.");
                }
            });

        return () => {
            cancelled = true;
        };
    }, [assetRetry]);

    useEffect(() => {
        if (hasTriggeredStartup || !hwid) {
            return;
        }

        setApiHwidHash(hwid);
        setHasTriggeredStartup(true);
    }, [hwid]);

    const repairRealmAssets = async () => {
        setRepairingAssets(true);
        setAssetsReady(false);
        setAssetError(null);

        try {
            const updateNeeded = await invoke('check_for_game_update', { force: true });
            if (updateNeeded) {
                const updateSucceeded = await invoke('perform_game_update');
                if (!updateSucceeded) {
                    throw new Error('Realm Updater did not complete successfully.');
                }
            }
            setAssetRetry((value) => value + 1);
        } catch (error) {
            setAssetsReady(true);
            setAssetError(error?.message || "Unable to update Realm game assets.");
        } finally {
            setRepairingAssets(false);
        }
    };

    if (!assetsReady) {
        return (
            <div style={{ display: "grid", minHeight: "100vh", placeItems: "center", color: "#fff" }}>
                {repairingAssets
                    ? "Realm Updater is repairing the installed client…"
                    : "Loading item assets from the installed Realm client…"}
            </div>
        );
    }

    if (assetError) {
        return (
            <div style={{ display: "grid", gap: "1rem", minHeight: "100vh", placeItems: "center", color: "#fff", textAlign: "center" }}>
                <div>
                    <div>Live game assets are required to run EAM.</div>
                    <div style={{ fontSize: "0.85rem", marginTop: "0.5rem", opacity: 0.75 }}>{assetError}</div>
                </div>
                <div style={{ display: "flex", gap: "0.75rem" }}>
                    <button type="button" onClick={() => setAssetRetry((value) => value + 1)}>
                        Retry extraction
                    </button>
                    <button type="button" disabled={repairingAssets} onClick={repairRealmAssets}>
                        Run Realm Updater
                    </button>
                </div>
            </div>
        );
    }

    return (
        <ColorContextProvider>
            <MainProviders />
        </ColorContextProvider>
    );
}

export default App;
