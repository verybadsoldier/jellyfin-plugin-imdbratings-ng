using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.ImdbRatingsNg;
using Xunit;
using Plugin = Jellyfin.Plugin.ImdbRatingsNg.Plugin;

namespace Jellyfin.Plugin.ImbdRatingsNg.Test
{
    public sealed class MigrationTests : IDisposable
    {
        private readonly string _tempDir;

        public MigrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"migration_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
                // Ignore cleanup issues in temp dir
            }
        }

        [Fact]
        public void MigrateConfiguration_MigratesFromOldPluginXml()
        {
            var oldConfigPath = Path.Combine(_tempDir, "Jellyfin.Plugin.ImdbRatings.xml");
            var newConfigPath = Path.Combine(_tempDir, "Jellyfin.Plugin.ImdbRatingsNg.xml");

            const string ExpectedXml = "<PluginConfiguration><DatasetUrl>https://custom.url</DatasetUrl></PluginConfiguration>";
            File.WriteAllText(oldConfigPath, ExpectedXml);

            bool migrated = global::Jellyfin.Plugin.ImdbRatingsNg.Plugin.TryMigrateConfiguration(newConfigPath, _tempDir);

            Assert.True(migrated);
            Assert.True(File.Exists(newConfigPath));
            Assert.Equal(ExpectedXml, File.ReadAllText(newConfigPath));
        }

        [Fact]
        public void MigrateConfiguration_MigratesFromLegacyImdbXml()
        {
            var oldConfigPath = Path.Combine(_tempDir, "Jellyfin.Plugin.Imdb.xml");
            var newConfigPath = Path.Combine(_tempDir, "Jellyfin.Plugin.ImdbRatingsNg.xml");

            const string ExpectedXml = "<PluginConfiguration><DatasetUrl>https://legacy.url</DatasetUrl></PluginConfiguration>";
            File.WriteAllText(oldConfigPath, ExpectedXml);

            bool migrated = global::Jellyfin.Plugin.ImdbRatingsNg.Plugin.TryMigrateConfiguration(newConfigPath, _tempDir);

            Assert.True(migrated);
            Assert.True(File.Exists(newConfigPath));
            Assert.Equal(ExpectedXml, File.ReadAllText(newConfigPath));
        }

        [Fact]
        public void MigrateConfiguration_DoesNotOverwriteExistingConfig()
        {
            var oldConfigPath = Path.Combine(_tempDir, "Jellyfin.Plugin.ImdbRatings.xml");
            var newConfigPath = Path.Combine(_tempDir, "Jellyfin.Plugin.ImdbRatingsNg.xml");

            File.WriteAllText(oldConfigPath, "<OldConfig />");
            File.WriteAllText(newConfigPath, "<NewConfig />");

            bool migrated = global::Jellyfin.Plugin.ImdbRatingsNg.Plugin.TryMigrateConfiguration(newConfigPath, _tempDir);

            Assert.False(migrated);
            Assert.Equal("<NewConfig />", File.ReadAllText(newConfigPath));
        }

        [Fact]
        public void MigrateDatabase_MigratesFromOldDatabaseFolder()
        {
            var oldDataDir = Path.Combine(_tempDir, "IMDb Ratings");
            var newDataDir = Path.Combine(_tempDir, "IMDb Ratings NG");
            Directory.CreateDirectory(oldDataDir);

            var oldDbPath = Path.Combine(oldDataDir, "imdbratings.db");
            var newDbPath = Path.Combine(newDataDir, "imdbratings.db");

            File.WriteAllText(oldDbPath, "MOCK_DB_CONTENT");
            File.WriteAllText(oldDbPath + "-wal", "MOCK_WAL_CONTENT");
            File.WriteAllText(oldDbPath + "-shm", "MOCK_SHM_CONTENT");

            bool migrated = IMDbRatingsManager.TryMigrateDatabase(newDbPath, newDataDir);

            Assert.True(migrated);
            Assert.True(File.Exists(newDbPath));
            Assert.True(File.Exists(newDbPath + "-wal"));
            Assert.True(File.Exists(newDbPath + "-shm"));
            Assert.Equal("MOCK_DB_CONTENT", File.ReadAllText(newDbPath));
            Assert.Equal("MOCK_WAL_CONTENT", File.ReadAllText(newDbPath + "-wal"));
            Assert.Equal("MOCK_SHM_CONTENT", File.ReadAllText(newDbPath + "-shm"));
        }

        [Fact]
        public void MigrateDatabase_DoesNotOverwriteExistingDatabase()
        {
            var oldDataDir = Path.Combine(_tempDir, "IMDb Ratings");
            var newDataDir = Path.Combine(_tempDir, "IMDb Ratings NG");
            Directory.CreateDirectory(oldDataDir);
            Directory.CreateDirectory(newDataDir);

            var oldDbPath = Path.Combine(oldDataDir, "imdbratings.db");
            var newDbPath = Path.Combine(newDataDir, "imdbratings.db");

            File.WriteAllText(oldDbPath, "OLD_DB");
            File.WriteAllText(newDbPath, "EXISTING_NEW_DB");

            bool migrated = IMDbRatingsManager.TryMigrateDatabase(newDbPath, newDataDir);

            Assert.False(migrated);
            Assert.Equal("EXISTING_NEW_DB", File.ReadAllText(newDbPath));
        }

        [Fact]
        public void CleanupOldPluginDirectories_DeletesOldVersionFoldersWithOldDllOrMeta()
        {
            var pluginsDir = Path.Combine(_tempDir, "plugins");
            Directory.CreateDirectory(pluginsDir);

            var oldFolder1 = Path.Combine(pluginsDir, "IMDb Ratings_4.2.0.0");
            var oldFolder2 = Path.Combine(pluginsDir, "Jellyfin.Plugin.ImdbRatings_4.1.0.0");
            var oldFolder3 = Path.Combine(pluginsDir, "IMDb Ratings_3.0.0.0");
            var currentFolder = Path.Combine(pluginsDir, "IMDb Ratings NG_5.0.0.0");
            var otherPluginFolder = Path.Combine(pluginsDir, "Trakt_1.0.0.0");
            var configurationsFolder = Path.Combine(pluginsDir, "configurations");

            Directory.CreateDirectory(oldFolder1);
            Directory.CreateDirectory(oldFolder2);
            Directory.CreateDirectory(oldFolder3);
            Directory.CreateDirectory(currentFolder);
            Directory.CreateDirectory(otherPluginFolder);
            Directory.CreateDirectory(configurationsFolder);

            // Folder 1 has old DLL
            File.WriteAllText(Path.Combine(oldFolder1, "Jellyfin.Plugin.ImdbRatings.dll"), "DUMMY");
            // Folder 2 has old legacy DLL
            File.WriteAllText(Path.Combine(oldFolder2, "Jellyfin.Plugin.Imdb.dll"), "DUMMY");
            // Folder 3 has old meta.json matching the GUID
            File.WriteAllText(Path.Combine(oldFolder3, "meta.json"), "{\"id\": \"12418add-9a9d-422d-8e35-dde91cf5baf9\", \"name\": \"IMDb Ratings\"}");

            // Current folder has new DLL
            File.WriteAllText(Path.Combine(currentFolder, "Jellyfin.Plugin.ImdbRatingsNg.dll"), "DUMMY");
            // Other plugin has its own DLL
            File.WriteAllText(Path.Combine(otherPluginFolder, "Trakt.dll"), "DUMMY");

            var deleted = global::Jellyfin.Plugin.ImdbRatingsNg.Plugin.TryCleanupOldPluginDirectories(currentFolder, pluginsDir);

            Assert.Equal(3, deleted.Count);
            Assert.False(Directory.Exists(oldFolder1));
            Assert.False(Directory.Exists(oldFolder2));
            Assert.False(Directory.Exists(oldFolder3));
            Assert.True(Directory.Exists(currentFolder));
            Assert.True(Directory.Exists(otherPluginFolder));
            Assert.True(Directory.Exists(configurationsFolder));
        }

        [Fact]
        public void CleanupOldPluginDirectories_NeverDeletesCurrentPluginDir()
        {
            var pluginsDir = Path.Combine(_tempDir, "plugins");
            Directory.CreateDirectory(pluginsDir);

            var activeDir = Path.Combine(pluginsDir, "IMDb Ratings_4.2.0.0");
            Directory.CreateDirectory(activeDir);
            File.WriteAllText(Path.Combine(activeDir, "Jellyfin.Plugin.ImdbRatings.dll"), "DUMMY");

            var deleted = global::Jellyfin.Plugin.ImdbRatingsNg.Plugin.TryCleanupOldPluginDirectories(activeDir, pluginsDir);

            Assert.Empty(deleted);
            Assert.True(Directory.Exists(activeDir));
        }

        [Fact]
        public void CleanupOldPluginDirectories_DoesNotDeleteIfContainsNewDll()
        {
            var pluginsDir = Path.Combine(_tempDir, "plugins");
            Directory.CreateDirectory(pluginsDir);

            var folder = Path.Combine(pluginsDir, "IMDb Ratings_5.0.0.0");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "Jellyfin.Plugin.ImdbRatingsNg.dll"), "DUMMY");
            File.WriteAllText(Path.Combine(folder, "Jellyfin.Plugin.ImdbRatings.dll"), "DUMMY");

            var currentFolder = Path.Combine(pluginsDir, "IMDb Ratings NG_5.0.0.0");
            Directory.CreateDirectory(currentFolder);

            var deleted = global::Jellyfin.Plugin.ImdbRatingsNg.Plugin.TryCleanupOldPluginDirectories(currentFolder, pluginsDir);

            Assert.Empty(deleted);
            Assert.True(Directory.Exists(folder));
        }
    }
}
