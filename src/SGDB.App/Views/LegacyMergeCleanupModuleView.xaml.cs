using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class LegacyMergeCleanupModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    public LegacyMergeCleanupModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Focus();
            Load();
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Refresh_Click(object sender, RoutedEventArgs e) => Load();

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5) { Load(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }

    private void Load()
    {
        try
        {
            if (!LegacyMergeCleanupAdminService.CanAccess())
            {
                MessageBox.Show(LegacyMergeCleanupRules.AccessDeniedMessage,
                    "Acesso negado", MessageBoxButton.OK, MessageBoxImage.Warning);
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            DbPathText.Text = $"Banco: {DatabaseService.DatabasePath}";
            InventoryWarningText.Text = LegacyMergeCleanupRules.InventoryWarningMessage
                + "\nPrioridade conhecida: Original 300 ml, Brahma 300 ml, Rothmans Blue, Coca-Cola 350 ml (por amostragem). Nenhum deles é ajustado automaticamente.";

            var client = !LegacyMergeCleanupAdminService.CanExecuteOnThisMachine();
            NetworkBanner.Text = LegacyMergeCleanupRules.ClientBlockedMessage;
            NetworkBanner.Visibility = client ? Visibility.Visible : Visibility.Collapsed;
            SanitizeBtn.IsEnabled = !client && LegacyMergeCleanupAdminService.HasValidSessionBackup;
            BackupBtn.IsEnabled = !client;

            AutoGrid.ItemsSource = LegacyMergeCleanupAdminService.ListAutomatic();
            ManualGrid.ItemsSource = LegacyMergeCleanupAdminService.ListManualReview();
            RefreshBackupStatus();

            var auto = (AutoGrid.ItemsSource as IReadOnlyList<LegacyMergeAbsorbCandidate>)?.Count ?? 0;
            var manual = (ManualGrid.ItemsSource as IReadOnlyList<LegacyMergeAbsorbCandidate>)?.Count ?? 0;
            var exec = LegacyMergeCleanupAdminService.ListExecutable().Count;
            SummaryText.Text =
                $"{auto} automático(s) · {exec} COMPROVADO(s) para saneamento · {manual} em revisão manual";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Resíduos de unificações antigas",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshBackupStatus()
    {
        if (!LegacyMergeCleanupAdminService.HasValidSessionBackup)
        {
            BackupStatusText.Text = "Backup desta execução: nenhum ainda. O saneamento fica bloqueado até validar um backup.";
            SanitizeBtn.IsEnabled = false;
            return;
        }

        BackupStatusText.Text =
            $"Backup: {System.IO.Path.GetFileName(LegacyMergeCleanupAdminService.BackupPath)} · " +
            $"{LegacyMergeCleanupAdminService.BackupDate:dd/MM/yyyy HH:mm:ss} · " +
            $"{LegacyMergeCleanupAdminService.BackupSize:N0} bytes";
        SanitizeBtn.IsEnabled = LegacyMergeCleanupAdminService.CanExecuteOnThisMachine();
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!LegacyMergeCleanupAdminService.CanExecuteOnThisMachine())
            {
                MessageBox.Show(LegacyMergeCleanupRules.ClientBlockedMessage,
                    "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var info = LegacyMergeCleanupAdminService.CreateRequiredBackup();
            RefreshBackupStatus();
            MessageBox.Show(
                $"Backup validado.\n\n{info.BackupPath}\n{info.BackupSize:N0} bytes",
                "Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Sanitize_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var confirm = MessageBox.Show(
                LegacyMergeCleanupAdminService.BuildConfirmMessage(),
                "Confirmar saneamento",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK)
            {
                LegacyMergeCleanupAdminService.ExecuteProven(confirmed: false);
                return;
            }

            var result = LegacyMergeCleanupAdminService.ExecuteProven(confirmed: true);
            var icon = LegacyMergeCleanupAdminService.ResultIsWarning(result)
                ? MessageBoxImage.Warning
                : MessageBoxImage.Information;
            MessageBox.Show(
                LegacyMergeCleanupAdminService.FormatResult(result),
                "Resultado do saneamento",
                MessageBoxButton.OK,
                icon);
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Saneamento", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AutoGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AutoGrid.SelectedItem is LegacyMergeAbsorbCandidate row)
            ShowDetail(row.AbsorbId);
    }

    private void ManualGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ManualGrid.SelectedItem is LegacyMergeAbsorbCandidate row)
            ShowDetail(row.AbsorbId);
    }

    private void ShowDetail(int absorbId)
    {
        try
        {
            var d = LegacyMergeCleanupAdminService.GetDetail(absorbId);
            var c = d.Candidate;
            var absActive = d.Absorb is null ? "—" : (d.Absorb.Active ? "ativo" : "inativo");
            var cult = CultureInfo.GetCultureInfo("pt-BR");
            DetailText.Text =
                "PRODUTO ANTIGO:\n" +
                $"- id: {c.AbsorbId}\n" +
                $"- nome: {c.AbsorbName}\n" +
                $"- saldo residual: {c.AbsorbStock.ToString("N2", cult)}\n" +
                $"- custo: {d.AbsorbCost.ToString("N4", cult)}\n" +
                $"- {absActive}\n\n" +
                "PRODUTO PRINCIPAL:\n" +
                $"- id: {c.KeepId}\n" +
                $"- nome: {c.KeepName}\n" +
                $"- estoque atual: {d.KeepStockBefore.ToString("N2", cult)}\n" +
                $"- custo atual: {d.KeepCost.ToString("N4", cult)}\n" +
                $"- preço de compra: {d.KeepPrecoCompra.ToString("N4", cult)}\n" +
                $"- preço venda: {d.KeepSalePrice.ToString("N4", cult)}\n\n" +
                "MERGE ORIGINAL:\n" +
                $"- data: {c.MergedAt}\n" +
                $"- usuário: {c.UserName} ({c.UserLogin})\n" +
                $"- referência do audit: #{c.MergeAuditId}\n" +
                $"- conta registrada: {(c.HasUnificacaoMovement ? "sim" : "não")}\n\n" +
                $"ESTOQUE PRINCIPAL ANTES: {d.KeepStockBefore.ToString("N2", cult)}\n" +
                $"ESTOQUE PRINCIPAL DEPOIS: {d.KeepStockAfter.ToString("N2", cult)}\n\n" +
                LegacyMergeCleanupRules.NoTransferMessage;
        }
        catch (Exception ex)
        {
            DetailText.Text = ex.Message;
        }
    }
}
