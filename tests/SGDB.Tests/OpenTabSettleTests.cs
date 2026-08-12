using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// Caracterização do fluxo LEGADO (APIs separadas): FinalizeSale + MarkSettled.
/// Documenta o risco da API antiga — a View agora usa OpenTabSettlementService.SettleOpenTab.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class OpenTabSettleTests
{
    [Fact]
    public void SettleDeck_FluxoAtual_FinalizaVendaEMarcaDeckComoFechado()
    {
        // Caminho feliz ainda válido nas APIs públicas separadas (compatibilidade).
        using var db = TempDatabase.Create();
        CashService.OpenSession(openingAmount: 50, notes: "teste");

        const double stockBefore = 50;
        const double qty = 2;
        const double unitPrice = 15;
        var expectedTotal = 30.0;

        var productId = TestDataHelper.SeedSimpleProduct(stockBefore, unitPrice, costPrice: 5);
        var tabId = OpenTabService.Create("Mesa Teste");
        OpenTabService.AddProduct(tabId, productId, qty, unitPrice);

        var lines = OpenTabService.ToCartLines(tabId).ToList();
        Assert.Single(lines);

        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items = lines,
            PaymentType = "Dinheiro",
            CashReceived = expectedTotal,
        });
        OpenTabService.MarkSettled(tabId, sale.SaleId);

        Assert.True(sale.SaleId > 0);
        Assert.Equal(expectedTotal, sale.Total);
        Assert.Equal(stockBefore - qty, TestDataHelper.GetProductStock(productId));

        using var conn = DatabaseService.OpenConnection();

        using (var cash = conn.CreateCommand())
        {
            cash.CommandText = """
                SELECT COUNT(*), IFNULL(SUM(amount_in), 0)
                FROM cash_movements
                WHERE IFNULL(ref_type, '') = 'sale' AND ref_id = $sale;
                """;
            cash.Parameters.AddWithValue("$sale", sale.SaleId);
            using var r = cash.ExecuteReader();
            Assert.True(r.Read());
            Assert.True(r.GetInt64(0) >= 1);
            Assert.Equal(expectedTotal, r.GetDouble(1));
        }

        using (var tab = conn.CreateCommand())
        {
            tab.CommandText = """
                SELECT status, sale_id
                FROM open_tabs WHERE id = $id;
                """;
            tab.Parameters.AddWithValue("$id", tabId);
            using var r = tab.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal("settled", r.GetString(0));
            Assert.False(r.IsDBNull(1));
            Assert.Equal(sale.SaleId, r.GetInt32(1));
        }
    }

    /// <summary>
    /// Risco histórico da API antiga: FinalizeSale commitado sem MarkSettled.
    /// A View NÃO usa mais esse caminho; mantido para caracterizar as APIs separadas.
    /// </summary>
    [Fact]
    public void SettleDeck_ApiAntigaSeparada_PermiteVendaConfirmadaComDeckAindaAberto()
    {
        using var db = TempDatabase.Create();
        CashService.OpenSession(openingAmount: 50, notes: "teste");

        const double stockBefore = 40;
        const double qty = 3;
        const double unitPrice = 8;
        var expectedTotal = 24.0;

        var productId = TestDataHelper.SeedSimpleProduct(stockBefore, unitPrice, costPrice: 3);
        var tabId = OpenTabService.Create("Deck Aberto");
        OpenTabService.AddProduct(tabId, productId, qty, unitPrice);

        var lines = OpenTabService.ToCartLines(tabId).ToList();
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items = lines,
            PaymentType = "Dinheiro",
            CashReceived = expectedTotal,
        });
        // Deliberadamente NÃO chama MarkSettled — espelha falha entre as duas operações da API antiga.

        using var conn = DatabaseService.OpenConnection();

        using (var saleCmd = conn.CreateCommand())
        {
            saleCmd.CommandText = "SELECT cancelled, total FROM sales WHERE id = $id;";
            saleCmd.Parameters.AddWithValue("$id", sale.SaleId);
            using var r = saleCmd.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal(0, r.GetInt32(0));
            Assert.Equal(expectedTotal, r.GetDouble(1));
        }

        Assert.Equal(stockBefore - qty, TestDataHelper.GetProductStock(productId));

        using (var cash = conn.CreateCommand())
        {
            cash.CommandText = """
                SELECT COUNT(*) FROM cash_movements
                WHERE IFNULL(ref_type, '') = 'sale' AND ref_id = $sale;
                """;
            cash.Parameters.AddWithValue("$sale", sale.SaleId);
            Assert.True(Convert.ToInt32(cash.ExecuteScalar()) >= 1);
        }

        using (var tab = conn.CreateCommand())
        {
            tab.CommandText = """
                SELECT status, sale_id
                FROM open_tabs WHERE id = $id;
                """;
            tab.Parameters.AddWithValue("$id", tabId);
            using var r = tab.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal("open", r.GetString(0));
            Assert.True(r.IsDBNull(1));
        }
    }
}
