import { useEffect, useMemo, useRef, useState } from "react";
import { Box, Button, Chip, Divider, Paper, Stack, Typography } from "@mui/material";
import ArrowBackOutlinedIcon from "@mui/icons-material/ArrowBackOutlined";
import { useNavigate } from "react-router-dom";
import { items, resolveRuntimeAssetSource } from "../assets/runtimeAssets";

const SAMPLE_ITEM_IDS = ["283", "284", "303"];
const ITEM_CROP_SIZE = 40;
const LEGACY_CELL_SIZE = 50;
const LIVE_CELL_SIZE = 40;

const drawRow = (canvas, image, sampleItems, cellSize, itemPadding) => {
    canvas.width = sampleItems.length * cellSize;
    canvas.height = cellSize;

    const context = canvas.getContext("2d");
    context.clearRect(0, 0, canvas.width, canvas.height);
    context.imageSmoothingEnabled = false;

    sampleItems.forEach(([, item], index) => {
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
            (index * cellSize) + itemPadding,
            itemPadding,
            ITEM_CROP_SIZE,
            ITEM_CROP_SIZE,
        );
    });
};

function AssetRenderComparisonPage() {
    const navigate = useNavigate();
    const legacyCanvasRef = useRef(null);
    const liveCanvasRef = useRef(null);
    const [isLoaded, setIsLoaded] = useState(false);
    const [loadError, setLoadError] = useState(null);

    const sampleItems = useMemo(() => {
        const preferredItems = SAMPLE_ITEM_IDS
            .map((itemId) => [itemId, items[itemId]])
            .filter(([, item]) => item);

        if (preferredItems.length === SAMPLE_ITEM_IDS.length) {
            return preferredItems;
        }

        return Object.entries(items)
            .filter(([itemId, item]) => Number(itemId) > 0 && item?.length >= 5)
            .slice(0, 3);
    }, []);

    useEffect(() => {
        let cancelled = false;
        const image = new Image();
        image.src = resolveRuntimeAssetSource("renders.png");

        image.onload = () => {
            if (cancelled) return;

            drawRow(legacyCanvasRef.current, image, sampleItems, LEGACY_CELL_SIZE, 5);
            drawRow(liveCanvasRef.current, image, sampleItems, LIVE_CELL_SIZE, 0);
            setIsLoaded(true);
        };

        image.onerror = () => {
            if (!cancelled) {
                setLoadError("The live render sheet could not be loaded.");
            }
        };

        return () => {
            cancelled = true;
        };
    }, [sampleItems]);

    return (
        <Box sx={{ p: 3, maxWidth: 900 }}>
            <Stack spacing={2.5}>
                <Box>
                    <Typography variant="h4" sx={{ fontWeight: 700 }}>
                        Asset render comparison
                    </Typography>
                    <Typography color="text.secondary" sx={{ mt: 0.75 }}>
                        A controlled one-row test for the old renderer geometry versus the live-extracted renderer geometry.
                    </Typography>
                </Box>

                <Paper sx={{ p: 2.5 }}>
                    <Stack spacing={2}>
                        <Box>
                            <Typography variant="h6">Same live sheet, two render styles</Typography>
                            <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                                Both rows use the same current runtime asset source. This isolates crop size, cell size, and transparent padding without restoring the retired bundled asset atlas.
                            </Typography>
                        </Box>

                        <Divider />

                        <Stack spacing={1}>
                            <Stack direction="row" spacing={1} alignItems="center">
                                <Chip size="small" label="OLD" color="warning" />
                                <Typography variant="subtitle1">Legacy renderer: 50px cell / 40px crop / 5px padding</Typography>
                            </Stack>
                            <Box sx={{ minHeight: LEGACY_CELL_SIZE, bgcolor: "background.default", display: "flex", alignItems: "flex-start" }}>
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
                                <Typography variant="subtitle1">Live renderer: 40px cell / 32px sprite inside the 40px crop</Typography>
                            </Stack>
                            <Box sx={{ minHeight: LIVE_CELL_SIZE, bgcolor: "background.default", display: "flex", alignItems: "flex-start" }}>
                                <canvas
                                    ref={liveCanvasRef}
                                    aria-label="Live renderer comparison row"
                                    style={{ imageRendering: "pixelated", display: "block" }}
                                />
                            </Box>
                        </Stack>

                        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                            {sampleItems.map(([itemId, item]) => (
                                <Chip key={itemId} size="small" variant="outlined" label={`${item?.[0] || "Unknown"} (#${itemId})`} />
                            ))}
                        </Stack>

                        {!isLoaded && !loadError && (
                            <Typography color="text.secondary">Loading the live render sheet…</Typography>
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
