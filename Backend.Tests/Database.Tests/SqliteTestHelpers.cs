using Backend.Database;
using Backend.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Backend.Tests.Database.Tests;

internal static class SqliteTestHelpers
{
    public static AppDbContext CreateContext(bool seed = false)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new AppDbContext(options);
        context.Database.EnsureCreated();

        if (seed)
        {
            AppDbContext.SeedData(context, CancellationToken.None).GetAwaiter().GetResult();
        }

        return context;
    }

    public static DatabaseService CreateService(AppDbContext context)
    {
        // TODO: could allow passing in test output helper to get DB logging in tests
        return new DatabaseService(
            Substitute.For<ILogger<DatabaseService>>(),
            context,
            Substitute.For<IEntityUpdateNotifier>(),
            Substitute.For<IAppEvents>(),
            Substitute.For<IDataFolderService>(),
            Substitute.For<IMediaProcessingService>());
    }
}
