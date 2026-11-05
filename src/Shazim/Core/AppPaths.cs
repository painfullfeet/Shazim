namespace Shazim.Core;

/// <summary>
/// Where Shazim keeps its data: %LOCALAPPDATA%\Shazim, or the folder named by the
/// SHAZIM_DATA_DIR environment variable (portable use, demos, tests).
/// </summary>
public static class AppPaths
{
    public const string AppName = "Shazim";
    private const string LegacyFolder = "ShazamDesktop";

    public static bool IsCustomDataDirectory { get; } =
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SHAZIM_DATA_DIR"));

    public static string DataDirectory { get; } = IsCustomDataDirectory
        ? Path.GetFullPath(Environment.GetEnvironmentVariable("SHAZIM_DATA_DIR")!)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
    public static string ArtDirectory => Path.Combine(DataDirectory, "art");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string DatabaseFile => Path.Combine(DataDirectory, "shazim.db");

    /// <summary>
    /// Moves the data of the former "Shazam Desktop" name to the new folder, once.
    /// </summary>
    public static void MigrateLegacyData()
    {
        if (IsCustomDataDirectory) return;
        try
        {
            var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyFolder);
            if (!Directory.Exists(legacy) || Directory.Exists(DataDirectory)) return;

            Directory.Move(legacy, DataDirectory);
            var oldDb = Path.Combine(DataDirectory, "shazam.db");
            if (File.Exists(oldDb) && !File.Exists(DatabaseFile))
            {
                File.Move(oldDb, DatabaseFile);
                foreach (var suffix in new[] { "-wal", "-shm" })
                    if (File.Exists(oldDb + suffix)) File.Move(oldDb + suffix, DatabaseFile + suffix);
            }
        }
        catch
        {
            // Another process holds the files: start fresh rather than fail.
        }
    }
}
