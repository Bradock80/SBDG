using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SGDB.Services;

/// <summary>
/// Cria/atualiza o atalho "SGDB" na área de trabalho apontando para o executável atual.
/// </summary>
public static class DesktopShortcutService
{
    public static void EnsureDesktopShortcut()
    {
        try
        {
            var exe = ResolveExePath();
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return;

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
                desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
                return;

            var lnkPath = Path.Combine(desktop, "SGDB.lnk");
            var workDir = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
            var iconPath = ResolveIconPath(workDir, exe);

            if (File.Exists(lnkPath) && ShortcutPointsTo(lnkPath, exe))
                return;

            CreateShortcut(lnkPath, exe, workDir, iconPath);
        }
        catch
        {
            // atalho é opcional — nunca derruba o app
        }
    }

    private static string? ResolveExePath()
    {
        try
        {
            var p = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(p) && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return p;
        }
        catch { /* ignore */ }

        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveIconPath(string workDir, string exe)
    {
        var candidates = new[]
        {
            Path.Combine(workDir, "Assets", "app.ico"),
            Path.Combine(workDir, "app.ico"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }
        return exe;
    }

    private static bool ShortcutPointsTo(string lnkPath, string exe)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return false;
            var shell = Activator.CreateInstance(shellType);
            if (shell is null)
                return false;
            var shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                [lnkPath]);
            if (shortcut is null)
                return false;
            var target = shortcut.GetType().InvokeMember(
                "TargetPath",
                BindingFlags.GetProperty,
                null,
                shortcut,
                null) as string;
            TryReleaseCom(shortcut);
            TryReleaseCom(shell);
            return !string.IsNullOrWhiteSpace(target)
                   && string.Equals(
                       Path.GetFullPath(target),
                       Path.GetFullPath(exe),
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void CreateShortcut(string lnkPath, string exe, string workDir, string iconPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell indisponível.");
        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Não foi possível criar WScript.Shell.");
        var shortcut = shellType.InvokeMember(
            "CreateShortcut",
            BindingFlags.InvokeMethod,
            null,
            shell,
            [lnkPath])
            ?? throw new InvalidOperationException("Não foi possível criar o atalho.");

        var iconLocation = iconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
            ? iconPath
            : exe + ",0";

        SetProp(shortcut, "TargetPath", exe);
        SetProp(shortcut, "WorkingDirectory", workDir);
        SetProp(shortcut, "IconLocation", iconLocation);
        SetProp(shortcut, "Description", "SGDB — Gestão de Depósito");
        shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);

        TryReleaseCom(shortcut);
        TryReleaseCom(shell);
    }

    private static void SetProp(object target, string name, object value) =>
        target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, [value]);

    private static void TryReleaseCom(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            try { Marshal.FinalReleaseComObject(com); }
            catch { /* ignore */ }
        }
    }
}
