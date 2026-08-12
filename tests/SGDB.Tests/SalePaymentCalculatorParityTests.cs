using SGDB.Domain.Sales;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// Paridade: fachada App (aliases) + Domain vs comportamento persistido via fluxos públicos.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class SalePaymentCalculatorParityTests
{
    private static IReadOnlyList<PaymentPart> NormalizeViaAppFacade(
        string paymentType, double total, IReadOnlyList<PdvPaymentPart>? payments)
    {
        // Espelha PdvService.NormalizePaymentParts (aliases no App, cálculo no Domain).
        IReadOnlyList<PaymentPart>? domainPayments = null;
        if (payments is { Count: > 0 })
        {
            domainPayments = payments
                .Select(p => new PaymentPart
                {
                    PaymentType = PaymentMethodsService.NormalizeToApiLabel(p.PaymentType),
                    Amount = p.Amount,
                })
                .ToList();
        }

        return SalePaymentCalculator.NormalizeParts(
            PaymentMethodsService.NormalizeToApiLabel(paymentType),
            total,
            domainPayments);
    }

    private static CashChangeResult TrocoViaAppFacade(
        IReadOnlyList<PdvPaymentPart> parts, double total, double cashReceived)
    {
        var domain = parts
            .Select(p => new PaymentPart { PaymentType = p.PaymentType, Amount = p.Amount })
            .ToList();
        return SalePaymentCalculator.ResolveCashChange(
            domain, total, cashReceived, PaymentMethodsService.IsDinheiroLabel);
    }

    [Fact]
    public void Paridade_Normalize_AliasCashEDebito_ViraCanonicos()
    {
        var parts = NormalizeViaAppFacade(
            "cash", 30,
            [
                new PdvPaymentPart { PaymentType = "a", Amount = 10 },
                new PdvPaymentPart { PaymentType = "debito", Amount = 20 },
            ]);
        Assert.Equal(2, parts.Count);
        Assert.Equal("Dinheiro", parts[0].PaymentType);
        Assert.Equal("Cartão Débito", parts[1].PaymentType);
    }

    [Fact]
    public void Paridade_Troco_Misto_CoincideComChangeSalePayment()
    {
        using var _ = TempDatabase.Create();
        CashService.OpenSession(50, "parity");
        TestDataHelper.SetSessionRole("admin");
        var productId = TestDataHelper.SeedSimpleProduct(stock: 50, salePrice: 10, costPrice: 4);
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = productId,
                    Quantity = 3,
                    UnitPrice = 10,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Pix",
            CashReceived = 0,
        });

        var newParts = new List<PdvPaymentPart>
        {
            new() { PaymentType = "Dinheiro", Amount = 10 },
            new() { PaymentType = "Pix", Amount = 20 },
        };
        const double cashRecv = 15;
        var expected = TrocoViaAppFacade(newParts, 30, cashRecv);

        var detail = PdvService.ChangeSalePayment(sale.SaleId, newParts, cashReceived: cashRecv);
        Assert.Equal(expected.CashReceived, detail.CashReceived);
        Assert.Equal(expected.ChangeAmount, detail.ChangeAmount);
    }

    [Fact]
    public void Paridade_PureFiado_PredicadoApp_CoincideComPreviewSwap()
    {
        using var _ = TempDatabase.Create();
        CashService.OpenSession(50, "parity");
        TestDataHelper.SetSessionRole("admin");
        var customerId = SeedCustomer("Cliente Fiado Parity");
        var a = TestDataHelper.SeedSimpleProduct(stock: 50, salePrice: 100, costPrice: 40, code: "PA", name: "Prod A");
        var b = TestDataHelper.SeedSimpleProduct(stock: 50, salePrice: 80, costPrice: 30, code: "PB", name: "Prod B");

        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = a,
                    Quantity = 1,
                    UnitPrice = 100,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Fiado",
            CustomerPersonId = customerId,
            CashReceived = 0,
        });

        var domainPure = SalePaymentCalculator.IsPureFiadoPayment(
            [new PaymentPart { PaymentType = "Fiado", Amount = 100 }],
            PaymentMethodsService.IsFiadoLabel);
        Assert.True(domainPure);

        var preview = PdvService.PreviewSwapSaleItem(sale.SaleId, GetFirstItemId(sale.SaleId), b, keepLinePrice: false);
        Assert.True(preview.IsPureFiado);
        Assert.False(preview.RequiresPaymentConfirmation);
    }

    [Fact]
    public void Paridade_DinheiroMaisFiado_NormalizeENaoEPuro()
    {
        using var _ = TempDatabase.Create();
        var parts = NormalizeViaAppFacade(
            "Dinheiro", 28.50,
            [
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 20 },
                new PdvPaymentPart { PaymentType = "Fiado", Amount = 8.50 },
            ]);
        Assert.Equal(2, parts.Count);
        Assert.False(SalePaymentCalculator.IsPureFiadoPayment(parts, PaymentMethodsService.IsFiadoLabel));
        Assert.Equal(8.50, parts.Single(p => p.PaymentType == "Fiado").Amount);
    }

    private static int GetFirstItemId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $id ORDER BY id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int SeedCustomer(string name)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('cliente', 'fisica', $name, 1, '{"ativo":true,"clientes":true}');
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
