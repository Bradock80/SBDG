using System.IO;
using SGDB.Domain.Sales;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69N-B — pagamento misto PIX + Dinheiro com acréscimo de tabela.
/// Preserva split parcial; substituição integral continua liberando PIX não pago no MP.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PdvMixedPaymentSplitTests
{
    private const double MacoBase = 11.50;
    private const double FixedSurcharge = 1.00;
    private const double TotalWithPix = 12.50;
    private const double PixPartial = 9.00;
    private const double CashNeed = 3.50;

    [Theory]
    [InlineData(9.00, 0, 12.50, false, false)] // split → não limpa
    [InlineData(9.00, 9.00, 12.50, false, false)] // já confirmado MP → não limpa
    [InlineData(12.50, 0, 12.50, false, true)] // integral não pago → limpa (substituição)
    [InlineData(12.50, 0, 12.50, true, false)] // trocando para PIX → não limpa
    [InlineData(0, 0, 12.50, false, false)] // sem alocação → não limpa
    public void ShouldClearUnpaidPix_SplitVsSubstituicao(
        double pixAllocated, double pixPaid, double total, bool toPix, bool expectClear)
    {
        var got = PdvPaymentSplitRules.ShouldClearUnpaidPixOnMethodSwitch(
            pixAllocated, pixPaid, total, switchingToPixMethod: toPix);
        Assert.Equal(expectClear, got);
    }

    [Fact]
    public void RemainingAmount_Pix9_DeTotal1250_E_350()
    {
        Assert.Equal(CashNeed,
            PdvPaymentSplitRules.RemainingAmount(TotalWithPix, PixPartial));
    }

    [Fact]
    public void RemainingAmount_NuncaNegativo()
    {
        Assert.Equal(0, PdvPaymentSplitRules.RemainingAmount(12.50, 20));
    }

    [Fact]
    public void Maco_PixAtivaTabela_Total1250()
    {
        using var _ = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        var cig = SeedMacoWithTable();

        var surcharge = PriceTablesService.CalcCartSurchargeAllocated(
            [Line(cig, MacoBase)],
            Pay("pix", MacoBase));

        Assert.Equal(FixedSurcharge, surcharge);
        Assert.Equal(TotalWithPix, ProductPriceHelper.RoundPrice(MacoBase + surcharge));
    }

    [Fact]
    public void Split_Pix9_MaisDinheiroRestante_MantemAcrescimo1250()
    {
        using var _ = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        var cig = SeedMacoWithTable();

        // Soft (dinheiro) não cobre toda a base → acréscimo cheio permanece.
        var surcharge = PriceTablesService.CalcCartSurchargeAllocated(
            [Line(cig, MacoBase)],
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["pix"] = PixPartial,
                ["dinheiro"] = CashNeed, // 3,50 < 11,50
            });

        Assert.Equal(FixedSurcharge, surcharge);
        Assert.Equal(TotalWithPix, ProductPriceHelper.RoundPrice(MacoBase + surcharge));
    }

    [Fact]
    public void Split_NaoDuplicaAcrescimo()
    {
        using var _ = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        var cig = SeedMacoWithTable();

        var once = PriceTablesService.CalcCartSurchargeAllocated(
            [Line(cig, MacoBase)],
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["pix"] = PixPartial,
                ["dinheiro"] = CashNeed,
            });
        var twice = PriceTablesService.CalcCartSurchargeAllocated(
            [Line(cig, MacoBase)],
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["pix"] = PixPartial,
                ["dinheiro"] = CashNeed,
            });

        Assert.Equal(FixedSurcharge, once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void DinheiroCobreTodaBase_SemAcrescimo_MesmoComPixNoMapaDeAvulsoZero()
    {
        using var _ = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        var cig = SeedMacoWithTable();

        var surcharge = PriceTablesService.CalcCartSurchargeAllocated(
            [Line(cig, MacoBase)],
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["dinheiro"] = MacoBase,
                ["pix"] = 0.01, // residual irrelevante; soft cobre 100% da base
            });

        // Soft cobre 11,50 → sem acréscimo (regra comercial preservada).
        Assert.Equal(0, surcharge);
    }

    [Fact]
    public void OrdemInversa_DinheiroParcial_MaisPix_MantemAcrescimo()
    {
        using var _ = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        var cig = SeedMacoWithTable();

        var surcharge = PriceTablesService.CalcCartSurchargeAllocated(
            [Line(cig, MacoBase)],
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["dinheiro"] = CashNeed,
                ["pix"] = PixPartial,
            });

        Assert.Equal(FixedSurcharge, surcharge);
    }

    [Fact]
    public void CartaoParcial_MaisDinheiro_MesmaRegraPremium()
    {
        using var _ = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        var cig = SeedMacoWithTable();

        var surcharge = PriceTablesService.CalcCartSurchargeAllocated(
            [Line(cig, MacoBase)],
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["credito"] = PixPartial,
                ["dinheiro"] = CashNeed,
            });

        Assert.Equal(FixedSurcharge, surcharge);
    }

    [Fact]
    public void Troco_Pix9_Dinheiro350_Recebido4_Troco050()
    {
        var parts = new List<PaymentPart>
        {
            new() { PaymentType = "PIX QR CODE", Amount = PixPartial },
            new() { PaymentType = "Dinheiro", Amount = CashNeed },
        };
        var r = SalePaymentCalculator.ResolveCashChange(
            parts, TotalWithPix, cashReceivedInput: 4.00,
            isCash: t => t.Contains("Dinheiro", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(4.00, r.CashReceived);
        Assert.Equal(0.50, r.ChangeAmount);
    }

    [Fact]
    public void Troco_Pix9_DinheiroExato350_SemTroco()
    {
        var parts = new List<PaymentPart>
        {
            new() { PaymentType = "PIX QR CODE", Amount = PixPartial },
            new() { PaymentType = "Dinheiro", Amount = CashNeed },
        };
        var r = SalePaymentCalculator.ResolveCashChange(
            parts, TotalWithPix, cashReceivedInput: CashNeed,
            isCash: t => t.Contains("Dinheiro", StringComparison.OrdinalIgnoreCase));

        Assert.Null(r.CashReceived);
        Assert.Equal(0, r.ChangeAmount);
    }

    [Fact]
    public void Troco_SoPix_CashReceivedNaoGeraTroco()
    {
        var r = SalePaymentCalculator.ResolveCashChange(
            [new PaymentPart { PaymentType = "PIX QR CODE", Amount = TotalWithPix }],
            TotalWithPix, cashReceivedInput: 20,
            isCash: t => t.Contains("Dinheiro", StringComparison.OrdinalIgnoreCase));

        Assert.Null(r.CashReceived);
        Assert.Equal(0, r.ChangeAmount);
    }

    [Fact]
    public void FinalizeSale_Split_PersistePartesETroco_CaixaFecha1250()
    {
        using var _ = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(50, "69nb-split");
        var cig = SeedMacoWithTable(stock: 100);

        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = cig,
                    Name = "ROTHMANS HAND SELECTED RED",
                    UnitPrice = MacoBase,
                    Quantity = 1,
                    StockUnitsPerSale = 20,
                },
            ],
            Discount = 0,
            Surcharge = FixedSurcharge,
            Payments =
            [
                new PdvPaymentPart { PaymentType = "PIX QR CODE", Amount = PixPartial },
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = CashNeed },
            ],
            CashReceived = 4.00,
            PaymentType = "Misto",
        });

        Assert.Equal(TotalWithPix, sale.Total);
        Assert.Equal(4.00, sale.CashReceived);
        Assert.Equal(0.50, sale.ChangeAmount);

        var movs = GetCashPaymentMovements(sale.SaleId);
        Assert.Contains(movs, m => m.Type.Contains("PIX", StringComparison.OrdinalIgnoreCase)
            && Math.Abs(m.AmountIn - PixPartial) < 0.02);
        Assert.Contains(movs, m => m.Type.Contains("Dinheiro", StringComparison.OrdinalIgnoreCase)
            && Math.Abs(m.AmountIn - CashNeed) < 0.02);

        // Receita líquida das formas = total da venda (troco não entra como receita).
        var paidIn = ProductPriceHelper.RoundPrice(movs.Sum(m => m.AmountIn));
        Assert.Equal(TotalWithPix, paidIn);
    }

    [Fact]
    public void FinalizeSale_AbaixoDoTotal_Bloqueado()
    {
        using var _ = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(50, "69nb-under");
        var cig = SeedMacoWithTable(stock: 100);

        var ex = Assert.Throws<PdvException>(() => PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = cig,
                    Name = "ROTHMANS HAND SELECTED RED",
                    UnitPrice = MacoBase,
                    Quantity = 1,
                    StockUnitsPerSale = 20,
                },
            ],
            Discount = 0,
            Surcharge = FixedSurcharge,
            Payments =
            [
                new PdvPaymentPart { PaymentType = "PIX QR CODE", Amount = PixPartial },
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 1.00 },
            ],
            CashReceived = 1.00,
            PaymentType = "Misto",
        }));
        Assert.Contains("difere", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UiSource_TeclaA_PreservaSplitPixParcial()
    {
        var src = File.ReadAllText(Path.Combine(AppSourceRoot(), "Views", "PdvPaymentWindow.xaml.cs"));
        Assert.Contains("PdvPaymentSplitRules.ShouldClearUnpaidPixOnMethodSwitch", src, StringComparison.Ordinal);
        Assert.Contains("PdvPaymentSplitRules.RemainingAmount", src, StringComparison.Ordinal);
        Assert.Contains("Já existe outra forma com valor", src, StringComparison.Ordinal);
        Assert.Contains("Split (PIX parcial", src, StringComparison.Ordinal);
        // Cancelamento explícito ainda zera via ClearPixAllocationIfUnpaid / ResetToSingleMethod
        Assert.Contains("ClearPixAllocationIfUnpaid()", src, StringComparison.Ordinal);
        Assert.Contains("ResetToSingleMethod(", src, StringComparison.Ordinal);
    }

    private static int SeedMacoWithTable(double stock = 0)
    {
        var table = PriceTablesService.Create(new PriceTableInput
        {
            Description = "TABELA 69NB MACO",
            SurchargePercent = 0,
            SurchargeFixed = FixedSurcharge,
            ApplyPaymentMethods = ["pix", "debito", "credito"],
            Active = true,
        });
        var extra = new ProductExtra
        {
            FatorEmbalagem = 20,
            QtdAtacado = 20,
            PrecoAvulso = 1.50,
            PrecoAtacado = MacoBase,
            PrecoCompra = 10,
            PriceTableId = table.Id,
        };
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                'CIG69NB', 'ROTHMANS HAND SELECTED RED', 'Cigarros', 'UN', $sale, $stock, 10, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$sale", MacoBase);
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$extra", extra.ToJson());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static (int ProductId, double UnitPrice, double Qty, double StockUnitsPerSale) Line(
        int productId, double unitPrice) =>
        (productId, unitPrice, 1, 20);

    private static Dictionary<string, double> Pay(string method, double amount) =>
        new(StringComparer.OrdinalIgnoreCase) { [method] = amount };

    private static List<(string Type, double AmountIn)> GetCashPaymentMovements(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(payment_type,''), IFNULL(amount_in,0)
            FROM cash_movements
            WHERE IFNULL(ref_type,'') = 'sale' AND ref_id = $id
              AND IFNULL(amount_in,0) > 0.009
            ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        var list = new List<(string, double)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add((reader.GetString(0), reader.GetDouble(1)));
        return list;
    }

    private static string AppSourceRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "src", "SGDB.App");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "src", "SGDB.App"));
    }
}
