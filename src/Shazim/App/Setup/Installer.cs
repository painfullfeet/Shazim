using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Shazim.App.Services;

namespace Shazim.App.Setup;

public enum SetupMode { None, Install, Uninstall }

/// <summary>
/// Per-user install of the single-file Shazim.exe (no admin rights):
/// %LOCALAPPDATA%\Programs\Shazim, Start menu / desktop shortcuts and an "Apps &amp; features" entry.
/// The same executable is the installer (named Shazim-Setup*.exe or run with --setup)
/// and the uninstaller (--uninstall).
/// </summary>
public static class Installer
{
    public const string SetupArg = "--setup";
    public const string UninstallArg = "--uninstall";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Shazim";

    public static string InstallDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Shazim");
    public static string InstalledExe => Path.Combine(InstallDir, "Shazim.exe");
    private static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Shazim.lnk");
    private static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Shazim.lnk");

    private static string CurrentExe => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

    public static SetupMode ModeFor(string[] args)
    {
        if (args.Any(a => a.Equals(UninstallArg, StringComparison.OrdinalIgnoreCase))) return SetupMode.Uninstall;
        if (args.Any(a => a.Equals(SetupArg, StringComparison.OrdinalIgnoreCase))) return SetupMode.Install;
        var name = Path.GetFileNameWithoutExtension(CurrentExe);
        return name.Contains("setup", StringComparison.OrdinalIgnoreCase) ? SetupMode.Install : SetupMode.None;
    }

    public static bool IsInstalled => File.Exists(InstalledExe);
    public static bool DesktopShortcutExists => File.Exists(DesktopShortcut);

    public static string Version
    {
        get
        {
            var v = typeof(Installer).Assembly.GetName().Version ?? new Version(1, 0);
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static long PackageSizeBytes
    {
        get { try { return new FileInfo(CurrentExe).Length; } catch { return 0; } }
    }

    // ===== Install =====

    public static async Task InstallAsync(bool desktopShortcut, IProgress<(double Value, string Key)> progress)
    {
        progress.Report((0.02, "setup.step_close"));
        await Task.Run(StopRunningCopies);

        progress.Report((0.08, "setup.step_copy"));
        Directory.CreateDirectory(InstallDir);
        await Task.Run(() => CopySelf(p => progress.Report((0.08 + p * 0.72, "setup.step_copy"))));

        progress.Report((0.82, "setup.step_clean"));
        await Task.Run(CleanOldFiles);

        progress.Report((0.88, "setup.step_shortcuts"));
        await Task.Run(() =>
        {
            CreateShortcut(StartMenuShortcut);
            if (desktopShortcut) CreateShortcut(DesktopShortcut);
            else TryDelete(DesktopShortcut);
        });

        progress.Report((0.95, "setup.step_register"));
        Register();

        // Keep an existing "start with Windows" entry pointing at the installed copy.
        if (StartupService.IsEnabled()) StartupService.SetPath(InstalledExe);

        await Task.Delay(250);
        progress.Report((1.0, "setup.step_done"));
    }

    public static void LaunchInstalled()
    {
        try { Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir }); }
        catch { /* the user can start it from the Start menu */ }
    }

    private static void CopySelf(Action<double> onProgress)
    {
        var source = CurrentExe;
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase))
        {
            onProgress(1);
            return;
        }

        var temp = InstalledExe + ".new";
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[1 << 20];
            long total = Math.Max(1, input.Length), done = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                done += read;
                onProgress((double)done / total);
            }
        }

        if (File.Exists(InstalledExe))
        {
            try { File.Delete(InstalledExe); }
            catch
            {
                // Still locked: move it aside, it is removed on the next install.
                File.Move(InstalledExe, InstalledExe + ".old-" + Environment.TickCount64, overwrite: true);
            }
        }
        File.Move(temp, InstalledExe);
    }

    /// <summary>Removes files of older multi-file installs (DLLs, scripts...), keeping Shazim.exe.</summary>
    private static void CleanOldFiles()
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(InstallDir))
        {
            if (string.Equals(entry, InstalledExe, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
            }
            catch { /* in use: harmless, tried again next time */ }
        }
    }

    private static void StopRunningCopies()
    {
        int self = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("Shazim"))
        {
            using (p)
            {
                if (p.Id == self) continue;
                try
                {
                    // Only the installed copy holds the file we replace; leave other copies alone.
                    var path = p.MainModule?.FileName;
                    if (path == null || !path.StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase)) continue;
                    p.Kill();
                    p.WaitForExit(4000);
                }
                catch { }
            }
        }
    }

    private static void CreateShortcut(string path)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = InstalledExe;
            link.WorkingDirectory = InstallDir;
            link.IconLocation = InstalledExe + ",0";
            link.Description = "Shazim";
            link.Save();
        }
        catch { /* shortcuts are a convenience */ }
    }

    private static void Register()
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "Shazim");
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", "Painful Fit");
        key.SetValue("DisplayIcon", InstalledExe + ",0");
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("UninstallString", $"\"{InstalledExe}\" {UninstallArg}");
        key.SetValue("QuietUninstallString", $"\"{InstalledExe}\" {UninstallArg} --quiet");
        key.SetValue("URLInfoAbout", Views.SettingsView.RepositoryUrl);
        key.SetValue("HelpLink", Views.SettingsView.RepositoryUrl + "#readme");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(PackageSizeBytes / 1024), RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
    }

    // ===== Uninstall =====

    public static async Task UninstallAsync(bool removeData, IProgress<(double Value, string Key)> progress)
    {
        progress.Report((0.1, "setup.step_close"));
        await Task.Run(StopRunningCopies);

        progress.Report((0.4, "setup.step_shortcuts_remove"));
        TryDelete(StartMenuShortcut);
        TryDelete(DesktopShortcut);
        try { StartupService.Apply(false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); } catch { }

        if (removeData)
        {
            progress.Report((0.7, "setup.step_data_remove"));
            await Task.Run(() =>
            {
                try { if (Directory.Exists(Core.AppPaths.DataDirectory)) Directory.Delete(Core.AppPaths.DataDirectory, true); } catch { }
            });
        }

        await Task.Delay(250);
        progress.Report((1.0, "setup.step_removed"));
    }

    /// <summary>
    /// The running exe cannot delete itself: a short-lived hidden cmd removes the
    /// install folder once this process has exited.
    /// </summary>
    public static void ScheduleFolderRemoval()
    {
        if (!Directory.Exists(InstallDir)) return;
        try
        {
            var cmd = $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{InstallDir}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", cmd)
            {
                CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetTempPath()
            });
        }
        catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
