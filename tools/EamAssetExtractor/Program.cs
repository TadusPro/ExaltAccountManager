using System.Security.Cryptography;
using System.Text.Json;
using RotMGAssetExtractor;
using AssetImageBuffer = RotMGAssetExtractor.Flatc.ImageBuffer;
using RotMGAssetExtractor.Model;
using RotMGAssetExtractor.ModelHelpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EamAssetExtractor;

internal static class Program
{
    private const int ItemSize = 40;
    private const int IconSize = 32;
    private const int IconCanvasSize = IconSize + 2;
    private const int SheetColumns = 64;
    private const int ManifestSchemaVersion = 3;

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ParseArguments(args);
            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            if (!File.Exists(options.ResourcesAssetsPath))
                throw new FileNotFoundException(
                    "The installed Realm resources.assets file was not found.",
                    options.ResourcesAssetsPath);

            Directory.CreateDirectory(Path.GetDirectoryName(options.ManifestPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(options.RenderSheetPath)!);

            var sourceChecksum = await GetFileChecksumAsync(options.ResourcesAssetsPath);

            if (!options.Force && IsManifestCurrent(options, sourceChecksum))
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    status = "cached",
                    buildHash = sourceChecksum,
                }));
                return 0;
            }

            await RotMGAssetExtractor.RotMGAssetExtractor.LoadLocalResourcesAsync(
                options.ResourcesAssetsPath,
                sourceChecksum);

            var entries = BuildEntries();
            if (entries.Count == 0)
                throw new InvalidOperationException("The current game build did not contain renderable item models.");

            using var spriteSheet = BuildSpriteSheet(entries);
            await spriteSheet.SaveAsPngAsync(options.RenderSheetPath);

            var manifest = new AssetManifest
            {
                // Bump this whenever the sheet's crop/placement format changes so
                // existing browser-side item image caches cannot reuse old crops.
                SchemaVersion = ManifestSchemaVersion,
                BuildHash = sourceChecksum,
                BuildVersion = string.Empty,
                Items = entries.ToDictionary(
                    entry => entry.Model.type.ToString(),
                    entry => new object[]
                    {
                        entry.Name,
                        entry.SlotType,
                        entry.Tier,
                        entry.X,
                        entry.Y,
                        0,
                        entry.FeedPower,
                        entry.BagType,
                        entry.Soulbound,
                        entry.Rarity,
                        entry.IsShiny,
                    })
            };

            var serializerOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            await File.WriteAllTextAsync(
                options.ManifestPath,
                JsonSerializer.Serialize(manifest, serializerOptions));

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = "updated",
                buildHash = manifest.BuildHash,
                itemCount = manifest.Items.Count,
            }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task<string> GetFileChecksumAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var checksum = await MD5.HashDataAsync(stream);
        return Convert.ToHexString(checksum).ToLowerInvariant();
    }

    private static bool IsManifestCurrent(ExtractorOptions options, string sourceChecksum)
    {
        if (!File.Exists(options.ManifestPath) || !File.Exists(options.RenderSheetPath))
            return false;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(options.ManifestPath));
            return document.RootElement.TryGetProperty("schemaVersion", out var schemaVersion)
                && schemaVersion.TryGetInt32(out var schemaValue)
                && schemaValue == ManifestSchemaVersion
                && document.RootElement.TryGetProperty("buildHash", out var buildHash)
                && string.Equals(
                    buildHash.GetString(),
                    sourceChecksum,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static List<RenderEntry> BuildEntries()
    {
        var entries = new Dictionary<int, RenderEntry>();
        var modelTypes = new[]
        {
            "Equipment",
            "Skin",
            "PetSkin",
            "PetAbility",
            "Dye",
            "Emote",
            "Entrance",
        };

        foreach (var modelType in modelTypes)
        {
            if (!RotMGAssetExtractor.RotMGAssetExtractor.BuildModelsByType.TryGetValue(modelType, out var models))
                continue;

            foreach (var model in models.OfType<RotMGAssetExtractor.Model.Object>())
            {
                if (model.type <= 0 || entries.ContainsKey(model.type))
                    continue;

                var texture = GetTexture(model);
                if (texture == null)
                    continue;

                using var image = AssetImageBuffer.GetImage(texture, model.type);
                if (image == null || image.Width <= 0 || image.Height <= 0)
                    continue;

                var equipment = model as Equipment;
                var name = model switch
                {
                    Equipment item when !string.IsNullOrWhiteSpace(item.DisplayId) => item.DisplayId,
                    PetSkin petSkin when !string.IsNullOrWhiteSpace(petSkin.DisplayId) => petSkin.DisplayId,
                    _ when !string.IsNullOrWhiteSpace(model.id) => model.id,
                    _ => $"Item {model.type}",
                };

                entries[model.type] = new RenderEntry
                {
                    Model = model,
                    Name = name,
                    Image = image.Clone(),
                    SlotType = equipment?.SlotType ?? 10,
                    Tier = equipment?.Tier ?? -1,
                    FeedPower = equipment?.feedPower ?? 0,
                    BagType = equipment?.BagType ?? 0,
                    Soulbound = equipment?.Soulbound ?? false,
                    Rarity = GetRarity(equipment?.Rarity),
                    IsShiny = IsShiny(name, model.id),
                };
            }
        }

        return entries.Values
            .OrderBy(entry => entry.Model.type)
            .ToList();
    }

    private static ITexture? GetTexture(RotMGAssetExtractor.Model.Object model) => model switch
    {
        Equipment equipment => (ITexture?)equipment.AnimatedTexture ?? (ITexture?)model.AnimatedTexture ?? equipment.Texture,
        Skin skin => (ITexture?)skin.AnimatedTexture ?? (ITexture?)model.AnimatedTexture ?? skin.Texture,
        _ => (ITexture?)model.AnimatedTexture ?? model.Texture,
    };

    private static Image<Rgba32> BuildSpriteSheet(IReadOnlyList<RenderEntry> entries)
    {
        var rows = (int)Math.Ceiling(entries.Count / (double)SheetColumns);
        var sheet = new Image<Rgba32>(SheetColumns * ItemSize, rows * ItemSize);
        sheet.Mutate(context => context.Clear(SixLabors.ImageSharp.Color.Transparent));

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            entry.X = (index % SheetColumns) * ItemSize;
            entry.Y = (index / SheetColumns) * ItemSize;

            using var resized = entry.Image.Clone(context => context.Resize(new ResizeOptions
            {
                // Match Muledump's source-sprite scaling. The outline is added
                // separately below, so the raw sprite remains a 32x32 image.
                Size = new Size(IconSize, IconSize),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.NearestNeighbor,
            }));

            using var iconCanvas = new Image<Rgba32>(IconCanvasSize, IconCanvasSize);
            iconCanvas.Mutate(context =>
            {
                context.Clear(SixLabors.ImageSharp.Color.Transparent);
                context.DrawImage(resized, new Point(1, 1), 1f);
            });

            using var edge = CreateEdgeOutline(iconCanvas);
            var offset = new Point(
                entry.X + ((ItemSize - IconCanvasSize) / 2),
                entry.Y + ((ItemSize - IconCanvasSize) / 2));
            sheet.Mutate(context =>
            {
                context.DrawImage(iconCanvas, offset, 1f);
                context.DrawImage(edge, offset, 1f);
            });
            entry.Image.Dispose();
        }

        return sheet;
    }

    private static Image<Rgba32> CreateEdgeOutline(Image<Rgba32> icon)
    {
        var outline = new Image<Rgba32>(icon.Width, icon.Height);
        outline.Mutate(context => context.Clear(SixLabors.ImageSharp.Color.Transparent));

        for (var y = 0; y < icon.Height; y++)
        {
            for (var x = 0; x < icon.Width; x++)
            {
                if (icon.Frames.RootFrame.DangerousGetPixelRowMemory(y).Span[x].A == 0)
                    continue;

                for (var offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (var offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        var neighborX = x + offsetX;
                        var neighborY = y + offsetY;
                        if (neighborX < 0 || neighborY < 0 || neighborX >= icon.Width || neighborY >= icon.Height)
                            continue;

                        if (icon.Frames.RootFrame.DangerousGetPixelRowMemory(neighborY).Span[neighborX].A == 0)
                            outline.Frames.RootFrame.DangerousGetPixelRowMemory(neighborY).Span[neighborX] = new Rgba32(0, 0, 0, 255);
                    }
                }
            }
        }

        return outline;
    }

    private static int GetRarity(string? rarity)
    {
        if (string.IsNullOrWhiteSpace(rarity))
            return 0;
        if (rarity.Contains("UT", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (rarity.Contains("ST", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 0;
    }

    private static bool IsShiny(string? name, string? id) =>
        (name?.Contains("shiny", StringComparison.OrdinalIgnoreCase) ?? false)
        || (id?.Contains("shiny", StringComparison.OrdinalIgnoreCase) ?? false);

    private static ExtractorOptions ParseArguments(string[] args)
    {
        var options = new ExtractorOptions
        {
            ResourcesAssetsPath = string.Empty,
            ManifestPath = Path.Combine(AppContext.BaseDirectory, "AssetCache", "manifest.json"),
            RenderSheetPath = Path.Combine(AppContext.BaseDirectory, "AssetCache", "renders.png"),
        };

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--resources-assets":
                    options.ResourcesAssetsPath = GetValue(args, ref index, "--resources-assets");
                    break;
                case "--manifest":
                    options.ManifestPath = GetValue(args, ref index, "--manifest");
                    break;
                case "--render-sheet":
                    options.RenderSheetPath = GetValue(args, ref index, "--render-sheet");
                    break;
                case "--force":
                    options.Force = true;
                    break;
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }

        if (!options.ShowHelp && string.IsNullOrWhiteSpace(options.ResourcesAssetsPath))
            throw new ArgumentException("Argument '--resources-assets' is required.");

        return options;
    }

    private static string GetValue(string[] args, ref int index, string argument)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException($"Argument '{argument}' requires a value.");
        return args[++index];
    }

    private static void PrintHelp()
    {
        Console.WriteLine("EAM asset extractor");
        Console.WriteLine("  --resources-assets <path>  Installed Realm resources.assets input");
        Console.WriteLine("  --manifest <path>       EAM runtime manifest output path");
        Console.WriteLine("  --render-sheet <path>   EAM renders.png-compatible output path");
        Console.WriteLine("  --force                 Regenerate even when the local file is unchanged");
    }

    private sealed class ExtractorOptions
    {
        public required string ResourcesAssetsPath { get; set; }
        public required string ManifestPath { get; set; }
        public required string RenderSheetPath { get; set; }
        public bool Force { get; set; }
        public bool ShowHelp { get; set; }
    }

    private sealed class AssetManifest
    {
        public int SchemaVersion { get; set; }
        public string BuildHash { get; set; } = string.Empty;
        public string BuildVersion { get; set; } = string.Empty;
        public Dictionary<string, object[]> Items { get; set; } = new();
    }

    private sealed class RenderEntry
    {
        public required RotMGAssetExtractor.Model.Object Model { get; init; }
        public required string Name { get; init; }
        public required Image<Rgba32> Image { get; init; }
        public int X { get; set; }
        public int Y { get; set; }
        public int SlotType { get; init; }
        public int Tier { get; init; }
        public int FeedPower { get; init; }
        public int BagType { get; init; }
        public bool Soulbound { get; init; }
        public int Rarity { get; init; }
        public bool IsShiny { get; init; }
    }
}
