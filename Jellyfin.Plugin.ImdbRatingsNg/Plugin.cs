using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Jellyfin.Plugin.ImdbRatingsNg.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ImdbRatingsNg
{
    /// <summary>
    /// The main plugin.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Plugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            MigrateOldConfiguration(applicationPaths, xmlSerializer);
            CleanupOldPluginDirectories(applicationPaths);
        }

        /// <inheritdoc />
        public override string Name => "IMDb Ratings NG";

        /// <inheritdoc />
        public override Guid Id => Guid.Parse("12418add-9a9d-422d-8e35-dde91cf5baf9");

        /// <summary>
        /// Gets the current plugin instance.
        /// </summary>
        public static Plugin? Instance { get; private set; }

        /// <summary>
        /// Gets Description.
        /// </summary>
        public override string Description => "Get ratings for movies, series, seasons, and episodes from IMDb.";

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = this.Name,
                    EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
                }
            };
        }

        private void MigrateOldConfiguration(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        {
            TryMigrateConfiguration(ConfigurationFilePath, applicationPaths.PluginConfigurationsPath, xmlSerializer, UpdateConfiguration);
        }

        internal static bool TryMigrateConfiguration(
            string configurationFilePath,
            string pluginConfigurationsPath,
            IXmlSerializer? xmlSerializer = null,
            Action<PluginConfiguration>? onConfigLoaded = null)
        {
            if (File.Exists(configurationFilePath))
            {
                return false;
            }

            var oldConfigCandidates = new[]
            {
                Path.Combine(pluginConfigurationsPath, "Jellyfin.Plugin.ImdbRatings.xml"),
                Path.Combine(pluginConfigurationsPath, "Jellyfin.Plugin.Imdb.xml")
            };

            foreach (var oldConfigPath in oldConfigCandidates)
            {
                if (File.Exists(oldConfigPath))
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(configurationFilePath);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        File.Copy(oldConfigPath, configurationFilePath, overwrite: false);

                        if (xmlSerializer != null && xmlSerializer.DeserializeFromFile(typeof(PluginConfiguration), configurationFilePath) is PluginConfiguration migratedConfig)
                        {
                            onConfigLoaded?.Invoke(migratedConfig);
                        }

                        return true;
                    }
                    catch
                    {
                        // Fall back to default configuration
                    }
                }
            }

            return false;
        }

        private void CleanupOldPluginDirectories(IApplicationPaths applicationPaths)
        {
            try
            {
                var currentPluginDir = Path.GetDirectoryName(GetType().Assembly.Location);
                TryCleanupOldPluginDirectories(currentPluginDir, applicationPaths.PluginsPath);
            }
            catch
            {
                // Suppress any top-level exceptions during directory cleanup
            }
        }

        internal static List<string> TryCleanupOldPluginDirectories(
            string? currentPluginDir,
            string pluginsPath)
        {
            var deletedDirectories = new List<string>();

            if (string.IsNullOrEmpty(pluginsPath) || !Directory.Exists(pluginsPath))
            {
                return deletedDirectories;
            }

            string? normalizedCurrent = null;
            if (!string.IsNullOrEmpty(currentPluginDir))
            {
                try
                {
                    normalizedCurrent = Path.GetFullPath(currentPluginDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch
                {
                    normalizedCurrent = currentPluginDir;
                }
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(pluginsPath);
            }
            catch
            {
                return deletedDirectories;
            }

            foreach (var dir in directories)
            {
                string normalizedDir;
                try
                {
                    normalizedDir = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch
                {
                    normalizedDir = dir;
                }

                // Never delete the current active plugin directory
                if (!string.IsNullOrEmpty(normalizedCurrent) && string.Equals(normalizedDir, normalizedCurrent, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var dirName = Path.GetFileName(normalizedDir);
                if (string.IsNullOrEmpty(dirName))
                {
                    continue;
                }

                // Never delete the new NG plugin folders or special folders
                if (dirName.StartsWith("IMDb Ratings NG", StringComparison.OrdinalIgnoreCase)
                    || dirName.StartsWith("IMDbRatingsNg", StringComparison.OrdinalIgnoreCase)
                    || dirName.StartsWith("Jellyfin.Plugin.ImdbRatingsNg", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dirName, "configurations", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Must match previous plugin names
                bool isOldNameMatch =
                    dirName.StartsWith("IMDb Ratings", StringComparison.OrdinalIgnoreCase)
                    || dirName.StartsWith("IMDbRatings", StringComparison.OrdinalIgnoreCase)
                    || dirName.StartsWith("Jellyfin.Plugin.ImdbRatings", StringComparison.OrdinalIgnoreCase)
                    || dirName.StartsWith("Jellyfin.Plugin.Imdb", StringComparison.OrdinalIgnoreCase);

                if (!isOldNameMatch)
                {
                    continue;
                }

                // Safety verification: do not delete if it contains the new DLL
                bool containsNewDll = File.Exists(Path.Combine(normalizedDir, "Jellyfin.Plugin.ImdbRatingsNg.dll"));
                if (containsNewDll)
                {
                    continue;
                }

                bool containsOldDll =
                    File.Exists(Path.Combine(normalizedDir, "Jellyfin.Plugin.ImdbRatings.dll"))
                    || File.Exists(Path.Combine(normalizedDir, "Jellyfin.Plugin.Imdb.dll"));

                bool containsOldMeta = false;
                var metaFile = Path.Combine(normalizedDir, "meta.json");
                if (File.Exists(metaFile))
                {
                    try
                    {
                        var content = File.ReadAllText(metaFile);
                        if (content.Contains("12418add-9a9d-422d-8e35-dde91cf5baf9", StringComparison.OrdinalIgnoreCase)
                            && !content.Contains("IMDb Ratings NG", StringComparison.OrdinalIgnoreCase))
                        {
                            containsOldMeta = true;
                        }
                    }
                    catch
                    {
                        // Ignore read error
                    }
                }

                if (containsOldDll || containsOldMeta)
                {
                    try
                    {
                        Directory.Delete(normalizedDir, true);
                        deletedDirectories.Add(normalizedDir);
                    }
                    catch
                    {
                        // Suppress if file is locked or permission issue
                    }
                }
            }

            return deletedDirectories;
        }
    }
}
