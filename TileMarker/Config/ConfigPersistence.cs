using StardewModdingAPI;
using System;
using System.Collections.Generic;
using System.Security;
using TileMarker.Config;

namespace TileMarker
{
    public partial class ModEntry
    {
        private bool canPersistConfig = true;
        private bool loggedConfigPermissionWarning;

        private ModConfig ReadConfigSafely(IModHelper helper)
        {
            try
            {
                ModConfig config = helper.ReadConfig<ModConfig>() ?? new ModConfig();
                config.InteractionButtons ??= new Dictionary<string, string>();
                return config;
            }
            catch (Exception ex) when (IsConfigPermissionException(ex))
            {
                canPersistConfig = false;
                LogConfigPermissionWarning(ex, "read or create");
                return new ModConfig();
            }
        }

        internal void TryWriteConfig()
        {
            if (!canPersistConfig)
            {
                LogConfigPermissionWarning(null, "save");
                return;
            }

            try
            {
                Helper.WriteConfig(Config);
            }
            catch (Exception ex) when (IsConfigPermissionException(ex))
            {
                canPersistConfig = false;
                LogConfigPermissionWarning(ex, "save");
            }
        }

        private void LogConfigPermissionWarning(Exception exception, string operation)
        {
            if (loggedConfigPermissionWarning)
                return;

            loggedConfigPermissionWarning = true;
            string details = exception == null ? "" : $" Technical details: {exception.Message}";
            Monitor.Log(
                $"Tile Marker couldn't {operation} config.json because the mod folder isn't writable. "
                + "The mod will keep running with default settings for this session, but changes won't be saved. "
                + "On macOS, give your user account Read & Write access to the TileMarker folder, then reinstall the mod if needed."
                + details,
                LogLevel.Warn
            );
        }

        private static bool IsConfigPermissionException(Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (current is UnauthorizedAccessException || current is SecurityException)
                    return true;
            }

            return false;
        }
    }
}
