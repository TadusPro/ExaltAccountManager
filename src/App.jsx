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
                    console.error("Failed to load live game data:", error);
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

    if (!assetsReady) {
        return (
            <div style={{ display: "grid", minHeight: "100vh", placeItems: "center", color: "#fff", backgroundColor: "#121212" }}>
                Loading live game data…
            </div>
        );
    }

    if (assetError) {
        return (
            <div style={{ display: "grid", gap: "1rem", minHeight: "100vh", placeItems: "center", color: "#fff", backgroundColor: "#121212", textAlign: "center" }}>
                <div>
                    <div>Live game data is required to run EAM.</div>
                    <div style={{ fontSize: "0.85rem", marginTop: "0.5rem", opacity: 0.75 }}>{assetError}</div>
                </div>
                <button type="button" onClick={() => setAssetRetry((value) => value + 1)}>
                    Retry download
                </button>
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
