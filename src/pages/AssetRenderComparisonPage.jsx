import { useEffect, useMemo, useRef, useState } from "react";
import { Box, Button, Chip, Divider, Paper, Stack, Typography } from "@mui/material";
import ArrowBackOutlinedIcon from "@mui/icons-material/ArrowBackOutlined";
import { useNavigate } from "react-router-dom";
import { items, resolveRuntimeAssetSource } from "../assets/runtimeAssets";
import { legacyItems } from "../assets/constants";
import useVaultPeeker from "../hooks/useVaultPeeker";

const ITEM_CROP_SIZE = 40;
const COMPARISON_CELL_SIZE = 50;
const COMPARISON_ITEM_PADDING = 5;

const drawRow = (canvas, image, comparisonItems, itemKey) => {
    canvas.width = comparisonItems.length * COMPARISON_CELL_SIZE;
    canvas.height = COMPARISON_CELL_SIZE;

    const context = canvas.getContext("2d");
    context.clearRect(0, 0, canvas.width, canvas.height);
    context.imageSmoothingEnabled = false;

    comparisonItems.forEach((comparisonItem, index) => {
        const item = comparisonItem[itemKey];
        const itemX = Number(item?.[3]);
        const itemY = Number(item?.[4]);

        if (!Number.isFinite(itemX) || !Number.isFinite(itemY)) {
            return;
        }

        context.drawImage(
            image,
            itemX,
            itemY,
            ITEM_CROP_SIZE,
            ITEM_CROP_SIZE,
            (index * COMPARISON_CELL_SIZE) + COMPARISON_ITEM_PADDING,
            COMPARISON_ITEM_PADDING,
            ITEM_CROP_SIZE,
            ITEM_CROP_SIZE,
        );
    });
};

function AssetRenderComparisonPage() {
    const navigate = useNavigate();
    const { totalsMap, isLoading: isTotalsLoading } = useVaultPeeker();
    const legacyCanvasRef = useRef(null);
    const liveCanvasRef = useRef(null);
    const [isLoaded, setIsLoaded] = useState(false);
    const [loadError, setLoadError] = useState(null);

    const comparisonItems = useMemo(() => {
        if (!totalsMap?.size) return [];

        return Array.from(totalsMap.entries())
            .filter(([itemId]) => Number(itemId) > 0)
            .sort(([, first], [, second]) => (second.count || 0) - (first.count || 0))
            .slice(0, 3)
            .map(([itemId, total]) => ({
                itemId: String(itemId),
                count: total.count || 0,
                liveItem: items[itemId],
                legacyItem: legacyItems[itemId],
            }));
    }, [totalsMap]);

    useEffect(() => {
        if (comparisonItems.length === 0) return undefined;

        let cancelled = false;
        setIsLoaded(false);
        setLoadError(null);

        const legacyImage = new Image();
        const liveImage = new Image();
        let loadedImages = 0;

        const handleLoaded = () => {
            loadedImages += 1;
            if (cancelled || loadedImages !== 2) return;

            drawRow(legacyCanvasRef.current, legacyImage, comparisonItems, "legacyItem");
            drawRow(liveCanvasRef.current, liveImage, comparisonItems, "liveItem");
            setIsLoaded(true);
        };

        const handleError = () => {
            if (!cancelled) setLoadError("The old or live render sheet could not be loaded.");
        };

        legacyImage.onload = handleLoaded;
        liveImage.onload = handleLoaded;
        legacyImage.onerror = handleError;
        liveImage.onerror = handleError;
        legacyImage.src = "/dev/legacy-renders.png";
        liveImage.src = resolveRuntimeAssetSource("renders.png");

        return () => {
            cancelled = true;
        };
    }, [comparisonItems]);

    return (
        <Box sx={{ p: 3, maxWidth: 900 }}>
            <Stack spacing={2.5}>
                <Box>
                    <Typography variant="h4" sx={{ fontWeight: 700 }}>
                        Asset render comparison
                    </Typography>
                    <Typography color="text.secondary" sx={{ mt: 0.75 }}>
                        The same three Vault Peeker totals rendered from the historical bundled atlas and the live-extracted atlas.
                    </Typography>
                </Box>

                <Paper sx={{ p: 2.5 }}>
                    <Stack spacing={2}>
                        <Box>
                            <Typography variant="h6">Historical assets versus live assets</Typography>
                            <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                                Both rows use identical 50px cells and 40px crops. The only source difference is the atlas: the upper row is the pre-live bundled sheet, and the lower row is the runtime extractor output.
                            </Typography>
                        </Box>

                        <Divider />

                        <Stack spacing={1}>
                            <Stack direction="row" spacing={1} alignItems="center">
                                <Chip size="small" label="OLD" color="warning" />
                                <Typography variant="subtitle1">Bundled renders.png</Typography>
                            </Stack>
                            <Box sx={{ minHeight: COMPARISON_CELL_SIZE, bgcolor: "background.default", display: "flex", alignItems: "flex-start" }}>
                                <canvas
                                    ref={legacyCanvasRef}
                                    aria-label="Legacy renderer comparison row"
                                    style={{ imageRendering: "pixelated", display: "block" }}
                                />
                            </Box>
                        </Stack>

                        <Stack spacing={1}>
                            <Stack direction="row" spacing={1} alignItems="center">
                                <Chip size="small" label="NEW" color="primary" />
                                <Typography variant="subtitle1">Live-extracted renders.png</Typography>
                            </Stack>
                            <Box sx={{ minHeight: COMPARISON_CELL_SIZE, bgcolor: "background.default", display: "flex", alignItems: "flex-start" }}>
                                <canvas
                                    ref={liveCanvasRef}
                                    aria-label="Live renderer comparison row"
                                    style={{ imageRendering: "pixelated", display: "block" }}
                                />
                            </Box>
                        </Stack>

                        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                            {comparisonItems.map(({ itemId, count, liveItem, legacyItem }) => (
                                <Chip
                                    key={itemId}
                                    size="small"
                                    variant="outlined"
                                    label={`${liveItem?.[0] || legacyItem?.[0] || "Unknown"} (#${itemId}) ×${count}`}
                                />
                            ))}
                        </Stack>

                        {!isLoaded && !loadError && (isTotalsLoading || comparisonItems.length === 0) && (
                            <Typography color="text.secondary">Loading the top Vault Peeker totals and both render sheets…</Typography>
                        )}
                        {loadError && <Typography color="error">{loadError}</Typography>}
                    </Stack>
                </Paper>

                <Box>
                    <Button
                        variant="outlined"
                        startIcon={<ArrowBackOutlinedIcon />}
                        onClick={() => navigate("/vaultPeekerV2")}
                    >
                        Open normal Vault Peeker
                    </Button>
                </Box>
            </Stack>
        </Box>
    );
}

export default AssetRenderComparisonPage;
