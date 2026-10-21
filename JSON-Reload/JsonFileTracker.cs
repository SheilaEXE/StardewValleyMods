using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace JsonReload;

internal sealed class JsonFileTracker
{
    private const string ContentPatcherId = "Pathoschild.ContentPatcher";

    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly Dictionary<string, TrackedMod> mods = new(StringComparer.OrdinalIgnoreCase);

    public JsonFileTracker(IModHelper helper, IMonitor monitor)
    {
        this.helper = helper;
        this.monitor = monitor;
    }

    public TrackingSummary DiscoverAndSnapshot()
    {
        this.mods.Clear();
        string modsPath = Path.Combine(Constants.GamePath, "Mods");
        int contentPackCount = 0;
        int translationModCount = 0;

        foreach (string manifestPath in EnumerateManifestFiles(modsPath))
        {
            try
            {
                JObject manifest = ReadJsonObject(manifestPath);
                string? uniqueId = manifest.Value<string>("UniqueID");
                string? targetId = manifest["ContentPackFor"]?.Value<string>("UniqueID");
                string directoryPath = Path.GetDirectoryName(manifestPath)!;
                bool isContentPatcherPack = string.Equals(
                    targetId,
                    ContentPatcherId,
                    StringComparison.OrdinalIgnoreCase
                );
                bool hasTranslations = Directory.Exists(Path.Combine(directoryPath, "i18n"));

                if (string.IsNullOrWhiteSpace(uniqueId)
                    || !this.helper.ModRegistry.IsLoaded(uniqueId)
                    || (!isContentPatcherPack && !hasTranslations))
                {
                    continue;
                }

                string name = manifest.Value<string>("Name") ?? uniqueId;
                var mod = new TrackedMod(uniqueId, name, directoryPath, isContentPatcherPack);
                mod.ReplaceSnapshot(CaptureSnapshot(mod));
                this.mods[uniqueId] = mod;

                if (isContentPatcherPack)
                    contentPackCount++;
                else
                    translationModCount++;
            }
            catch (Exception ex)
            {
                this.monitor.Log($"Could not inspect manifest '{manifestPath}': {ex.Message}", LogLevel.Trace);
            }
        }

        return new TrackingSummary(contentPackCount, translationModCount);
    }

    public IReadOnlyList<ModChanges> FindChanges()
    {
        var results = new List<ModChanges>();

        foreach (TrackedMod mod in this.mods.Values.OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase))
        {
            Dictionary<string, JsonFileStamp> current = CaptureSnapshot(mod);
            string[] changedPaths = mod.Snapshot.Keys
                .Union(current.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(path => !mod.Snapshot.TryGetValue(path, out JsonFileStamp previous)
                    || !current.TryGetValue(path, out JsonFileStamp now)
                    || previous != now)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (changedPaths.Length > 0)
                results.Add(new ModChanges(mod, current, changedPaths));
        }

        return results;
    }

    public bool TryValidateChanges(ModChanges changes, out IReadOnlyList<JsonValidationError> errors)
    {
        var foundErrors = new List<JsonValidationError>();

        foreach (string relativePath in changes.ChangedPaths)
        {
            string fullPath = Path.Combine(changes.Mod.DirectoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                continue;

            try
            {
                using var stream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var textReader = new StreamReader(stream);
                using var jsonReader = new JsonTextReader(textReader)
                {
                    DateParseHandling = DateParseHandling.None,
                    CloseInput = false
                };

                JToken.ReadFrom(jsonReader, new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Ignore,
                    LineInfoHandling = LineInfoHandling.Load
                });

                while (jsonReader.Read())
                {
                    if (jsonReader.TokenType != JsonToken.Comment)
                        throw new JsonReaderException("Additional content was found after the JSON document.");
                }
            }
            catch (JsonReaderException ex)
            {
                foundErrors.Add(new JsonValidationError(relativePath, ex.LineNumber, ex.LinePosition, ex.Message));
            }
            catch (Exception ex)
            {
                foundErrors.Add(new JsonValidationError(relativePath, null, null, ex.Message));
            }
        }

        errors = foundErrors;
        return foundErrors.Count == 0;
    }

    public static bool IsTranslationPath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        return normalized.StartsWith("i18n/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRestartOnlyPath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        return normalized.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("config.json", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateManifestFiles(string modsPath)
    {
        try
        {
            return Directory.EnumerateFiles(modsPath, "manifest.json", SearchOption.AllDirectories).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static Dictionary<string, JsonFileStamp> CaptureSnapshot(TrackedMod mod)
    {
        var snapshot = new Dictionary<string, JsonFileStamp>(StringComparer.OrdinalIgnoreCase);
        string searchPath = mod.IsContentPatcherPack
            ? mod.DirectoryPath
            : Path.Combine(mod.DirectoryPath, "i18n");

        try
        {
            foreach (string fullPath in Directory.EnumerateFiles(searchPath, "*.json", SearchOption.AllDirectories))
            {
                var file = new FileInfo(fullPath);
                string relativePath = Path.GetRelativePath(mod.DirectoryPath, fullPath).Replace('\\', '/');
                snapshot[relativePath] = new JsonFileStamp(file.Length, file.LastWriteTimeUtc.Ticks);
            }
        }
        catch
        {
            // A later reload attempt will report a concrete read error for any file it can see.
        }

        return snapshot;
    }

    private static JObject ReadJsonObject(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var textReader = new StreamReader(stream);
        using var jsonReader = new JsonTextReader(textReader) { DateParseHandling = DateParseHandling.None };
        return JObject.Load(jsonReader, new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });
    }
}

internal sealed class TrackedMod
{
    public string UniqueId { get; }

    public string Name { get; }

    public string DirectoryPath { get; }

    public bool IsContentPatcherPack { get; }

    public Dictionary<string, JsonFileStamp> Snapshot { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public TrackedMod(string uniqueId, string name, string directoryPath, bool isContentPatcherPack)
    {
        this.UniqueId = uniqueId;
        this.Name = name;
        this.DirectoryPath = directoryPath;
        this.IsContentPatcherPack = isContentPatcherPack;
    }

    public void ReplaceSnapshot(Dictionary<string, JsonFileStamp> snapshot)
    {
        this.Snapshot = snapshot;
    }
}

internal sealed record ModChanges(
    TrackedMod Mod,
    Dictionary<string, JsonFileStamp> CurrentSnapshot,
    IReadOnlyList<string> ChangedPaths
);

internal readonly record struct TrackingSummary(int ContentPackCount, int TranslationModCount);

internal readonly record struct JsonFileStamp(long Length, long LastWriteUtcTicks);

internal sealed record JsonValidationError(string RelativePath, int? Line, int? Column, string Message);
