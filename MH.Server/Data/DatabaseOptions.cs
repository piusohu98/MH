using Microsoft.Extensions.Configuration;

namespace MH.Server.Data;

public static class DatabaseOptions
{
    private const string DatabasePathKey = "Database:Path";

    public static string ResolvePath(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredPath = configuration[DatabasePathKey];
        return string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MHMarket",
                "data",
                "market.db")
            : configuredPath.Trim();
    }
}
