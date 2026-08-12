using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using SGDB.Views;

namespace SGDB.Services;

/// <summary>
/// Atualização via GitHub Releases (Bradock80/SBDG).
/// UI gráfica (sem CMD). Não altera o banco em %LocalAppData%\SGDB.
/// Estratégia comercial: copia para pasta nova (staging) e troca a pasta inteira
/// — evita falha por arquivo travado (robocopy 8/9).
/// </summary>
public static class AutoUpdateService
{
    public const string ApplyUpdateArg = "--apply-update";
    private const string JobFileName = "sgdb-update-job.json";

    private const string LatestReleaseUrl =
        "https://api.github.com/repos/Bradock80/SBDG/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SGDB-AutoUpdate/1.0");
        c.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>
    /// Modo atualizador: roda a partir da pasta extraída (temp), troca a pasta de instalação e reinicia.
    /// </summary>
    public static bool TryHandleApplyUpdateArgs(string[] args)
    {
        var wantApply = args.Any(a => string.Equals(a, ApplyUpdateArg, StringComparison.OrdinalIgnoreCase));
        if (!wantApply)
            return false;

        var job = TryReadJobFile(AppContext.BaseDirectory);
        var target = GetArgValue(args, "--target") ?? job?.Target;
        var pidText = GetArgValue(args, "--pid") ?? job?.Pid.ToString();
        var zipPath = GetArgValue(args, "--zip") ?? job?.Zip;
        var extractPath = GetArgValue(args, "--extract") ?? job?.Extract;

        if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
        {
            MessageBox.Show(
                "Pasta de instalação inválida para atualização.\n\n" +
                $"Target: {target ?? "(vazio)"}\n" +
                "Tente atualizar pelo pendrive (pasta SGDB_Para_Outro_PC).",
                "SGDB — Atualização",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Environment.Exit(1);
            return true;
        }

        _ = int.TryParse(pidText, out var waitPid);

        var splash = new UpdateSplashWindow { Topmost = true };
        System.Windows.Application.Current.MainWindow = splash;
        splash.Show();
        splash.Activate();
        splash.SetProgress(5, "Aguardando o SGDB fechar…");

        _ = ApplyUpdateAsync(splash, target, waitPid, zipPath, extractPath);
        return true;
    }

    private sealed class UpdateJob
    {
        public string? Target { get; set; }
        public int Pid { get; set; }
        public string? Zip { get; set; }
        public string? Extract { get; set; }
    }

    private static UpdateJob? TryReadJobFile(string dir)
    {
        try
        {
            var path = Path.Combine(dir, JobFileName);
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<UpdateJob>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private static void WriteJobFile(string dir, string target, int pid, string zip, string extract)
    {
        var path = Path.Combine(dir, JobFileName);
        var json = JsonSerializer.Serialize(new UpdateJob
        {
            Target = target,
            Pid = pid,
            Zip = zip,
            Extract = extract,
        });
        File.WriteAllText(path, json);
    }

    private static async Task ApplyUpdateAsync(
        UpdateSplashWindow splash,
        string targetDir,
        int waitPid,
        string? zipPath,
        string? extractPath)
    {
        try
        {
            await WaitForProcessExitAsync(waitPid, splash).ConfigureAwait(true);

            splash.SetProgress(18, "Liberando arquivos…");
            await Task.Run(ForceKillOtherSgdbInstances).ConfigureAwait(true);
            await Task.Delay(1500).ConfigureAwait(true);

            splash.SetProgress(20, "Preparando arquivos…");

            var sourceDir = Path.GetFullPath(AppContext.BaseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            targetDir = Path.GetFullPath(targetDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(sourceDir, targetDir, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "O atualizador não pode rodar de dentro da pasta de instalação.");

            splash.SetIndeterminate("Instalando versão nova (troca de pasta)…");
            var finalDir = await Task.Run(() => InstallByFolderSwap(sourceDir, targetDir))
                .ConfigureAwait(true);

            splash.SetProgress(92, "Finalizando…");
            await Task.Delay(200).ConfigureAwait(true);

            var exe = Path.Combine(finalDir, "SGDB.exe");
            if (!File.Exists(exe))
                throw new FileNotFoundException(
                    "SGDB.exe não encontrado após a atualização.\n" +
                    $"Origem: {sourceDir}\nDestino: {finalDir}", exe);

            try
            {
                var jobInTarget = Path.Combine(finalDir, JobFileName);
                if (File.Exists(jobInTarget))
                    File.Delete(jobInTarget);
            }
            catch { /* ignore */ }

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = finalDir,
                UseShellExecute = true,
            });

            splash.SetProgress(100, "Concluído!");
            await Task.Delay(600).ConfigureAwait(true);

            TryDeleteQuiet(zipPath);
            ScheduleExtractCleanup(extractPath ?? sourceDir);

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SGDB", "update.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] APPLY: {ex}\n\n");
            }
            catch { /* ignore */ }

            MessageBox.Show(
                splash,
                $"Falha ao aplicar a atualização:\n\n{ex.Message}\n\n" +
                "Solução rápida: feche o SGDB e use o pendrive\n" +
                "(pasta SGDB_Para_Outro_PC → ATUALIZAR_NESTE_PC.bat).\n\n" +
                $"Detalhes: {logPath}",
                "SGDB — Atualização",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Verifica update em background.
    /// notifyResult=true: mostra mensagem se já está atualizado ou se falhou a consulta (menu manual).
    /// </summary>
    public static async Task CheckAndOfferUpdateAsync(Window? owner = null, bool notifyResult = false)
    {
        try
        {
            RemoteRelease? remote;
            try
            {
                remote = await FetchLatestAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (notifyResult)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        MessageBox.Show(
                            owner,
                            "Não foi possível consultar o GitHub.\n\n" +
                            $"{ex.Message}\n\n" +
                            "Verifique a internet deste PC (o notebook usa a rede dele, não a da loja).",
                            "SGDB — Atualização",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning));
                }
                return;
            }

            if (remote is null)
            {
                if (notifyResult)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        MessageBox.Show(
                            owner,
                            "Não encontrei uma release no GitHub (Bradock80/SBDG).\n\n" +
                            "Confira se a versão foi publicada e se este PC tem internet.",
                            "SGDB — Atualização",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning));
                }
                return;
            }

            var current = GetCurrentVersion();
            if (!IsNewer(remote.Version, current))
            {
                if (notifyResult)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        MessageBox.Show(
                            owner,
                            $"Você já está na versão mais recente.\n\n" +
                            $"Versão atual: {FormatVersion(current)}\n" +
                            $"GitHub: {FormatVersion(remote.Version)}",
                            "SGDB — Atualização",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information));
                }
                return;
            }

            var accept = false;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var msg =
                    $"Há uma nova versão do SGDB.\n\n" +
                    $"Versão atual: {FormatVersion(current)}\n" +
                    $"Nova versão: {FormatVersion(remote.Version)}\n\n" +
                    (string.IsNullOrWhiteSpace(remote.Name) ? "" : $"{remote.Name}\n\n") +
                    "Deseja baixar e instalar agora?\n\n" +
                    "O banco de dados e as configurações (Rede Loja etc.) não serão apagados.";
                accept = MessageBox.Show(
                    owner,
                    msg,
                    "SGDB — Atualização",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information) == MessageBoxResult.Yes;
            });

            if (!accept)
                return;

            UpdateSplashWindow? splash = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                splash = new UpdateSplashWindow { Topmost = true };
                System.Windows.Application.Current.MainWindow = splash;
                splash.Show();
                splash.Activate();
                HideAllWindowsExcept(splash);
                splash.SetProgress(2, "Baixando do GitHub… (~70 MB, aguarde)");
            });

            if (splash is null)
                return;

            try
            {
                var zipPath = await DownloadZipAsync(remote.ZipUrl, splash).ConfigureAwait(false);

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    splash.SetIndeterminate("Extraindo arquivos…"));

                var extractDir = Path.Combine(
                    Path.GetTempPath(),
                    "SGDB-update-extract-" + Guid.NewGuid().ToString("N"));

                await Task.Run(() =>
                {
                    if (Directory.Exists(extractDir))
                        Directory.Delete(extractDir, true);
                    Directory.CreateDirectory(extractDir);
                    ZipFile.ExtractToDirectory(zipPath, extractDir);
                }).ConfigureAwait(false);

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    splash.SetProgress(75, "Extração concluída."));

                var sourceDir = FindAppRoot(extractDir)
                    ?? throw new InvalidOperationException("SGDB.exe não encontrado no pacote baixado.");

                var installDir = Path.GetFullPath(AppContext.BaseDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var updaterExe = Path.Combine(sourceDir, "SGDB.exe");
                if (!File.Exists(updaterExe))
                    throw new FileNotFoundException("Instalador não encontrado após extrair.", updaterExe);

                var pid = Environment.ProcessId;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    splash.SetProgress(85, "Abrindo atualizador…"));

                WriteJobFile(sourceDir, installDir, pid, zipPath, extractDir);

                var psi = new ProcessStartInfo
                {
                    FileName = updaterExe,
                    WorkingDirectory = sourceDir,
                    UseShellExecute = false,
                };
                psi.ArgumentList.Add(ApplyUpdateArg);
                psi.ArgumentList.Add("--target");
                psi.ArgumentList.Add(installDir);
                psi.ArgumentList.Add("--pid");
                psi.ArgumentList.Add(pid.ToString());
                psi.ArgumentList.Add("--zip");
                psi.ArgumentList.Add(zipPath);
                psi.ArgumentList.Add("--extract");
                psi.ArgumentList.Add(extractDir);

                _ = Process.Start(psi)
                    ?? throw new InvalidOperationException("Não foi possível iniciar o atualizador.");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    splash.SetProgress(92, "Fechando o programa…"));

                await Task.Delay(250).ConfigureAwait(false);
                ForceExitForUpdate();
            }
            catch (Exception ex)
            {
                try
                {
                    var logDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "SGDB");
                    Directory.CreateDirectory(logDir);
                    File.AppendAllText(
                        Path.Combine(logDir, "update.log"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DOWNLOAD: {ex}\n\n");
                }
                catch { /* ignore */ }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try { splash.Close(); } catch { /* ignore */ }
                    MessageBox.Show(
                        owner,
                        $"Não foi possível baixar/aplicar a atualização:\n\n{ex.Message}\n\n" +
                        "Se o erro continuar, use o pendrive (SGDB_Para_Outro_PC) ou tente de novo com internet estável.",
                        "SGDB — Atualização",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
            }
        }
        catch (Exception ex)
        {
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SGDB");
                Directory.CreateDirectory(logDir);
                File.AppendAllText(
                    Path.Combine(logDir, "update.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] CHECK: {ex}\n\n");
            }
            catch { /* ignore */ }
        }
    }

    /// <summary>Mata o processo atual sem passar por Closing/backup (atualização).</summary>
    private static void ForceExitForUpdate()
    {
        try
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                try { System.Windows.Application.Current.Shutdown(0); } catch { /* ignore */ }
            });
        }
        catch { /* ignore */ }

        Environment.Exit(0);
    }

    public static Version GetCurrentVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var clean = info.Split('+')[0].Trim().TrimStart('v', 'V');
            if (Version.TryParse(NormalizeVersion(clean), out var v))
                return v;
        }

        return asm.GetName().Version ?? new Version(0, 1, 0);
    }

    private static async Task<RemoteRelease?> FetchLatestAsync()
    {
        using var res = await Http.GetAsync(LatestReleaseUrl).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
            return null;

        await using var stream = await res.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var versionText = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(NormalizeVersion(versionText), out var version))
            return null;

        string? zipUrl = null;
        string? zipName = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                    continue;
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;
                zipUrl = url;
                zipName = name;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(zipUrl))
            return null;

        var releaseName = root.TryGetProperty("name", out var rn) ? rn.GetString() : tag;
        return new RemoteRelease(version, releaseName ?? tag, zipUrl, zipName ?? "SGDB-update.zip");
    }

    private static async Task<string> DownloadZipAsync(string url, UpdateSplashWindow splash)
    {
        var zipPath = Path.Combine(
            Path.GetTempPath(),
            $"SGDB-update-{DateTime.Now:yyyyMMddHHmmss}.zip");

        using var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);
        res.EnsureSuccessStatusCode();

        var total = res.Content.Headers.ContentLength ?? -1L;
        await using var input = await res.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using var output = File.Create(zipPath);

        var buffer = new byte[256 * 1024];
        long readTotal = 0;
        int read;
        var lastUi = 0.0;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            readTotal += read;
            if (total > 0)
            {
                var pct = 2 + readTotal * 58.0 / total;
                if (pct - lastUi >= 1.0 || readTotal >= total)
                {
                    lastUi = pct;
                    splash.SetProgress(pct,
                        $"Baixando… {readTotal / 1024 / 1024:0} / {total / 1024 / 1024:0} MB");
                }
            }
            else if (readTotal - (long)lastUi > 4_000_000)
            {
                lastUi = readTotal;
                splash.SetIndeterminate($"Baixando… {readTotal / 1024 / 1024:0} MB");
            }
        }

        splash.SetProgress(60, "Download concluído.");
        return zipPath;
    }

    private static void HideAllWindowsExcept(Window keep)
    {
        foreach (Window w in System.Windows.Application.Current.Windows)
        {
            if (ReferenceEquals(w, keep))
                continue;
            try { w.Hide(); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Copia a versão nova para uma pasta vazia e troca com a instalação atual.
    /// Assim não sobrescreve EXE/DLL em uso (causa do robocopy código 8/9).
    /// </summary>
    private static string InstallByFolderSwap(string sourceDir, string targetDir)
    {
        ForceKillOtherSgdbInstances();
        Thread.Sleep(1200);

        var parent = Path.GetDirectoryName(targetDir)
            ?? throw new InvalidOperationException("Pasta de instalação inválida.");
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var staging = Path.Combine(parent, "SGDB_staging_" + stamp);
        var backup = Path.Combine(parent, "SGDB_backup_" + stamp);

        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);

        // Pasta nova vazia: cópia sem conflito com antivírus/arquivos da instalação antiga
        CopyDirectoryFresh(sourceDir, staging);
        TryDeleteQuiet(Path.Combine(staging, JobFileName));

        if (!File.Exists(Path.Combine(staging, "SGDB.exe")))
            throw new InvalidOperationException("Pacote incompleto: SGDB.exe ausente na preparação.");

        var installedAt = targetDir;
        var movedOldAway = false;

        try
        {
            if (Directory.Exists(targetDir))
            {
                if (Directory.Exists(backup))
                    Directory.Delete(backup, recursive: true);

                for (var attempt = 0; attempt < 8; attempt++)
                {
                    try
                    {
                        ForceKillOtherSgdbInstances();
                        Directory.Move(targetDir, backup);
                        movedOldAway = true;
                        break;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Thread.Sleep(500);
                        if (attempt == 7)
                            throw;
                    }
                }
            }

            Directory.Move(staging, targetDir);
            installedAt = targetDir;
        }
        catch (Exception swapEx)
        {
            // Se a pasta antiga não pôde ser renomeada: usa a staging e atualiza o atalho
            if (Directory.Exists(staging) && File.Exists(Path.Combine(staging, "SGDB.exe")))
            {
                installedAt = staging;
            }
            else if (movedOldAway && Directory.Exists(backup) && !Directory.Exists(targetDir))
            {
                try { Directory.Move(backup, targetDir); } catch { /* ignore */ }
                throw new InvalidOperationException(
                    "Não foi possível concluir a troca de pasta.\n" + swapEx.Message,
                    swapEx);
            }
            else
            {
                throw new InvalidOperationException(
                    "Não foi possível instalar a nova versão.\n" + swapEx.Message,
                    swapEx);
            }
        }

        UpdateDesktopShortcut(installedAt);
        if (movedOldAway && Directory.Exists(backup))
            ScheduleDirectoryCleanup(backup);

        return installedAt;
    }

    private static void ForceKillOtherSgdbInstances()
    {
        foreach (var p in Process.GetProcessesByName("SGDB"))
        {
            try
            {
                if (p.Id == Environment.ProcessId)
                    continue;
                if (!p.HasExited)
                    p.Kill(entireProcessTree: true);
                try { p.WaitForExit(3000); } catch { /* ignore */ }
            }
            catch { /* ignore */ }
            finally { p.Dispose(); }
        }
    }

    private static void CopyDirectoryFresh(string sourceDir, string targetDir)
    {
        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        if (files.Length == 0)
            throw new InvalidOperationException("Pacote de atualização vazio.");

        Directory.CreateDirectory(targetDir);
        foreach (var src in files)
        {
            var name = Path.GetFileName(src);
            if (string.Equals(name, JobFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.EndsWith(".old", StringComparison.OrdinalIgnoreCase))
                continue;

            var rel = Path.GetRelativePath(sourceDir, src);
            var dest = Path.Combine(targetDir, rel);
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            Exception? last = null;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    File.Copy(src, dest, overwrite: true);
                    last = null;
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    last = ex;
                    Thread.Sleep(250);
                }
            }

            if (last is not null)
                throw last;
        }
    }

    private static void UpdateDesktopShortcut(string appDir)
    {
        try
        {
            var exe = Path.Combine(appDir, "SGDB.exe");
            if (!File.Exists(exe))
                return;

            var ico = Path.Combine(appDir, "Assets", "app.ico");
            var iconLocation = File.Exists(ico) ? ico : exe + ",0";
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
                desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (string.IsNullOrWhiteSpace(desktop))
                return;

            var lnk = Path.Combine(desktop, "SGDB.lnk");
            var ps =
                "$ErrorActionPreference='Stop';" +
                "$ws = New-Object -ComObject WScript.Shell;" +
                "$s = $ws.CreateShortcut([string]$env:SGDB_LNK);" +
                "$s.TargetPath = [string]$env:SGDB_EXE;" +
                "$s.WorkingDirectory = [string]$env:SGDB_DIR;" +
                "$s.IconLocation = [string]$env:SGDB_ICO;" +
                "$s.Description = 'SGDB — Gestao de Deposito';" +
                "$s.Save();";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " + ps,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            psi.Environment["SGDB_LNK"] = lnk;
            psi.Environment["SGDB_EXE"] = exe;
            psi.Environment["SGDB_DIR"] = appDir;
            psi.Environment["SGDB_ICO"] = iconLocation;

            using var p = Process.Start(psi);
            p?.WaitForExit(15000);
        }
        catch
        {
            // atalho é conveniente; a pasta nova já funciona abrindo o EXE
        }
    }

    private static void ScheduleDirectoryCleanup(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return;

        try
        {
            var q = dir.Replace("\"", "");
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c timeout /t 8 /nobreak >nul & rd /s /q \"{q}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch
        {
            // limpeza opcional
        }
    }

    private static async Task WaitForProcessExitAsync(int waitPid, UpdateSplashWindow splash)
    {
        const int timeoutMs = 12_000;

        if (waitPid <= 0)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                var others = Process.GetProcessesByName("SGDB")
                    .Where(p => p.Id != Environment.ProcessId)
                    .ToList();
                if (others.Count == 0)
                    break;

                foreach (var p in others)
                {
                    try
                    {
                        if (sw.ElapsedMilliseconds > 5_000 && !p.HasExited)
                            p.Kill(entireProcessTree: true);
                    }
                    catch { /* ignore */ }
                    finally { p.Dispose(); }
                }

                splash.SetProgress(5 + Math.Min(10, (int)(sw.ElapsedMilliseconds / 1000)), "Aguardando o SGDB fechar…");
                await Task.Delay(300).ConfigureAwait(true);
            }

            await Task.Delay(800).ConfigureAwait(true);
            return;
        }

        try
        {
            using var proc = Process.GetProcessById(waitPid);
            splash.SetProgress(8, "Aguardando o SGDB fechar…");
            var exited = await WaitForExitOrTimeoutAsync(proc, timeoutMs, splash).ConfigureAwait(true);
            if (!exited && !proc.HasExited)
            {
                splash.SetProgress(12, "Encerrando o SGDB antigo…");
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                try { await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true); }
                catch { /* ignore */ }
            }
        }
        catch (ArgumentException)
        {
            // já encerrou
        }

        await Task.Delay(800).ConfigureAwait(true);
        splash.SetProgress(15, "SGDB fechado.");
    }

    private static async Task<bool> WaitForExitOrTimeoutAsync(
        Process proc, int timeoutMs, UpdateSplashWindow splash)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (proc.HasExited)
                return true;
            splash.SetProgress(
                8 + Math.Min(6, sw.ElapsedMilliseconds / 3000.0),
                "Aguardando o SGDB fechar…");
            try
            {
                await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(true);
                return true;
            }
            catch (TimeoutException)
            {
                // continua
            }
        }

        return proc.HasExited;
    }

    private static string? FindAppRoot(string extractDir)
    {
        var direct = Path.Combine(extractDir, "SGDB.exe");
        if (File.Exists(direct))
            return extractDir;

        var nested = Path.Combine(extractDir, "SGDB", "SGDB.exe");
        if (File.Exists(nested))
            return Path.Combine(extractDir, "SGDB");

        var found = Directory.GetFiles(extractDir, "SGDB.exe", SearchOption.AllDirectories).FirstOrDefault();
        return found is null ? null : Path.GetDirectoryName(found);
    }

    private static void ScheduleExtractCleanup(string? extractDir)
    {
        if (string.IsNullOrWhiteSpace(extractDir) || !Directory.Exists(extractDir))
            return;

        try
        {
            var q = extractDir.Replace("\"", "");
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c timeout /t 4 /nobreak >nul & rd /s /q \"{q}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch
        {
            // limpeza opcional
        }
    }

    private static void TryDeleteQuiet(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch { /* ignore */ }
    }

    private static string? GetArgValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1].Trim('"');
        }

        return null;
    }

    public static bool IsNewer(Version remote, Version local)
    {
        var r = Normalize(remote);
        var l = Normalize(local);
        return r > l;
    }

    private static Version Normalize(Version v) =>
        new(Math.Max(0, v.Major), Math.Max(0, v.Minor),
            v.Build < 0 ? 0 : v.Build, v.Revision < 0 ? 0 : v.Revision);

    private static string NormalizeVersion(string text)
    {
        var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "0.0.0";
        if (parts.Length == 1) return parts[0] + ".0.0";
        if (parts.Length == 2) return parts[0] + "." + parts[1] + ".0";
        return string.Join('.', parts.Take(4));
    }

    private static string FormatVersion(Version v) =>
        v.Build >= 0 ? $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}" : $"{v.Major}.{v.Minor}";

    private sealed record RemoteRelease(Version Version, string Name, string ZipUrl, string ZipName);
}
