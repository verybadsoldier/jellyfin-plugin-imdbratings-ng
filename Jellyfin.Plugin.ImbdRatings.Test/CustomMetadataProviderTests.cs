using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.ImdbRatingsNg;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Providers.Plugins.Imdb;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.ImbdRatingsNg.Test
{
    public sealed class CustomMetadataProviderTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly FakeLogger<ILogger> _logger;
        private readonly FakeLogger<ImdbItemProvider> _providerLogger;

        public CustomMetadataProviderTests()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"imdbratings_custom_provider_test_{Guid.NewGuid():N}.db");
            _logger = new FakeLogger<ILogger>();
            _providerLogger = new FakeLogger<ImdbItemProvider>();
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            if (File.Exists(_testDbPath))
            {
                try
                {
                    File.Delete(_testDbPath);
                }
                catch (IOException)
                {
                    // Ignore transient lock during cleanup in temp dir
                }
            }
        }

        private async Task SeedDatabaseAsync()
        {
            using var connection = new SqliteConnection($"Data Source={_testDbPath};Pooling=False");
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            string ratingsTsv = "tconst\taverageRating\tnumVotes\n" +
                                "tt0111161\t9.3\t2800000\n" +
                                "tt0903747\t9.5\t2000000\n" +
                                "tt0959621\t9.0\t50000\n" +
                                "tt2301451\t10.0\t200000\n";

            string episodesTsv = "tconst\tparentTconst\tseasonNumber\tepisodeNumber\n" +
                                 "tt0959621\ttt0903747\t1\t1\n" +
                                 "tt2301451\ttt0903747\t5\t14\n";

            using (var ratingsReader = new StringReader(ratingsTsv))
            {
                await IMDbRatingsManager.ImportRatingsAsync(connection, ratingsReader);
            }

            using (var epReader = new StringReader(episodesTsv))
            {
                await IMDbRatingsManager.ImportEpisodesAsync(connection, epReader);
            }
        }

        [Fact]
        public async Task FetchAsync_Movie_OverwritesExistingCommunityRating()
        {
            await SeedDatabaseAsync();
            var manager = new IMDbRatingsManager(_logger, _testDbPath);
            var provider = new ImdbItemProvider(null!, null!, null!, null!, null!, _providerLogger, manager);

            var movie = new Movie
            {
                Name = "The Shawshank Redemption",
                CommunityRating = 7.5f // Simulates TMDb having already populated its rating first
            };
            movie.SetProviderId(MetadataProvider.Imdb, "tt0111161");

            var result = await provider.FetchAsync(movie, null!, CancellationToken.None);

            Assert.Equal(ItemUpdateType.MetadataEdit, result);
            Assert.Equal(9.3f, movie.CommunityRating);

            // Second pass when already up to date should return None
            var secondResult = await provider.FetchAsync(movie, null!, CancellationToken.None);
            Assert.Equal(ItemUpdateType.None, secondResult);
        }

        [Fact]
        public async Task FetchAsync_Series_OverwritesExistingCommunityRating()
        {
            await SeedDatabaseAsync();
            var manager = new IMDbRatingsManager(_logger, _testDbPath);
            var provider = new ImdbItemProvider(null!, null!, null!, null!, null!, _providerLogger, manager);

            var series = new Series
            {
                Name = "Breaking Bad",
                CommunityRating = 8.0f // Previous rating from TVDB / TMDb
            };
            series.SetProviderId(MetadataProvider.Imdb, "tt0903747");

            var result = await provider.FetchAsync(series, null!, CancellationToken.None);

            Assert.Equal(ItemUpdateType.MetadataEdit, result);
            Assert.Equal(9.5f, series.CommunityRating);
        }

        [Fact]
        public async Task FetchAsync_Episode_WithImdbId_OverwritesExistingCommunityRating()
        {
            await SeedDatabaseAsync();
            var manager = new IMDbRatingsManager(_logger, _testDbPath);
            var provider = new ImdbItemProvider(null!, null!, null!, null!, null!, _providerLogger, manager);

            var episode = new Episode
            {
                Name = "Ozymandias",
                CommunityRating = 8.5f
            };
            episode.SetProviderId(MetadataProvider.Imdb, "tt2301451");

            var result = await provider.FetchAsync(episode, null!, CancellationToken.None);

            Assert.Equal(ItemUpdateType.MetadataEdit, result);
            Assert.Equal(10.0f, episode.CommunityRating);
        }

        [Fact]
        public async Task FetchAsync_Episode_WithoutImdbId_ResolvesViaSeriesAndAppliesRating()
        {
            await SeedDatabaseAsync();
            var manager = new IMDbRatingsManager(_logger, _testDbPath);

            var seriesId = Guid.NewGuid();
            var series = new Series
            {
                Id = seriesId,
                Name = "Breaking Bad"
            };
            series.SetProviderId(MetadataProvider.Imdb, "tt0903747");

            var libraryManager = Substitute.For<ILibraryManager>();
            libraryManager.GetItemById(seriesId).Returns(series);

            var provider = new ImdbItemProvider(null!, libraryManager, null!, null!, null!, _providerLogger, manager);

            var episode = new Episode
            {
                Name = "Pilot",
                SeriesId = seriesId,
                ParentIndexNumber = 1,
                IndexNumber = 1,
                CommunityRating = 7.0f
            };
            // Note: episode has NO IMDb ID set directly

            var result = await provider.FetchAsync(episode, null!, CancellationToken.None);

            Assert.Equal(ItemUpdateType.MetadataEdit, result);
            Assert.Equal(9.0f, episode.CommunityRating);
        }

        [Fact]
        public async Task FetchAsync_ReturnsNone_WhenItemHasNoImdbIdAndCannotBeResolved()
        {
            await SeedDatabaseAsync();
            var manager = new IMDbRatingsManager(_logger, _testDbPath);
            var provider = new ImdbItemProvider(null!, null!, null!, null!, null!, _providerLogger, manager);

            var movie = new Movie
            {
                Name = "Unknown Home Video",
                CommunityRating = 5.0f
            };

            var result = await provider.FetchAsync(movie, null!, CancellationToken.None);

            Assert.Equal(ItemUpdateType.None, result);
            Assert.Equal(5.0f, movie.CommunityRating);
        }
    }
}
