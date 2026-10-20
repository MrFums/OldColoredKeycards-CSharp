using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using OldColoredKeycards.Keycards;

namespace OldColoredKeycards;

[Injectable(InjectionType.Singleton)]
public class ConfigManager(
    ISptLogger<ConfigManager> logger,
    ModHelper modHelper)
{
    // .jsonc so editors know the // notes in it are allowed.
    private const string ConfigFile = "config.jsonc";

    // The shipped config.jsonc, notes and all, built into the DLL (see OldColoredKeycards.csproj).
    private const string DefaultConfigResource = "OldColoredKeycards.config.jsonc";

    public ModConfig Mod { get; private set; } = new();

    public string ModPath { get; private set; } = string.Empty;

    public void Load()
    {
        ModPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        RestoreConfigIfMissing();
        Mod = LoadOrDefault<ModConfig>(ConfigFile);

        Validate();
        DebugLog.Bind(this, message => logger.Info(message));
    }

    private void Validate()
    {
        WarnUnknown(Mod.Unknown, "");
        WarnUnknown(Mod.Uses.Unknown, "uses.");
        WarnUnknown(Mod.Prices.Unknown, "prices.");
        WarnUnknown(Mod.Spawns.Unknown, "spawns.");

        // The JS version quietly used 40 here too.
        if (Mod.Uses.MaxUses < 1)
        {
            logger.Warning($"[OldColoredKeycards] uses.maxUses must be 1 or more, using {KeycardUsesConfig.DefaultMaxUses}.");
            Mod.Uses.MaxUses = KeycardUsesConfig.DefaultMaxUses;
        }

        foreach (var keycard in ColoredKeycards.All)
        {
            if (keycard.Price(Mod.Prices) < 0)
            {
                logger.Warning($"[OldColoredKeycards] prices.{keycard.ConfigName} can't be below 0, keeping the normal {keycard.Name} price.");
            }
        }
    }

    // A misspelt or moved setting would otherwise be ignored without a word and quietly use its default.
    private void WarnUnknown(Dictionary<string, System.Text.Json.JsonElement>? unknown, string section)
    {
        if (unknown is not { Count: > 0 })
        {
            return;
        }

        var names = string.Join(", ", unknown.Keys.Select(key => section + key));
        logger.Warning($"[OldColoredKeycards] Unknown setting(s) in {ConfigFile}, ignored: {names}. Check the spelling, or compare with the {ConfigFile} that ships with the mod.");
    }

    // A deleted config.jsonc is written back from the copy in the DLL, so "delete it to reset" works
    // and the user always has a file to edit. An existing file is never touched.
    private void RestoreConfigIfMissing()
    {
        var path = Path.Combine(ModPath, ConfigFile);
        if (File.Exists(path))
        {
            return;
        }

        try
        {
            using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(DefaultConfigResource)
                ?? throw new InvalidOperationException("the default config is missing from the DLL");
            using var target = File.Create(path);
            source.CopyTo(target);
            logger.Info($"[OldColoredKeycards] {ConfigFile} was missing, so a new one was made with the default settings.");
        }
        catch (Exception ex)
        {
            logger.Warning($"[OldColoredKeycards] {ConfigFile} is missing and a new one could not be made, using defaults. ({ex.Message})");
        }
    }

    private T LoadOrDefault<T>(string relativeFile) where T : new()
    {
        try
        {
            return modHelper.GetJsonDataFromFile<T>(ModPath, relativeFile) ?? new T();
        }
        catch (Exception ex)
        {
            logger.Warning($"[OldColoredKeycards] Could not read '{relativeFile}', using defaults. ({ex.Message})");
            return new T();
        }
    }
}
