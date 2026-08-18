using System.Text.Json;
using RotMGAssetExtractor;
using AssetImageBuffer = RotMGAssetExtractor.Flatc.ImageBuffer;
using RotMGAssetExtractor.Model;
using RotMGAssetExtractor.ModelHelpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EamAssetExtractor;

internal static class Program
{
    private const int ItemSize = 40;
    private const int SheetColumns = 64;

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

            Directory.CreateDirectory(options.DataDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(options.ManifestPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(options.RenderSheetPath)!);

            if (options.Force)
            {
                // Removing only the extractor metadata invalidates its build cache while
                // keeping the downloaded files recoverable for the next extraction.
                var metadataPath = Path.Combine(options.DataDirectory, "GameData", "meta.xml");
                if (File.Exists(metadataPath))
                    File.Delete(metadataPath);
            }

            await RotMGAssetExtractor.RotMGAssetExtractor.InitAsync(
                options.DataDirectory,
                ExtractionType.Models,
                ExtractionType.ImagesLight,
                ExtractionType.Spritesheet);

            if (!options.Force && IsManifestCurrent(options))
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    status = "cached",
                    buildHash = RotMGAssetExtractor.RotMGAssetExtractor.BuildHash,
                }));
                return 0;
            }

            var entries = BuildEntries();
            if (entries.Count == 0)
                throw new InvalidOperationException("The current game build did not contain renderable item models.");

            using var spriteSheet = BuildSpriteSheet(entries);
            await spriteSheet.SaveAsPngAsync(options.RenderSheetPath);

            var manifest = new AssetManifest
            {
                SchemaVersion = 1,
                BuildHash = RotMGAssetExtractor.RotMGAssetExtractor.BuildHash,
                BuildVersion = RotMGAssetExtractor.RotMGAssetExtractor.BuildVersion,
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

    private static bool IsManifestCurrent(ExtractorOptions options)
    {
        if (!File.Exists(options.ManifestPath) || !File.Exists(options.RenderSheetPath))
            return false;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(options.ManifestPath));
            return document.RootElement.TryGetProperty("buildHash", out var buildHash)
                && string.Equals(
                    buildHash.GetString(),
                    RotMGAssetExtractor.RotMGAssetExtractor.BuildHash,
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
                Size = new Size(ItemSize, ItemSize),
                Mode = ResizeMode.Max,
                Sampler = KnownResamplers.NearestNeighbor,
            }));

            var offset = new Point(
                entry.X + ((ItemSize - resized.Width) / 2),
                entry.Y + ((ItemSize - resized.Height) / 2));
            sheet.Mutate(context => context.DrawImage(resized, offset, 1f));
            entry.Image.Dispose();
        }

        return sheet;
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
            DataDirectory = Path.Combine(AppContext.BaseDirectory, "AssetCache"),
            ManifestPath = Path.Combine(AppContext.BaseDirectory, "AssetCache", "manifest.json"),
            RenderSheetPath = Path.Combine(AppContext.BaseDirectory, "AssetCache", "renders.png"),
        };

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--data-dir":
                    options.DataDirectory = GetValue(args, ref index, "--data-dir");
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
        Console.WriteLine("  --data-dir <path>       Extractor download/cache directory");
        Console.WriteLine("  --manifest <path>       EAM runtime manifest output path");
        Console.WriteLine("  --render-sheet <path>   EAM renders.png-compatible output path");
        Console.WriteLine("  --force                 Refresh the current build metadata");
    }

    private sealed class ExtractorOptions
    {
        public required string DataDirectory { get; set; }
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
