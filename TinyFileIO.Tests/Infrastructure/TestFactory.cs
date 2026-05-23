using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TinyFileIO.Data;

namespace TinyFileIO.Tests.Infrastructure;

/// <summary>
/// Builds IConfiguration and a SQLite-in-memory AppDbContext factory for service tests.
/// </summary>
internal static class TestFactory
{
    public static IConfiguration ConfigFor(string storeLocation) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StoreLocation"] = storeLocation
            })
            .Build();

    public static IConfiguration ConfigForStaticAccount(string storeLocation, string user, string password) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StoreLocation"]     = storeLocation,
                ["UseStaticAccount"]  = "true",
                ["StaticUser"]        = user,
                ["StaticPassword"]    = password
            })
            .Build();

    public static IDbContextFactory<AppDbContext> CreateInMemoryDbFactory(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new InMemoryDbContextFactory(options);
    }

    private sealed class InMemoryDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
