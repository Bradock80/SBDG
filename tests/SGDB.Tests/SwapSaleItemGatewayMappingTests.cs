using SGDB.Adapters;
using SGDB.Application.Sales;
using SGDB.Models;

namespace SGDB.Tests;

/// <summary>
/// Paridade de mapeamento adapters de Swap (sem SQLite).
/// </summary>
public class SwapSaleItemGatewayMappingTests
{
    [Fact]
    public void Preview_ToResult_MapsFieldsUsedByView()
    {
        var preview = new PdvSwapItemPreview
        {
            SaleId = 42,
            OldTotal = 90,
            NewTotal = 110,
            Difference = 20,
            PaymentType = "Dinheiro",
            CurrentPayments =
            [
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 90 },
            ],
            CustomerPersonId = 7,
            IsPureFiado = false,
            RequiresPaymentConfirmation = true,
            RefundHint = null,
            OldGross = 100,
            NewGross = 120,
            OriginalAdjustment = -10,
        };

        var result = PreviewSwapSaleItemGateway.ToResult(preview);

        Assert.Equal(42, result.SaleId);
        Assert.Equal(90, result.OldTotal);
        Assert.Equal(110, result.NewTotal);
        Assert.Equal(20, result.Difference);
        Assert.Equal("Dinheiro", result.PaymentType);
        Assert.Single(result.CurrentPayments);
        Assert.Equal("Dinheiro", result.CurrentPayments[0].PaymentType);
        Assert.Equal(90, result.CurrentPayments[0].Amount);
        Assert.Equal(7, result.CustomerPersonId);
        Assert.False(result.IsPureFiado);
        Assert.True(result.RequiresPaymentConfirmation);
        Assert.Null(result.RefundHint);
    }

    [Fact]
    public void Preview_ToResult_PureFiado_Preserved()
    {
        var result = PreviewSwapSaleItemGateway.ToResult(new PdvSwapItemPreview
        {
            SaleId = 1,
            OldTotal = 100,
            NewTotal = 80,
            Difference = -20,
            PaymentType = "Fiado",
            CurrentPayments = [new PdvPaymentPart { PaymentType = "Fiado", Amount = 100 }],
            CustomerPersonId = 3,
            IsPureFiado = true,
            RequiresPaymentConfirmation = false,
            RefundHint = null,
        });

        Assert.True(result.IsPureFiado);
        Assert.False(result.RequiresPaymentConfirmation);
        Assert.Equal(3, result.CustomerPersonId);
    }

    [Fact]
    public void Swap_ToConfirmedPayments_Null_Preserved()
    {
        var parts = SwapSaleItemGateway.ToConfirmedPayments(new SwapSaleItemCommand
        {
            SaleId = 1,
            ItemId = 2,
            NewProductId = 3,
            ConfirmedPayments = null,
        });
        Assert.Null(parts);
    }

    [Fact]
    public void Swap_ToConfirmedPayments_MapsWithoutLoss()
    {
        var command = new SwapSaleItemCommand
        {
            SaleId = 1,
            ItemId = 2,
            NewProductId = 3,
            KeepLinePrice = true,
            NewQuantity = 5,
            ConfirmedPayments =
            [
                new SalePayment { PaymentType = "Dinheiro", Amount = 40 },
                new SalePayment { PaymentType = "Pix", Amount = 70 },
            ],
            CashReceived = 40,
            CustomerPersonId = 9,
        };

        var parts = SwapSaleItemGateway.ToConfirmedPayments(command)!;

        Assert.Equal(2, parts.Count);
        Assert.Equal("Dinheiro", parts[0].PaymentType);
        Assert.Equal(40, parts[0].Amount);
        Assert.Equal("Pix", parts[1].PaymentType);
        Assert.Equal(70, parts[1].Amount);
    }

    [Fact]
    public void Swap_ToConfirmedPayments_EmptyList_Preserved()
    {
        var parts = SwapSaleItemGateway.ToConfirmedPayments(new SwapSaleItemCommand
        {
            SaleId = 1,
            ItemId = 1,
            NewProductId = 1,
            ConfirmedPayments = [],
        });
        Assert.NotNull(parts);
        Assert.Empty(parts!);
    }

    [Fact]
    public void Swap_ToResult_MapsSaleIdTotalMessageRefund()
    {
        var appResult = new PdvSwapItemResult
        {
            Sale = new PdvSaleDetail { Id = 88, Total = 125 },
            RefundHint = 15,
            Message = "Produto trocado. Devolver/estornar R$ 15,00",
        };

        var result = SwapSaleItemGateway.ToResult(appResult);

        Assert.Equal(88, result.SaleId);
        Assert.Equal(125, result.NewTotal);
        Assert.Equal(15, result.RefundHint);
        Assert.Contains("Devolver", result.Message);
    }
}
