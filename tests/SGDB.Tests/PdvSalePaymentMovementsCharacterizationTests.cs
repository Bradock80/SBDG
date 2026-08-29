using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 35 — Caracterização de cash_movements (notes/description de troco)
/// e paridade FinalizeSaleCore vs ApplySalePaymentUpdate (via ChangeSalePayment).
/// Não altera produção.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PdvSalePaymentMovementsCharacterizationTests
{
    public PdvSalePaymentMovementsCharacterizationTests()
    {
        TestDataHelper.SetSessionRole("admin");
    }

    // ── 2. Finalize dinheiro simples sem troco ───────────────────────

    [Fact]
    public void Finalize_DinheiroSimples_SemTroco()
    {
        using var _ = TempDatabase.Create();
        OpenCash();
        var pid = SeedProduct(100);

        var sale = Finalize(pid, qty: 5, unit: 10, parts: Pay("Dinheiro", 50), cashReceived: 50);

        Assert.Equal(50, sale.Total);
        Assert.Null(GetCashReceived(sale.SaleId));
        Assert.Equal(0, GetChangeAmount(sale.SaleId));
        Assert.Equal("Dinheiro", GetPaymentType(sale.SaleId));

        var movs = LoadMovements(sale.SaleId);
        Assert.Single(movs);
        Assert.Equal("venda", movs[0].Kind);
        Assert.Equal("Dinheiro", movs[0].PaymentType);
        Assert.Equal(50, movs[0].AmountIn);
        Assert.Equal(0, movs[0].AmountOut);
        Assert.True(movs[0].AffectsBalance);
        Assert.Equal("sale", movs[0].RefType);
        Assert.Equal(sale.SaleId, movs[0].RefId);
        Assert.True(string.IsNullOrEmpty(movs[0].Notes));
        Assert.Equal($"VENDA PDV #{sale.SaleId} — Dinheiro", movs[0].Description);
    }

    // ── 3. Finalize dinheiro com troco ────────────────────────────────

    [Fact]
    public void Finalize_Dinheiro_ComTroco_NotesEDescription()
    {
        using var _ = TempDatabase.Create();
        OpenCash();
        var pid = SeedProduct(100);

        var sale = Finalize(pid, qty: 5, unit: 10, parts: Pay("Dinheiro", 50), cashReceived: 100);

        Assert.Equal(100, GetCashReceived(sale.SaleId));
        Assert.Equal(50, GetChangeAmount(sale.SaleId));

        var movs = LoadMovements(sale.SaleId);
        Assert.Single(movs);
        Assert.Equal(50, movs[0].AmountIn);
        Assert.Equal("venda", movs[0].Kind);
        Assert.Contains("(recebido R$ 100,00, troco R$ 50,00)", movs[0].Description);
        Assert.Equal("{\"cash_received\":100,\"change\":50}", movs[0].Notes);
    }

    // ── 4. Finalize Dinheiro + Pix ───────────────────────────────────

    [Fact]
    public void Finalize_DinheiroMaisPix_SemTroco()
    {
        using var _ = TempDatabase.Create();
        OpenCash();
        var pid = SeedProduct(100);

        var sale = Finalize(
            pid, qty: 10, unit: 10,
            parts:
            [
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 40 },
                new PdvPaymentPart { PaymentType = "Pix", Amount = 60 },
            ],
            cashReceived: 0);

        Assert.Equal("DIN+PIX", GetPaymentType(sale.SaleId));
        var movs = LoadMovements(sale.SaleId);
        Assert.Equal(2, movs.Count);

        Assert.Equal("venda", movs[0].Kind);
        Assert.Equal("Dinheiro", movs[0].PaymentType);
        Assert.Equal(40, movs[0].AmountIn);
        Assert.True(movs[0].AffectsBalance);
        Assert.True(string.IsNullOrEmpty(movs[0].Notes));
        Assert.Equal($"VENDA PDV #{sale.SaleId} — Dinheiro R$ 40,00", movs[0].Description);

        Assert.Equal("venda", movs[1].Kind);
        Assert.Equal("Pix", movs[1].PaymentType);
        Assert.Equal(60, movs[1].AmountIn);
        Assert.True(movs[1].AffectsBalance);
        Assert.True(string.IsNullOrEmpty(movs[1].Notes));
        Assert.Equal($"VENDA PDV #{sale.SaleId} — Pix R$ 60,00", movs[1].Description);
    }

    [Fact]
    public void Finalize_DescricaoMonetaria_EstavelQuandoRunnerEnUs()
    {
        using var _ = TempDatabase.Create();
        using var culture = new CultureScope("en-US");
        OpenCash();
        var pid = SeedProduct(100);

        var sale = Finalize(
            pid, qty: 10, unit: 10,
            parts:
            [
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 40 },
                new PdvPaymentPart { PaymentType = "Pix", Amount = 60 },
            ],
            cashReceived: 0);

        var movs = LoadMovements(sale.SaleId);
        Assert.Equal($"VENDA PDV #{sale.SaleId} — Dinheiro R$ 40,00", movs[0].Description);
        Assert.Equal($"VENDA PDV #{sale.SaleId} — Pix R$ 60,00", movs[1].Description);
        Assert.DoesNotContain("40.00", movs[0].Description);
        Assert.DoesNotContain("60.00", movs[1].Description);
    }

    // ── 5. Finalize Dinheiro + Fiado ─────────────────────────────────

    [Fact]
    public void Finalize_DinheiroMaisFiado_28_50()
    {
        using var _ = TempDatabase.Create();
        OpenCash();
        var customerId = SeedCustomer("Cliente Misto 2850");
        // 1 un × 28,50
        var pid = TestDataHelper.SeedSimpleProduct(50, salePrice: 28.50, costPrice: 10, code: "M285", name: "Item 28.50");

        var sale = Finalize(
            pid, qty: 1, unit: 28.50,
            parts:
            [
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 20 },
                new PdvPaymentPart { PaymentType = "Fiado", Amount = 8.50 },
            ],
            cashReceived: 0,
            customerId: customerId);

        Assert.Equal(customerId, GetCustomerId(sale.SaleId));
        Assert.Equal(8.50, FiadoService.GetDetail(customerId).Balance);

        var movs = LoadMovements(sale.SaleId);
        Assert.Equal(2, movs.Count);

        Assert.Equal("venda", movs[0].Kind);
        Assert.Equal("Dinheiro", movs[0].PaymentType);
        Assert.Equal(20, movs[0].AmountIn);
        Assert.True(movs[0].AffectsBalance);

        Assert.Equal("venda_fiado", movs[1].Kind);
        Assert.Equal("Fiado", movs[1].PaymentType);
        Assert.Equal(8.50, movs[1].AmountIn);
        Assert.False(movs[1].AffectsBalance);
        Assert.Equal("Cliente Misto 2850", movs[1].PartyName);
        Assert.Contains("FIADO R$ 8,50", movs[1].Description);
        Assert.Contains("Cliente Misto 2850", movs[1].Description);
    }

    // ── 6. Finalize três formas ──────────────────────────────────────

    [Fact]
    public void Finalize_TresFormas_DinheiroPixFiado()
    {
        using var _ = TempDatabase.Create();
        OpenCash();
        var customerId = SeedCustomer("Cliente 3 Formas");
        var pid = SeedProduct(100);

        var sale = Finalize(
            pid, qty: 10, unit: 10,
            parts:
            [
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 40 },
                new PdvPaymentPart { PaymentType = "Pix", Amount = 30 },
                new PdvPaymentPart { PaymentType = "Fiado", Amount = 30 },
            ],
            cashReceived: 0,
            customerId: customerId);

        Assert.Equal("DIN+PIX+Fiado", GetPaymentType(sale.SaleId));
        var movs = LoadMovements(sale.SaleId);
        Assert.Equal(3, movs.Count);
        Assert.Equal(("venda", "Dinheiro", 40.0, true), (movs[0].Kind, movs[0].PaymentType, movs[0].AmountIn, movs[0].AffectsBalance));
        Assert.Equal(("venda", "Pix", 30.0, true), (movs[1].Kind, movs[1].PaymentType, movs[1].AmountIn, movs[1].AffectsBalance));
        Assert.Equal(("venda_fiado", "Fiado", 30.0, false), (movs[2].Kind, movs[2].PaymentType, movs[2].AmountIn, movs[2].AffectsBalance));
        Assert.All(movs, m => Assert.True(string.IsNullOrEmpty(m.Notes)));
    }

    // ── 7. Regressão: duas partes Dinheiro + troco (Finalize) ─────────

    [Fact]
    public void Finalize_DuasPartesDinheiro_ComTroco_UmaUnicaNotesNaPrimeira()
    {
        // Regra 35.1: notes só na primeira parte Dinheiro do payload.
        using var _ = TempDatabase.Create();
        OpenCash();
        var pid = SeedProduct(100);

        var sale = Finalize(
            pid, qty: 10, unit: 10,
            parts:
            [
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 40 },
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 60 },
            ],
            cashReceived: 150);

        Assert.Equal(150, GetCashReceived(sale.SaleId));
        Assert.Equal(50, GetChangeAmount(sale.SaleId));
        Assert.Equal("DIN+DIN", GetPaymentType(sale.SaleId));

        var movs = LoadMovements(sale.SaleId);
        Assert.Equal(2, movs.Count);
        Assert.Equal(40, movs[0].AmountIn);
        Assert.Equal(60, movs[1].AmountIn);
        Assert.Equal(1, movs.Count(m => !string.IsNullOrEmpty(m.Notes)));
        Assert.Equal("{\"cash_received\":150,\"change\":50}", movs[0].Notes);
        Assert.True(string.IsNullOrEmpty(movs[1].Notes));
        Assert.Contains("(recebido R$ 150,00, troco R$ 50,00)", movs[0].Description);
        Assert.DoesNotContain("troco", movs[1].Description, StringComparison.OrdinalIgnoreCase);
    }

    // ── 8. ChangePayment — duas partes Dinheiro + troco ──────────────

    [Fact]
    public void ChangePayment_DuasPartesDinheiro_ComTroco_UmaUnicaNotesNaPrimeira()
    {
        using var _ = TempDatabase.Create();
        OpenCash();
        var pid = SeedProduct(100);
        var sale = Finalize(pid, qty: 10, unit: 10, parts: Pay("Pix", 100), cashReceived: 0);

        PdvService.ChangeSalePayment(
            sale.SaleId,
            [
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 40 },
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 60 },
            ],
            cashReceived: 150);

        Assert.Equal(150, GetCashReceived(sale.SaleId));
        Assert.Equal(50, GetChangeAmount(sale.SaleId));

        var movs = LoadMovements(sale.SaleId);
        Assert.Equal(2, movs.Count);
        Assert.Equal(1, movs.Count(m => !string.IsNullOrEmpty(m.Notes)));
        Assert.Equal("{\"cash_received\":150,\"change\":50}", movs[0].Notes);
        Assert.True(string.IsNullOrEmpty(movs[1].Notes));
        Assert.Contains("(recebido R$ 150,00, troco R$ 50,00)", movs[0].Description);
        Assert.DoesNotContain("troco", movs[1].Description, StringComparison.OrdinalIgnoreCase);
    }

    // ── 9. Paridade — dinheiro simples ───────────────────────────────

    [Fact]
    public void Paridade_DinheiroSimples_FinalizeVsChange()
    {
        using var _ = TempDatabase.Create();
        OpenCash();
        var pid = SeedProduct(100);
        var parts = Pay("Dinheiro", 50);

        var fin = Finalize(pid, 5, 10, parts, cashReceived: 50);
        var finMovs = Snapshot(LoadMovements(fin.SaleId));

        var baseSale = Finalize(pid, 5, 10, Pay("Pix", 50), cashReceived: 0);
        PdvService.ChangeSalePayment(baseSale.SaleId, parts, cashReceived: 50);
        var chgMovs = Snapshot(LoadMovements(baseSale.SaleId));

        AssertMovementsEqual(finMovs, chgMovs, ignoreSaleIdInDescription: true);
        Assert.Equal(GetCashReceived(fin.SaleId), GetCashReceived(baseSale.SaleId));
        Assert.Equal(GetChangeAmount(fin.SaleId), GetChangeAmount(baseSale.SaleId));
        Assert.Equal(GetPaymentType(fin.SaleId), GetPaymentType(baseSale.SaleId));
    }

    // ── 10. Paridade — Dinheiro + Pix ────────────────────────────────

    [Fact]
    public void Paridade_DinheiroMaisPix_FinalizeVsChange()
    {
        using var _ = TempDatabase.Create();
        OpenCash();
        var pid = SeedProduct(100);
        var parts = new List<PdvPaymentPart>
        {
            new() { PaymentType = "Dinheiro", Amount = 40 },
            new() { PaymentType = "Pix", Amount = 60 },
        };

        var fin = Finalize(pid, 10, 10, parts, 0);
        var finMovs = Snapshot(LoadMovements(fin.SaleId));

        var baseSale = Finalize(pid, 10, 10, Pay("Pix", 100), 0);
        PdvService.ChangeSalePayment(baseSale.SaleId, parts, cashReceived: 0);
        var chgMovs = Snapshot(LoadMovements(baseSale.SaleId));

        AssertMovementsEqual(finMovs, chgMovs, ignoreSaleIdInDescription: true);
    }

    // ── 11. Paridade — Dinheiro + Fiado ──────────────────────────────

    [Fact]
    public void Paridade_DinheiroMaisFiado_FinalizeVsChange()
    {
        using var _ = TempDatabase.Create();
        OpenCash();
        var customerId = SeedCustomer("Cliente Paridade Fiado");
        var pid = TestDataHelper.SeedSimpleProduct(50, 28.50, 10, "PF", "Item PF");
        var parts = new List<PdvPaymentPart>
        {
            new() { PaymentType = "Dinheiro", Amount = 20 },
            new() { PaymentType = "Fiado", Amount = 8.50 },
        };

        var fin = Finalize(pid, 1, 28.50, parts, 0, customerId);
        var finMovs = Snapshot(LoadMovements(fin.SaleId));

        var baseSale = Finalize(pid, 1, 28.50, Pay("Pix", 28.50), 0, customerId);
        PdvService.ChangeSalePayment(baseSale.SaleId, parts, cashReceived: 0, customerPersonId: customerId);
        var chgMovs = Snapshot(LoadMovements(baseSale.SaleId));

        AssertMovementsEqual(finMovs, chgMovs, ignoreSaleIdInDescription: true);
        Assert.Equal(GetCustomerId(fin.SaleId), GetCustomerId(baseSale.SaleId));
        Assert.Equal(customerId, GetCustomerId(baseSale.SaleId));
        Assert.Equal(8.50, finMovs.Single(m => m.Kind == "venda_fiado").AmountIn);
        Assert.Equal(8.50, chgMovs.Single(m => m.Kind == "venda_fiado").AmountIn);
        Assert.False(finMovs.Single(m => m.Kind == "venda_fiado").AffectsBalance);
        Assert.False(chgMovs.Single(m => m.Kind == "venda_fiado").AffectsBalance);
    }

    // ── 12. Paridade — 3 formas ──────────────────────────────────────

    [Fact]
    public void Paridade_TresFormas_FinalizeVsChange()
    {
        using var _ = TempDatabase.Create();
        OpenCash();
        var customerId = SeedCustomer("Cliente 3P");
        var pid = SeedProduct(100);
        var parts = new List<PdvPaymentPart>
        {
            new() { PaymentType = "Dinheiro", Amount = 40 },
            new() { PaymentType = "Pix", Amount = 30 },
            new() { PaymentType = "Fiado", Amount = 30 },
        };

        var fin = Finalize(pid, 10, 10, parts, 0, customerId);
        var finMovs = Snapshot(LoadMovements(fin.SaleId));

        var baseSale = Finalize(pid, 10, 10, Pay("Pix", 100), 0, customerId);
        PdvService.ChangeSalePayment(baseSale.SaleId, parts, 0, customerId);
        var chgMovs = Snapshot(LoadMovements(baseSale.SaleId));

        AssertMovementsEqual(finMovs, chgMovs, ignoreSaleIdInDescription: true);
        Assert.Equal(GetPaymentType(fin.SaleId), GetPaymentType(baseSale.SaleId));
    }

    // ── 13. Paridade — troco (1× Dinheiro) ───────────────────────────

    [Fact]
    public void Paridade_Troco_UmaParteDinheiro_FinalizeVsChange()
    {
        using var _ = TempDatabase.Create();
        OpenCash();
        var pid = SeedProduct(100);
        var parts = Pay("Dinheiro", 50);

        var fin = Finalize(pid, 5, 10, parts, cashReceived: 100);
        var finMovs = Snapshot(LoadMovements(fin.SaleId));

        var baseSale = Finalize(pid, 5, 10, Pay("Pix", 50), 0);
        PdvService.ChangeSalePayment(baseSale.SaleId, parts, cashReceived: 100);
        var chgMovs = Snapshot(LoadMovements(baseSale.SaleId));

        AssertMovementsEqual(finMovs, chgMovs, ignoreSaleIdInDescription: true);
        Assert.Equal(100, GetCashReceived(fin.SaleId));
        Assert.Equal(100, GetCashReceived(baseSale.SaleId));
        Assert.Equal(50, GetChangeAmount(fin.SaleId));
        Assert.Equal(50, GetChangeAmount(baseSale.SaleId));
        Assert.Equal("{\"cash_received\":100,\"change\":50}", finMovs[0].Notes);
        Assert.Equal(finMovs[0].Notes, chgMovs[0].Notes);
    }

    // ── 13b. Paridade — troco com 2× Dinheiro (unificado) ────────────

    [Fact]
    public void Paridade_Troco_DuasPartesDinheiro_FinalizeVsChange_Iguais()
    {
        using var _ = TempDatabase.Create();
        OpenCash();
        var pid = SeedProduct(100);
        var parts = new List<PdvPaymentPart>
        {
            new() { PaymentType = "Dinheiro", Amount = 40 },
            new() { PaymentType = "Dinheiro", Amount = 60 },
        };

        var fin = Finalize(pid, 10, 10, parts, cashReceived: 150);
        var finMovs = Snapshot(LoadMovements(fin.SaleId));

        var baseSale = Finalize(pid, 10, 10, Pay("Pix", 100), 0);
        PdvService.ChangeSalePayment(baseSale.SaleId, parts, cashReceived: 150);
        var chgMovs = Snapshot(LoadMovements(baseSale.SaleId));

        AssertMovementsEqual(finMovs, chgMovs, ignoreSaleIdInDescription: true);
        Assert.Equal(GetCashReceived(fin.SaleId), GetCashReceived(baseSale.SaleId));
        Assert.Equal(GetChangeAmount(fin.SaleId), GetChangeAmount(baseSale.SaleId));
        Assert.Equal(1, finMovs.Count(m => !string.IsNullOrEmpty(m.Notes)));
        Assert.Equal(finMovs[0].Notes, chgMovs[0].Notes);
        Assert.Equal("{\"cash_received\":150,\"change\":50}", finMovs[0].Notes);
        Assert.True(string.IsNullOrEmpty(finMovs[1].Notes));
    }

    // ── 14. Customer: Finalize throw se pessoa inexistente ────────────

    [Fact]
    public void Finalize_CustomerInexistente_Lanca()
    {
        using var _ = TempDatabase.Create();
        OpenCash();
        var pid = SeedProduct(10);
        var ex = Assert.Throws<PdvException>(() =>
            Finalize(pid, 1, 10, Pay("Dinheiro", 10), 10, customerId: 999999));
        Assert.Contains("Cliente não encontrado", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── helpers ──────────────────────────────────────────────────────

    private static void OpenCash() => CashService.OpenSession(50, "etapa35");

    private static int SeedProduct(double stock) =>
        TestDataHelper.SeedSimpleProduct(stock, salePrice: 10, costPrice: 4,
            code: $"P{Guid.NewGuid():N}"[..8], name: "Prod E35");

    private static List<PdvPaymentPart> Pay(string type, double amount) =>
        [new PdvPaymentPart { PaymentType = type, Amount = amount }];

    private static PdvFinalizeResult Finalize(
        int productId, double qty, double unit,
        IReadOnlyList<PdvPaymentPart> parts, double cashReceived, int? customerId = null)
    {
        return PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = productId,
                    Quantity = qty,
                    UnitPrice = unit,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = parts[0].PaymentType,
            Payments = parts,
            CashReceived = cashReceived,
            CustomerPersonId = customerId,
        });
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

    private sealed record MovRow(
        string Kind, string PaymentType, double AmountIn, double AmountOut,
        bool AffectsBalance, string RefType, int RefId, string? Notes,
        string Description, string? PartyName);

    private static List<MovRow> LoadMovements(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT kind, IFNULL(payment_type,''), IFNULL(amount_in,0), IFNULL(amount_out,0),
                   IFNULL(affects_balance,1), IFNULL(ref_type,''), IFNULL(ref_id,0),
                   notes, description, party_name
            FROM cash_movements
            WHERE IFNULL(ref_type,'') = 'sale' AND ref_id = $id
            ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        var list = new List<MovRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new MovRow(
                r.GetString(0),
                r.GetString(1),
                r.GetDouble(2),
                r.GetDouble(3),
                r.GetInt32(4) != 0,
                r.GetString(5),
                r.GetInt32(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9)));
        }
        return list;
    }

    private static List<MovRow> Snapshot(List<MovRow> rows) =>
        rows.Select(m => m with { RefId = 0 }).ToList();

    private static void AssertMovementsEqual(
        List<MovRow> a, List<MovRow> b, bool ignoreSaleIdInDescription)
    {
        Assert.Equal(a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Kind, b[i].Kind);
            Assert.Equal(a[i].PaymentType, b[i].PaymentType);
            Assert.Equal(a[i].AmountIn, b[i].AmountIn);
            Assert.Equal(a[i].AmountOut, b[i].AmountOut);
            Assert.Equal(a[i].AffectsBalance, b[i].AffectsBalance);
            Assert.Equal(a[i].Notes, b[i].Notes);
            Assert.Equal(a[i].PartyName, b[i].PartyName);
            if (ignoreSaleIdInDescription)
            {
                Assert.Equal(StripSaleId(a[i].Description), StripSaleId(b[i].Description));
            }
            else
            {
                Assert.Equal(a[i].Description, b[i].Description);
            }
        }
    }

    private static string StripSaleId(string desc) =>
        System.Text.RegularExpressions.Regex.Replace(desc, @"#\d+", "#N");

    private static string GetPaymentType(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payment_type FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return (string)(cmd.ExecuteScalar() ?? "");
    }

    private static double? GetCashReceived(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT cash_received FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Convert.ToDouble(v);
    }

    private static double GetChangeAmount(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(change_amount,0) FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    private static int? GetCustomerId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT customer_id FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Convert.ToInt32(v);
    }
}
