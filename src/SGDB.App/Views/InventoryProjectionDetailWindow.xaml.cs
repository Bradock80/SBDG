using System.Windows;
using System.Windows.Input;
using SGDB.Models;

namespace SGDB.Views;

public partial class InventoryProjectionDetailWindow : Window
{
    public InventoryProjectionDetailWindow(InventoryProjectionDetail detail)
    {
        InitializeComponent();
        Bind(detail);
    }

    private void Bind(InventoryProjectionDetail detail)
    {
        var giro = InventoryIntelligencePresentation.ToGridRow(detail.Intelligence);
        var projection = detail.Projection;
        var name = string.IsNullOrWhiteSpace(giro.Name) ? "Produto" : giro.Name;
        var code = string.IsNullOrWhiteSpace(giro.Code) ? InventoryProjectionPresentation.EmDash : giro.Code;

        Title = $"{InventoryProjectionDetailUi.Heading} — {name}";
        ProductNameText.Text = name;
        ProductCodeText.Text = $"Código {code}";

        StockText.Text = giro.StockDisplay;
        FridgeText.Text = giro.StockFridgeDisplay;
        TotalText.Text = giro.TotalStockDisplay;
        VmvText.Text = giro.Vmv30Display;
        CoverageText.Text = giro.CoverageDisplay;
        LastSaleText.Text = giro.LastSaleDisplay;
        HistoryText.Text = giro.HistoryDisplay;

        DemandText.Text = projection.ProjectedDemandDisplay;
        Surplus30Text.Text = projection.Surplus30Display;
        ExcessStatusText.Text = projection.ExcessStatusDisplay;
        SkuBlockText.Text = projection.SkuBlockedExplanation;
        SkuBlockText.Visibility = string.IsNullOrWhiteSpace(projection.SkuBlockedExplanation)
            ? Visibility.Collapsed
            : Visibility.Visible;

        ValiditySummaryText.Text = projection.ValidityRiskDisplay;
        LotsGrid.ItemsSource = projection.Lots;
        var emptyLots = projection.Lots.Count == 0;
        LotsGrid.Visibility = emptyLots ? Visibility.Collapsed : Visibility.Visible;
        LotsEmptyText.Text = InventoryProjectionDetailUi.EmptyLotsMessage(projection);
        LotsEmptyText.Visibility = emptyLots ? Visibility.Visible : Visibility.Collapsed;

        ExpiryValueText.Text = $"{projection.SurplusValueCaption}: {projection.SurplusValueDisplay}";
        ExpiryValueHintText.Text = InventoryProjectionDetailUi.SurplusValueExplanation(projection);

        TrackedText.Text = projection.TrackedLotQuantityDisplay;
        UntrackedText.Text = projection.UntrackedWarehouseQuantityDisplay;
        UntrackedAlertText.Text = projection.UntrackedWarehouseAlert;
        UntrackedAlertText.Visibility = string.IsNullOrWhiteSpace(projection.UntrackedWarehouseAlert)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (projection.HasLotLocationLimitation)
        {
            FridgeBanner.Visibility = Visibility.Visible;
            FridgeAlertText.Text = projection.FridgeLimitationAlert;
        }

        var observations = InventoryProjectionDetailUi.ObservationLines(projection);
        if (observations.Count > 0)
        {
            ObservationsSection.Visibility = Visibility.Visible;
            ObservationsList.ItemsSource = observations;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        Close();
        e.Handled = true;
    }
}
