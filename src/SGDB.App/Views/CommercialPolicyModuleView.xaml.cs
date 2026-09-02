using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Services;

namespace SGDB.Views;

public partial class CommercialPolicyModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    public CommercialPolicyModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Apply(InventoryCommercialMarginAdminService.LoadSnapshot());
            Focus();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (!InventoryCommercialMarginAdminService.CanMutate()
            || !InventoryCommercialMarginAdminService.StationAllowsWrite())
        {
            SetFeedback(InventoryCommercialMarginAdminService.CanMutate()
                ? InventoryCommercialMarginAdminService.ClientBlockedMessage
                : "Seu usuário não tem permissão para alterar a política comercial.",
                isError: true);
            return;
        }

        var answer = MessageBox.Show(
            "Remover a margem mínima global?\n\nAs análises do Estoque Inteligente ficarão sem piso configurado.",
            "Política comercial",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        var result = InventoryCommercialMarginAdminService.TryClear(confirmed: true);
        Apply(result.Snapshot);
        SetFeedback(result.Message, isError: !result.Succeeded);
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9)
        {
            Save();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void Save()
    {
        var result = InventoryCommercialMarginAdminService.TrySave(PercentBox.Text);
        Apply(result.Snapshot, preserveEditorOnFailure: !result.Succeeded);
        if (!result.Succeeded && string.IsNullOrWhiteSpace(result.Message))
            return;
        SetFeedback(result.Message, isError: !result.Succeeded);
    }

    private void Apply(Models.InventoryCommercialMarginAdminSnapshot snapshot, bool preserveEditorOnFailure = false)
    {
        StatusText.Text = snapshot.StatusText;
        if (!preserveEditorOnFailure)
            PercentBox.Text = snapshot.EditorText;

        var canWrite = snapshot.CanMutate && snapshot.StationAllowsWrite;
        PercentBox.IsEnabled = canWrite;
        SaveBtn.IsEnabled = canWrite;
        ClearBtn.IsEnabled = canWrite;
    }

    private void SetFeedback(string message, bool isError)
    {
        FeedbackText.Text = message ?? "";
        FeedbackText.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(isError ? "#B91C1C" : "#0F766E")!);
    }
}
