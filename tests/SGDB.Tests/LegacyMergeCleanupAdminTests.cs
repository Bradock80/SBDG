using System.IO;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69T-F — UI administrativa de saneamento. Bancos isolados; nunca AppData/deposito.db.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class LegacyMergeCleanupAdminTests
{
    private const string EanKeep = "7891000001001";
    private const string PackKeep = "7891000001023";
    private const string EanAbsorb = "7891000002002";

    private static TempDatabase Begin()
    {
        LegacyMergedProductStockCleanupService.TestBeforeWriteAudit = null;
        ProductService.TestBeforeApplyMergeCost = null;
        ProductService.TestAfterRemapProductIds = null;
        BackupService.TestBeforeConsistentSnapshot = null;
        LegacyMergeCleanupAdminService.ResetSession();
        Environment.SetEnvironmentVariable(DatabaseService.DatabasePathEnvVar, null);
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        return db;
    }

    [Fact]
    public void AdminAcessaModulo()
    {
        using var _ = Begin();
        TestDataHelper.SetSessionRole("admin");
        Assert.True(LegacyMergeCleanupAdminService.CanAccess());
        Assert.True(AccessControl.CanAccessModule(LegacyMergeCleanupRules.ModuleId));
    }

    [Fact]
    public void GestorAcessaModulo()
    {
        using var _ = Begin();
        TestDataHelper.SetSessionRole("gestor");
        Assert.True(LegacyMergeCleanupAdminService.CanAccess());
    }

    [Fact]
    public void AdminCustomizadoSemFlagsDeRelatorioAindaAcessa()
    {
        using var _ = Begin();
        TestDataHelper.SetSessionCustomPermissions("admin", p =>
        {
            p.RelatoriosAcesso = false;
            p.SistemaUsuarios = false;
        });
        Assert.True(AccessControl.CanAccessLegacyMergeCleanup());
        Assert.True(LegacyMergeCleanupAdminService.CanAccess());
    }

    [Fact]
    public void GestorSemRelatoriosAindaAcessa()
    {
        using var _ = Begin();
        TestDataHelper.SetSessionCustomPermissions("gestor", p => p.RelatoriosAcesso = false);
        Assert.True(AccessControl.CanAccessLegacyMergeCleanup());
        Assert.True(LegacyMergeCleanupAdminService.CanAccess());
    }

    [Fact]
    public void VendedorComRelatoriosContinuaBloqueado()
    {
        using var _ = Begin();
        TestDataHelper.SetSessionCustomPermissions("vendedor", p => p.RelatoriosAcesso = true);
        Assert.False(AccessControl.CanAccessLegacyMergeCleanup());
        Assert.False(LegacyMergeCleanupAdminService.CanAccess());
    }

    [Fact]
    public void VendedorBloqueado()
    {
        using var _ = Begin();
        TestDataHelper.SetSessionRole("vendedor");
        Assert.False(LegacyMergeCleanupAdminService.CanAccess());
        var ex = Assert.Throws<InvalidOperationException>(
            () => LegacyMergeCleanupAdminService.EnsureAccess());
        Assert.Equal(LegacyMergeCleanupRules.AccessDeniedMessage, ex.Message);
        Assert.Throws<InvalidOperationException>(
            () => LegacyMergeCleanupAdminService.ExecuteProven(true));
    }

    [Fact]
    public void ListaSomenteCandidatosDoServico()
    {
        using var _ = Begin();
        var (keep, absorb) = SeedLeftover(10, 1, 2, 1, 3, 1, 7, 3, 10);
        var extra = SeedProduct("X9", "FORA DA LISTA", 50, 1, 2, active: false);
        var listed = LegacyMergeCleanupAdminService.ListCandidates();
        Assert.Contains(listed, c => c.AbsorbId == absorb && c.KeepId == keep);
        Assert.DoesNotContain(listed, c => c.AbsorbId == extra);
    }

    [Fact]
    public void RevisarNaoEntraNoAutomatico()
    {
        using var _ = Begin();
        var (keep, absorb) = SeedLeftover(10, 1, 2, 1, 3, 1, 7, 3, 10);
        var badAbs = SeedRevisarConflict();

        Assert.Contains(LegacyMergeCleanupAdminService.ListExecutable(),
            c => c.AbsorbId == absorb && c.UiStatus == LegacyMergeCleanupUiStatus.Comprovado);
        Assert.DoesNotContain(LegacyMergeCleanupAdminService.ListExecutable(),
            c => c.AbsorbId == badAbs);
        Assert.Contains(LegacyMergeCleanupAdminService.ListManualReview(),
            c => c.AbsorbId == badAbs && c.UiStatus == LegacyMergeCleanupUiStatus.Revisar);
        Assert.DoesNotContain(LegacyMergeCleanupAdminService.ListAutomatic(),
            c => c.AbsorbId == badAbs);
    }

    [Fact]
    public void SemBackupExecucaoBloqueada()
    {
        using var _ = Begin();
        SeedLeftover(10, 1, 2, 1, 3, 1, 7, 3, 10);
        var ex = Assert.Throws<InvalidOperationException>(
            () => LegacyMergeCleanupAdminService.ExecuteProven(true));
        Assert.Equal(LegacyMergeCleanupRules.BackupRequiredMessage, ex.Message);
    }

    [Fact]
    public void BackupValidoHabilita()
    {
        using var db = Begin();
        SeedLeftover(10, 1, 2, 1, 3, 1, 7, 3, 10);
        Assert.False(LegacyMergeCleanupAdminService.HasValidSessionBackup);
        var info = LegacyMergeCleanupAdminService.CreateRequiredBackup();
        Assert.True(info.IsValid);
        Assert.True(LegacyMergeCleanupAdminService.HasValidSessionBackup);
        Assert.True(File.Exists(info.BackupPath));
        Assert.True(info.BackupSize > 0);
        var validation = BackupService.ValidateArchive(info.BackupPath);
        Assert.True(validation.ZipOpens);
        Assert.True(validation.HasDepositoDb);
        Assert.StartsWith(Path.GetFullPath(Path.GetDirectoryName(db.DatabasePath)!),
            Path.GetFullPath(info.BackupPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackupUsaBancoEfetivoNaoPadrao()
    {
        using var db = Begin();
        var token = "TOKEN_BACKUP_EFETIVO_" + Guid.NewGuid().ToString("N")[..8];
        var productId = SeedProduct("TOK", token, 1, 1, 2, active: true);
        Assert.True(productId > 0);
        var info = LegacyMergeCleanupAdminService.CreateRequiredBackup();

        Assert.Equal(db.DatabasePath, DatabaseService.DatabasePath);
        Assert.False(string.Equals(
            Path.GetFullPath(DatabaseService.DatabasePath),
            Path.GetFullPath(DatabaseService.DefaultStoreDatabasePath),
            StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            Path.Combine("AppData", "Local", "SGDB"),
            info.BackupPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.DirectorySeparatorChar + "Backups" + Path.DirectorySeparatorChar,
            info.BackupPath, StringComparison.OrdinalIgnoreCase);

        using var zip = ZipFile.OpenRead(info.BackupPath);
        var entry = zip.Entries.Single(e => e.Name.Equals("deposito.db", StringComparison.OrdinalIgnoreCase));
        var extracted = Path.Combine(Path.GetTempPath(), "sgdb_bak_check_" + Guid.NewGuid().ToString("N") + ".db");
        entry.ExtractToFile(extracted, overwrite: true);
        try
        {
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = extracted,
                Mode = SqliteOpenMode.ReadOnly,
            }.ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM products WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", productId);
            Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));
        }
        finally
        {
            try { File.Delete(extracted); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void VacuumIntoComSucessoHabilitaSanear()
    {
        using var _ = Begin();
        SeedLeftover(10, 1, 2, 1, 3, 1, 7, 3, 10);
        Assert.False(LegacyMergeCleanupAdminService.HasValidSessionBackup);
        var info = LegacyMergeCleanupAdminService.CreateRequiredBackup();
        Assert.True(info.IsValid);
        Assert.True(LegacyMergeCleanupAdminService.HasValidSessionBackup);
        var result = LegacyMergeCleanupAdminService.ExecuteProven(true);
        Assert.True(result.Executed);
        Assert.Equal(1, result.Sanitized);
        Assert.False(LegacyMergeCleanupAdminService.ResultIsWarning(result));
    }

    [Fact]
    public void FalhaVacuumIntoNaoUsaFileCopyNemHabilitaSanear()
    {
        using var db = Begin();
        SeedLeftover(10, 1, 2, 1, 3, 1, 7, 3, 10);
        BackupService.TestBeforeConsistentSnapshot = () =>
            throw new InvalidOperationException("VACUUM INTO injetado");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => LegacyMergeCleanupAdminService.CreateRequiredBackup());
            Assert.Contains(LegacyMergeCleanupRules.BackupConsistentFailedMessage, ex.Message);
            Assert.Contains("VACUUM INTO", ex.Message);
            Assert.False(LegacyMergeCleanupAdminService.HasValidSessionBackup);
            Assert.Null(LegacyMergeCleanupAdminService.BackupPath);
            var backups = Path.Combine(Path.GetDirectoryName(db.DatabasePath)!, "Backups");
            if (Directory.Exists(backups))
            {
                Assert.Empty(Directory.GetFiles(backups, "SGDB_backup_residuos_*.zip"));
            }

            var blocked = Assert.Throws<InvalidOperationException>(
                () => LegacyMergeCleanupAdminService.ExecuteProven(true));
            Assert.Equal(LegacyMergeCleanupRules.BackupRequiredMessage, blocked.Message);
        }
        finally
        {
            BackupService.TestBeforeConsistentSnapshot = null;
        }
    }

    [Fact]
    public void WalAtivoNaoAceitaFileCopyComoBackup69Tf()
    {
        using var db = Begin();
        SeedLeftover(10, 1, 2, 1, 3, 1, 7, 3, 10);
        var token = "WAL_TOKEN_" + Guid.NewGuid().ToString("N")[..8];
        var productId = SeedProduct("WLT", token, 1, 1, 2, active: true);
        Assert.True(productId > 0);
        using (var live = DatabaseService.OpenConnection())
        using (var liveCmd = live.CreateCommand())
        {
            liveCmd.CommandText = "SELECT COUNT(*) FROM products WHERE id = $id;";
            liveCmd.Parameters.AddWithValue("$id", productId);
            Assert.Equal(1, Convert.ToInt32(liveCmd.ExecuteScalar()));
        }

        var wal = db.DatabasePath + "-wal";
        Assert.True(File.Exists(wal), "O banco de teste deve estar em WAL antes do backup 69T-F.");

        var info = LegacyMergeCleanupAdminService.CreateRequiredBackup();
        Assert.True(LegacyMergeCleanupAdminService.HasValidSessionBackup);

        using var zip = ZipFile.OpenRead(info.BackupPath);
        var entry = zip.Entries.Single(e => e.Name.Equals("deposito.db", StringComparison.OrdinalIgnoreCase));
        var extracted = Path.Combine(Path.GetTempPath(), "sgdb_wal_check_" + Guid.NewGuid().ToString("N") + ".db");
        entry.ExtractToFile(extracted, overwrite: true);
        try
        {
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = extracted,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM products WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", productId);
            Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));
        }
        finally
        {
            try { File.Delete(extracted); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ClienteRecusaCreateRequiredBackup()
    {
        using var _ = Begin();
        SeedLeftover(10, 1, 2, 1, 3, 1, 7, 3, 10);
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        try
        {
            Assert.ThrowsAny<InvalidOperationException>(
                () => LegacyMergeCleanupAdminService.CreateRequiredBackup());
            Assert.False(LegacyMergeCleanupAdminService.HasValidSessionBackup);
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void VendedorNaoCriaBackupObrigatorio()
    {
        using var _ = Begin();
        TestDataHelper.SetSessionRole("vendedor");
        var ex = Assert.Throws<InvalidOperationException>(
            () => LegacyMergeCleanupAdminService.CreateRequiredBackup());
        Assert.Equal(LegacyMergeCleanupRules.AccessDeniedMessage, ex.Message);
        Assert.False(LegacyMergeCleanupAdminService.HasValidSessionBackup);
    }

    [Fact]
    public void ResultadoComFalhasUsaWarning()
    {
        using var _ = Begin();
        SeedLeftover(10, 1, 2, 1, 3, 1, 7, 3, 10);
        LegacyMergeCleanupAdminService.CreateRequiredBackup();
        LegacyMergedProductStockCleanupService.TestBeforeWriteAudit =
            () => throw new InvalidOperationException("falha injetada no item");
        try
        {
            var result = LegacyMergeCleanupAdminService.ExecuteProven(true);
            Assert.True(result.Executed);
            Assert.True(result.Failures > 0);
            Assert.True(LegacyMergeCleanupAdminService.ResultIsWarning(result));
            var text = LegacyMergeCleanupAdminService.FormatResult(result);
            Assert.Contains("Saneados:", text);
            Assert.Contains("Já saneados:", text);
            Assert.Contains("Bloqueados:", text);
            Assert.Contains("Falhas:", text);
        }
        finally
        {
            LegacyMergedProductStockCleanupService.TestBeforeWriteAudit = null;
        }
    }

    [Fact]
    public void FecharReabrirModuloNoMesmoProcessoMantemBackup()
    {
        using var _ = Begin();
        SeedLeftover(10, 1, 2, 1, 3, 1, 7, 3, 10);
        LegacyMergeCleanupAdminService.CreateRequiredBackup();
        Assert.True(LegacyMergeCleanupAdminService.HasValidSessionBackup);
        var path = LegacyMergeCleanupAdminService.BackupPath;
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(LegacyMergeCleanupAdminService.HasValidSessionBackup);
        Assert.Equal(path, LegacyMergeCleanupAdminService.BackupPath);
    }

    [Fact]
    public void ReinicioDaSessaoNaoHerdaBackupAnterior()
    {
        using var _ = Begin();
        SeedLeftover(10, 1, 2, 1, 3, 1, 7, 3, 10);
        LegacyMergeCleanupAdminService.CreateRequiredBackup();
        Assert.True(LegacyMergeCleanupAdminService.HasValidSessionBackup);
        LegacyMergeCleanupAdminService.ResetSession();
        Assert.False(LegacyMergeCleanupAdminService.HasValidSessionBackup);
        var ex = Assert.Throws<InvalidOperationException>(
            () => LegacyMergeCleanupAdminService.ExecuteProven(true));
        Assert.Equal(LegacyMergeCleanupRules.BackupRequiredMessage, ex.Message);
    }

    [Fact]
    public void ConfirmacaoCancelarNaoAltera()
    {
        using var _ = Begin();
        var (keep, absorb) = SeedLeftover(10, 1, 2, 1, 3, 1, 7, 3, 10);
        LegacyMergeCleanupAdminService.CreateRequiredBackup();
        var keepBefore = Snapshot(keep);
        var result = LegacyMergeCleanupAdminService.ExecuteProven(confirmed: false);
        Assert.False(result.Executed);
        Assert.Equal(0, result.Sanitized);
        Assert.Equal(3, ProductService.GetById(absorb)!.Stock, 4);
        AssertKeepUnchanged(keep, keepBefore);
        Assert.Equal(0, CountBatchAudits());
    }

    [Fact]
    public void ConfirmacaoExecutarChamaServicoEProtegeKeep()
    {
        using var _ = Begin();
        var (keep, absorb) = SeedLeftover(
            keepStock: 40, keepCost: 1.5, keepSale: 3, keepCompra: 1.55,
            absorbStock: 9, absorbCost: 1.4,
            auditKeepBefore: 31, auditAbsorbBefore: 9, auditAfter: 40);
        LegacyMergeCleanupAdminService.CreateRequiredBackup();
        var keepBefore = Snapshot(keep);

        var result = LegacyMergeCleanupAdminService.ExecuteProven(true);
        Assert.True(result.Executed);
        Assert.Equal(1, result.Sanitized);
        Assert.Equal(0, result.Failures);
        Assert.Contains("não foi alterado", result.KeepUnchangedMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, ProductService.GetById(absorb)!.Stock, 4);
        Assert.Equal(0, ProductService.GetById(absorb)!.StockFridge, 4);
        AssertKeepUnchanged(keep, keepBefore);

        var again = LegacyMergeCleanupAdminService.ExecuteProven(true);
        Assert.Equal(1, again.AlreadyClean);
        Assert.Equal(0, again.Sanitized);
        AssertKeepUnchanged(keep, keepBefore);
        Assert.Equal(0, ProductService.GetById(absorb)!.Stock, 4);
    }

    [Fact]
    public void UiNaoChamaMergeProducts()
    {
        var root = AppSourceRoot();
        var view = File.ReadAllText(Path.Combine(root, "Views", "LegacyMergeCleanupModuleView.xaml.cs"));
        var admin = File.ReadAllText(Path.Combine(root, "Services", "LegacyMergeCleanupAdminService.cs"));
        Assert.Contains("ResultIsWarning", view);
        Assert.Contains("MessageBoxImage.Warning", view);
        Assert.DoesNotContain("MergeProducts", view);
        Assert.DoesNotContain("MergeProducts", admin);
        Assert.Contains("LegacyMergedProductStockCleanupService.Sanitize", admin);
        Assert.Contains("BackupService.CreateConsistentBackup", admin);
        Assert.DoesNotContain("BackupService.CreateBackup(", admin);
        Assert.DoesNotContain("UPDATE products", view, StringComparison.OrdinalIgnoreCase);
        var backup = File.ReadAllText(Path.Combine(root, "Services", "BackupService.cs"));
        Assert.Contains("allowDegradedCopy: false", backup);
        Assert.Contains("CreateConsistentBackup", backup);
    }

    [Fact]
    public void HallsMentosVinhoForaDoAutomatico()
    {
        using var _ = Begin();
        SeedLeftover(10, 1, 2, 1, 3, 1, 7, 3, 10);
        var halls = SeedProduct("H32", "BALA HALLS MORANGO", -120, 1.33, 2, active: false);
        var mentos = SeedProduct("M10", "MENTOS", 8, 1, 2, active: false);
        var vinho = SeedProduct("V353", "VINHO SUAVE", 4, 10, 15, active: false);
        var exec = LegacyMergeCleanupAdminService.ListExecutable();
        Assert.DoesNotContain(exec, c => c.AbsorbId == halls);
        Assert.DoesNotContain(exec, c => c.AbsorbId == mentos);
        Assert.DoesNotContain(exec, c => c.AbsorbId == vinho);
        Assert.Equal(-120, ProductService.GetById(halls)!.Stock, 4);
        Assert.Equal(8, ProductService.GetById(mentos)!.Stock, 4);
        Assert.Equal(4, ProductService.GetById(vinho)!.Stock, 4);
    }

    [Fact]
    public void ResultadoMostraSaneadosBloqueadosFalhasEAuditLote()
    {
        using var _ = Begin();
        SeedLeftover(10, 1, 2, 1, 3, 1, 7, 3, 10);
        SeedRevisarConflict();
        LegacyMergeCleanupAdminService.CreateRequiredBackup();

        var result = LegacyMergeCleanupAdminService.ExecuteProven(true);
        Assert.Equal(1, result.Candidates);
        Assert.Equal(1, result.Sanitized);
        Assert.Equal(1, result.Blocked);
        Assert.Equal(0, result.Failures);
        var text = LegacyMergeCleanupAdminService.FormatResult(result);
        Assert.Contains("Saneados:", text);
        Assert.Contains("Bloqueados:", text);
        Assert.Contains("Falhas:", text);
        Assert.Equal(1, CountBatchAudits());
    }

    [Fact]
    public void RedeClienteIncompativelBloqueia()
    {
        using var _ = Begin();
        var (_, absorb) = SeedLeftover(10, 1, 2, 1, 3, 1, 7, 3, 10);
        var stockBefore = ProductService.GetById(absorb)!.Stock;
        LegacyMergeCleanupAdminService.CreateRequiredBackup();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        try
        {
            Assert.True(StoreNetworkMode.IsModuleBlockedOnClient(LegacyMergeCleanupRules.ModuleId));
            Assert.False(LegacyMergeCleanupAdminService.CanExecuteOnThisMachine());
            Assert.ThrowsAny<InvalidOperationException>(
                () => LegacyMergeCleanupAdminService.ExecuteProven(true));
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
        Assert.Equal(stockBefore, ProductService.GetById(absorb)!.Stock, 4);
    }

    [Fact]
    public void HostCompativelPermiteEAnunciaCapability()
    {
        using var _ = Begin();
        SeedLeftover(10, 1, 2, 1, 3, 1, 7, 3, 10);
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleServer);
        try
        {
            Assert.True(LegacyMergeCleanupAdminService.CanExecuteOnThisMachine());
            Assert.False(StoreNetworkMode.IsModuleBlockedOnClient(LegacyMergeCleanupRules.ModuleId));
            LegacyMergeCleanupAdminService.CreateRequiredBackup();
            var result = LegacyMergeCleanupAdminService.ExecuteProven(true);
            Assert.True(result.Executed);
            Assert.Equal(1, result.Sanitized);
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }

        Assert.Contains(LegacyMergeCleanupRules.AtomicFeature, StoreNetworkHost.AdvertisedFeatures);
        Assert.True(LegacyMergeCleanupRules.SupportsCleanup(StoreNetworkHost.AdvertisedFeatures));
        var host = File.ReadAllText(Path.Combine(AppSourceRoot(), "Services", "StoreNetworkHost.cs"));
        Assert.Contains("apiVersion = 2", host);
        Assert.DoesNotContain("/api/legacy-merge", host, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SanitizeAllProven", host);
    }

    [Fact]
    public void NenhumSaneamentoNaInicializacao()
    {
        var app = File.ReadAllText(Path.Combine(AppSourceRoot(), "App.xaml.cs"));
        var main = File.ReadAllText(Path.Combine(AppSourceRoot(), "MainWindow.xaml.cs"));
        Assert.DoesNotContain("SanitizeAllProven", app);
        Assert.DoesNotContain("LegacyMergedProductStockCleanupService.Sanitize", app);
        Assert.DoesNotContain("ExecuteProven", app);
        Assert.DoesNotContain("SanitizeAllProven", main);
        Assert.DoesNotContain("LegacyMergedProductStockCleanupService.Sanitize", main);
    }

    [Fact]
    public void SgdbDatabasePathNuncaEscolheBancoReal()
    {
        var testPath = Path.Combine(
            Path.GetTempPath(), "SGDB.Tests", "iso-" + Guid.NewGuid().ToString("N"), "deposito.db");
        Environment.SetEnvironmentVariable(DatabaseService.DatabasePathEnvVar, testPath);
        try
        {
            var resolved = DatabaseService.ResolveStartupDatabasePath();
            Assert.Equal(Path.GetFullPath(testPath), resolved);
            Assert.False(string.Equals(
                resolved,
                Path.GetFullPath(DatabaseService.DefaultStoreDatabasePath),
                StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DatabaseService.DatabasePathEnvVar, null);
        }

        Assert.Throws<InvalidOperationException>(() =>
            DatabaseService.ResolveStartupDatabasePath(null, @"C:\Users\Dell\Documents\SGDB_TESTE_69TF\app_teste\"));
        Assert.Equal(
            Path.GetFullPath(DatabaseService.DefaultStoreDatabasePath),
            DatabaseService.ResolveStartupDatabasePath(null, @"C:\Users\Dell\OneDrive\Documentos\SGDB_Para_Outro_PC\SGDB\"));
    }

    private static (int Keep, int Absorb) SeedLeftover(
        double keepStock, double keepCost, double keepSale, double keepCompra,
        double absorbStock, double absorbCost,
        double auditKeepBefore, double auditAbsorbBefore, double auditAfter)
    {
        var keep = SeedProduct("KEEP", "PRODUTO KEEP", keepStock, keepCost, keepSale,
            active: true, barcode: EanKeep, pack: PackKeep, compra: keepCompra);
        var absorb = SeedProduct("ABS", "PRODUTO ABSORB", absorbStock, absorbCost, keepSale,
            active: false);
        InsertLegacyAudit(absorb, keep, "PRODUTO ABSORB", "PRODUTO KEEP",
            auditKeepBefore, auditAbsorbBefore, auditAfter);
        return (keep, absorb);
    }

    private static int SeedRevisarConflict()
    {
        var keep = SeedProduct("K2", "KEEP REVISAR", 10, 1, 2, active: true, barcode: "7891000003003");
        var absorb = SeedProduct("A2", "ABS REVISAR", 50, 1, 2, active: false);
        InsertLegacyAudit(absorb, keep, "ABS REVISAR", "KEEP REVISAR", 10, 303, 313);
        return absorb;
    }

    private static int SeedProduct(
        string code, string name, double stock, double cost, double sale,
        bool active, string? barcode = null, string? pack = null, double compra = 0)
    {
        var extra = new ProductExtra
        {
            BarcodeEmbalagem = pack,
            FatorEmbalagem = string.IsNullOrWhiteSpace(pack) ? 1 : 12,
            PrecoCompra = compra > 0 ? compra : cost,
        };
        var id = ProductService.Create(new ProductInput
        {
            Code = code,
            Barcode = barcode,
            Name = name,
            GroupName = "GERAL",
            Unit = "UN",
            CostPrice = cost,
            SalePrice = sale,
            Stock = stock,
            Extra = extra,
            Active = true,
        }).Id;
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE products
            SET stock = $s, cost_price = $c, sale_price = $sale, active = $a,
                barcode = CASE WHEN $a = 0 THEN NULL ELSE barcode END
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$s", stock);
        cmd.Parameters.AddWithValue("$c", cost);
        cmd.Parameters.AddWithValue("$sale", sale);
        cmd.Parameters.AddWithValue("$a", active ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        return id;
    }

    private static void InsertLegacyAudit(
        int absorbId, int keepId, string absorbName, string keepName,
        double keepBefore, double absorbBefore, double after)
    {
        var details = $"#{absorbId} {absorbName} → #{keepId} {keepName} · estoque {Fmt(keepBefore)}+{Fmt(absorbBefore)}={Fmt(after)}";
        InsertRawAudit(keepId, details);
    }

    private static void InsertRawAudit(int keepId, string details)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO audit_log (user_login, user_name, action, entity, entity_id, details)
            VALUES ('hg', 'DEPOSITO', 'unificar', 'produto', $eid, $d);
            """;
        cmd.Parameters.AddWithValue("$eid", keepId.ToString());
        cmd.Parameters.AddWithValue("$d", details);
        cmd.ExecuteNonQuery();
    }

    private static string Fmt(double v)
    {
        if (Math.Abs(v - Math.Round(v)) < 1e-9)
            return ((int)Math.Round(v)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return v.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed record KeepSnap(double Stock, double Fridge, double Cost, double Sale, double PrecoCompra);

    private static KeepSnap Snapshot(int keepId)
    {
        var p = ProductService.GetById(keepId)!;
        return new KeepSnap(p.Stock, p.StockFridge, p.CostPrice, p.SalePrice,
            ProductExtra.Parse(p.ExtraJson).PrecoCompra);
    }

    private static void AssertKeepUnchanged(int keepId, KeepSnap before)
    {
        var after = Snapshot(keepId);
        Assert.Equal(before.Stock, after.Stock, 4);
        Assert.Equal(before.Fridge, after.Fridge, 4);
        Assert.Equal(before.Cost, after.Cost, 4);
        Assert.Equal(before.Sale, after.Sale, 4);
        Assert.Equal(before.PrecoCompra, after.PrecoCompra, 4);
    }

    private static int CountBatchAudits()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM audit_log
            WHERE action = $a AND entity = $e;
            """;
        cmd.Parameters.AddWithValue("$a", LegacyMergeCleanupAdminService.BatchAuditAction);
        cmd.Parameters.AddWithValue("$e", LegacyMergeCleanupAdminService.BatchAuditEntity);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string AppSourceRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "SGDB.App"));
}
