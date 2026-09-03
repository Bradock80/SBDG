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

        var attention = detail.Attention ?? InventoryAttentionPresentation.MissingRow(
            detail.Intelligence.ProductId);
        AttentionPriorityText.Text = attention.PriorityDisplay;
        AttentionReasonText.Text = attention.PrimaryReasonDisplay;
        AttentionActionText.Text = attention.ActionDisplay;
        AttentionConfidenceText.Text = attention.ConfidenceDisplay;
        AttentionExplanationText.Text = attention.Explanation;
        if (attention.SecondaryReasonDisplays.Count > 0)
        {
            AttentionSecondaryPanel.Visibility = Visibility.Visible;
            AttentionSecondaryList.ItemsSource = attention.SecondaryReasonDisplays;
        }

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

        BindCommercial(detail.Commercial);
        BindPromotion(detail.PromotionSuggestion);
    }

    private void BindCommercial(InventoryCommercialScenarioPresentationRow commercial)
    {
        commercial ??= InventoryCommercialScenarioPresentation.MissingRow();
        CommercialStatusText.Text = commercial.StatusLabel;
        CommercialThesisText.Text = commercial.ThesisLabel;
        CommercialReasonText.Text = commercial.PrimaryReasonLabel;
        CommercialExplanationText.Text = commercial.Explanation;
        CommercialGuidanceText.Text = commercial.ActionGuidance;

        CommercialFinancePanel.Visibility = commercial.ShowFinancialAnalysis
            ? Visibility.Visible
            : Visibility.Collapsed;
        CommercialCatalogPriceText.Text = commercial.CurrentCatalogPriceText;
        CommercialCurrentMarginText.Text = commercial.CurrentGrossMarginText;
        CommercialMinMarginText.Text = commercial.MinimumGrossMarginText;
        CommercialFloorText.Text = commercial.FloorPriceText;
        CommercialRoomText.Text = commercial.FinancialRoomText;

        var showQuantity = commercial.AttentionQuantityText != InventoryCommercialScenarioPresentation.EmDash;
        CommercialQuantityPanel.Visibility = showQuantity ? Visibility.Visible : Visibility.Collapsed;
        CommercialQuantityLabelText.Text = commercial.AttentionQuantityLabel;
        CommercialQuantityText.Text = commercial.AttentionQuantityText;

        var showScenarios = commercial.ShowScenarioOptions;
        CommercialScenariosList.ItemsSource = showScenarios
            ? commercial.Scenarios
            : [];
        CommercialScenariosList.Visibility = showScenarios
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (commercial.Warnings.Count > 0)
        {
            CommercialWarningsPanel.Visibility = Visibility.Visible;
            CommercialWarningsList.ItemsSource = commercial.Warnings;
        }

        if (commercial.SecondaryReasonLabels.Count > 0)
        {
            CommercialSecondaryPanel.Visibility = Visibility.Visible;
            CommercialSecondaryList.ItemsSource = commercial.SecondaryReasonLabels;
        }

        var showDisclaimer = commercial.ShowFinancialAnalysis || commercial.IsScenarioAvailable;
        CommercialDisclaimerText.Text = showDisclaimer ? commercial.SimulationDisclaimer : "";
        CommercialDisclaimerText.Visibility = showDisclaimer ? Visibility.Visible : Visibility.Collapsed;
        CommercialFooterText.Text = showDisclaimer ? commercial.OperatorFooter : "";
        CommercialFooterText.Visibility = showDisclaimer ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BindPromotion(InventoryPromotionSuggestionPresentationRow suggestion)
    {
        suggestion ??= InventoryPromotionSuggestionPresentation.MissingRow();
        CommercialActionStatusText.Text = suggestion.StatusLabel;
        CommercialActionSuggestionText.Text = suggestion.ActionLabel;
        CommercialActionPriorityText.Text = suggestion.PriorityLabel;
        CommercialActionConfidenceText.Text = suggestion.ConfidenceLabel;
        CommercialActionReasonText.Text = suggestion.PrimaryReasonLabel;
        CommercialActionExplanationText.Text = suggestion.Explanation;
        CommercialActionObjectiveText.Text = suggestion.ObjectiveLabel;

        var showQuantity = InventoryPromotionSuggestionDetailUi.ShowQuantity(suggestion);
        CommercialActionQuantityPanel.Visibility = showQuantity ? Visibility.Visible : Visibility.Collapsed;
        CommercialActionQuantityText.Text = suggestion.AttentionQuantityText;
        CommercialActionSourceText.Text = suggestion.AttentionQuantitySourceLabel;

        var possibilities = InventoryPromotionSuggestionDetailUi.PossibilityLines(suggestion);
        CommercialActionPossibilitiesPanel.Visibility = possibilities.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        CommercialActionPossibilitiesList.ItemsSource = possibilities;

        if (InventoryPromotionSuggestionDetailUi.ShowWarnings(suggestion))
        {
            CommercialActionWarningsPanel.Visibility = Visibility.Visible;
            CommercialActionWarningsList.ItemsSource = suggestion.WarningLabels;
        }

        if (InventoryPromotionSuggestionDetailUi.ShowSecondary(suggestion))
        {
            CommercialActionSecondaryPanel.Visibility = Visibility.Visible;
            CommercialActionSecondaryList.ItemsSource = suggestion.SecondaryReasonLabels;
        }

        CommercialActionDisclaimerText.Text = suggestion.DisclaimerText;
        CommercialActionDisclaimerText.Visibility = string.IsNullOrWhiteSpace(suggestion.DisclaimerText)
            ? Visibility.Collapsed
            : Visibility.Visible;
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
