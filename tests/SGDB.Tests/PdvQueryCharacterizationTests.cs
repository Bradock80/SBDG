using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// Congela o comportamento atual das queries históricas do PDV
/// (ListSales / GetSaleDetail / GetResumoDia e variantes *Local)
/// via PdvQueryService.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PdvQueryCharacterizationTests
{
    private static void EnsureStandalone() =>
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

    private static void OpenCash(double opening = 100) =>
        CashService.OpenSession(openingAmount: opening, notes: "query-char");

    // ——— ListSalesLocal ———

    [Fact]
    public void ListSalesLocal_Basico_RetornaCamposEssenciais()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        OpenCash();
        TestDataHelper.SetSessionRole("admin");

        var productId = TestDataHelper.SeedSimpleProduct(50, 10, 4, "L1", "Lista Basico");
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 2, 10, 20);

        var rows = PdvQueryService.ListSalesLocal(includeCancelled: true);
        var row = Assert.Single(rows, r => r.Id == sale.SaleId);

        Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd"), row.SessionDate);
        Assert.Equal(20, row.Total);
        Assert.Equal("Dinheiro", row.PaymentType);
        Assert.Null(row.CustomerName);
        Assert.Null(row.SellerName);
        Assert.Equal(1, row.ItemsCount);
        Assert.False(row.Cancelled);
        Assert.Equal("Dinheiro", row.PaymentLabel);
    }

    [Fact]
    public void ListSalesLocal_Multiplas_OrdenacaoFiltroECancelada()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        OpenCash();
        TestDataHelper.SetSessionRole("admin");
        TestDataHelper.GrantPdvCancelPermission();

        var p = TestDataHelper.SeedSimpleProduct(80, 10, 4, "M1", "Multi");
        var first = TestDataHelper.FinalizeSimpleCashSale(p, 1, 10, 10);
        var second = TestDataHelper.FinalizeSimpleCashSale(p, 1, 10, 10);
        var third = TestDataHelper.FinalizeSimpleCashSale(p, 1, 10, 10);
        PdvService.CancelSale(third.SaleId);

        var withCancelled = PdvQueryService.ListSalesLocal(includeCancelled: true);
        Assert.Equal(3, withCancelled.Count);
        // ORDER BY created_at DESC, id DESC → mais recente primeiro
        Assert.Equal(third.SaleId, withCancelled[0].Id);
        Assert.True(withCancelled[0].Cancelled);
        Assert.Equal(second.SaleId, withCancelled[1].Id);
        Assert.Equal(first.SaleId, withCancelled[2].Id);

        var activeOnly = PdvQueryService.ListSalesLocal(includeCancelled: false);
        Assert.Equal(2, activeOnly.Count);
        Assert.DoesNotContain(activeOnly, r => r.Id == third.SaleId);

        // Filtro por data explícita fora do dia → vazio
        var otherDay = PdvQueryService.ListSalesLocal(DateTime.Today.AddYears(-1), includeCancelled: true);
        Assert.Empty(otherDay);

        var today = PdvQueryService.ListSalesLocal(DateTime.Today, includeCancelled: true);
        Assert.Equal(3, today.Count);
    }

    [Fact]
    public void ListSalesLocal_PagamentoMisto_LabelDIN_PIX()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        OpenCash();

        var p = TestDataHelper.SeedSimpleProduct(40, 10, 4, "MIX", "Misto");
        var sale = FinalizeMixed(p, dinheiro: 12, pix: 8);

        var row = Assert.Single(PdvQueryService.ListSalesLocal(includeCancelled: false), r => r.Id == sale.SaleId);
        Assert.Equal(20, row.Total);
        Assert.Equal("DIN+PIX", row.PaymentType);
        Assert.Equal("DIN+PIX", row.PaymentLabel);
    }

    [Fact]
    public void ListSalesLocal_ClienteEVendedor_NomesRetornados()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        OpenCash();

        var customerId = SeedCustomer("Cliente Lista");
        var seller = SellersService.Create(new SellerInput { Code = "V1", Name = "Vendedor Lista" });
        var p = TestDataHelper.SeedSimpleProduct(30, 15, 5, "CV", "Com Cliente");

        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = p,
                    Code = "CV",
                    Name = "Com Cliente",
                    Unit = "UN",
                    Quantity = 1,
                    UnitPrice = 15,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = 15,
            CustomerPersonId = customerId,
            SellerId = seller.Id,
        });

        var row = Assert.Single(PdvQueryService.ListSalesLocal(), r => r.Id == sale.SaleId);
        Assert.Equal("Cliente Lista", row.CustomerName);
        // SellersService grava nome em maiúsculas
        Assert.Equal("VENDEDOR LISTA", row.SellerName);
    }

    // ——— GetSaleDetailLocal ———

    [Fact]
    public void GetSaleDetailLocal_CabecalhoItensEStockQtyNoBanco()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        OpenCash();

        var p = TestDataHelper.SeedSimpleProduct(100, 10, 4, "D1", "Detalhe Item");
        // Fator 20: tela 1 → stock_qty 20 gravado; DTO de detalhe NÃO expõe stock_qty
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = p,
                    Code = "D1",
                    Name = "Detalhe Item",
                    Unit = "UN",
                    Quantity = 1,
                    UnitPrice = 10,
                    StockUnitsPerSale = 20,
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = 10,
        });

        var detail = PdvQueryService.GetSaleDetailLocal(sale.SaleId);
        Assert.Equal(sale.SaleId, detail.Id);
        Assert.Equal(10, detail.Total);
        Assert.Equal("Dinheiro", detail.PaymentType);
        // Sem troco: ResolveCashTroco grava cash_received/change como null/0
        Assert.Null(detail.CashReceived);
        Assert.True((detail.ChangeAmount ?? 0) < 0.01);
        Assert.Null(detail.CustomerName);
        Assert.Null(detail.SellerName);
        Assert.False(detail.Cancelled);
        Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd"), detail.SessionDate);
        Assert.Null(detail.CustomerPersonId);

        var item = Assert.Single(detail.Items);
        Assert.Equal(p, item.ProductId);
        Assert.Equal("Detalhe Item", item.ProductName);
        Assert.Equal(1, item.Quantity);
        Assert.Equal(10, item.UnitPrice);
        Assert.Equal(10, item.Subtotal);
        // Caracterização: PdvSaleItemRow não tem StockQty — protege gap na extração
        Assert.Null(typeof(PdvSaleItemRow).GetProperty("StockQty"));

        Assert.Equal(20, ReadStockQty(sale.SaleId));
    }

    [Fact]
    public void GetSaleDetailLocal_PagamentosMistos()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        OpenCash();

        var p = TestDataHelper.SeedSimpleProduct(40, 10, 4, "PM", "Pag Misto");
        var sale = FinalizeMixed(p, dinheiro: 7, pix: 13);

        var detail = PdvQueryService.GetSaleDetailLocal(sale.SaleId);
        Assert.Equal("DIN+PIX", detail.PaymentType);
        Assert.Equal(2, detail.Payments.Count);
        Assert.Contains(detail.Payments, x => x.PaymentType == "Dinheiro" && x.Amount == 7);
        Assert.Contains(detail.Payments, x => x.PaymentType == "Pix" && x.Amount == 13);
    }

    [Fact]
    public void GetSaleDetailLocal_FiadoEMistoComCliente()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        OpenCash();

        var customerId = SeedCustomer("Cliente Fiado Query");
        var p = TestDataHelper.SeedSimpleProduct(40, 10, 4, "FF", "Fiado Prod");

        var puro = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = p, Code = "FF", Name = "Fiado Prod", Unit = "UN",
                    Quantity = 1, UnitPrice = 10, StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Fiado",
            Payments = [new PdvPaymentPart { PaymentType = "Fiado", Amount = 10 }],
            CustomerPersonId = customerId,
            CashReceived = 0,
        });

        var detailPuro = PdvQueryService.GetSaleDetailLocal(puro.SaleId);
        Assert.Equal("Fiado", detailPuro.PaymentType);
        Assert.Equal(customerId, detailPuro.CustomerPersonId);
        Assert.Equal("Cliente Fiado Query", detailPuro.CustomerName);
        Assert.Equal(10, detailPuro.Total);
        var partFiado = Assert.Single(detailPuro.Payments);
        Assert.Equal("Fiado", partFiado.PaymentType);
        Assert.Equal(10, partFiado.Amount);

        var misto = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = p, Code = "FF", Name = "Fiado Prod", Unit = "UN",
                    Quantity = 2, UnitPrice = 10, StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Dinheiro",
            Payments =
            [
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 5 },
                new PdvPaymentPart { PaymentType = "Fiado", Amount = 15 },
            ],
            CustomerPersonId = customerId,
            CashReceived = 5,
        });

        var detailMisto = PdvQueryService.GetSaleDetailLocal(misto.SaleId);
        Assert.Equal("DIN+Fiado", detailMisto.PaymentType);
        Assert.Equal(2, detailMisto.Payments.Count);
        Assert.Contains(detailMisto.Payments, x => x.PaymentType == "Dinheiro" && x.Amount == 5);
        Assert.Contains(detailMisto.Payments, x => x.PaymentType == "Fiado" && x.Amount == 15);
    }

    [Fact]
    public void GetSaleDetailLocal_Cancelada_CancelledTrueComItens()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        OpenCash();
        TestDataHelper.GrantPdvCancelPermission();

        var p = TestDataHelper.SeedSimpleProduct(30, 10, 4, "CX", "Cancelavel");
        var sale = TestDataHelper.FinalizeSimpleCashSale(p, 1, 10, 10);
        PdvService.CancelSale(sale.SaleId);

        var detail = PdvQueryService.GetSaleDetailLocal(sale.SaleId);
        Assert.True(detail.Cancelled);
        Assert.Equal(10, detail.Total);
        Assert.Single(detail.Items);
        // Pagamentos: após cancel, movimentos de caixa da venda são removidos —
        // LoadSalePayments cai no fallback (payment_type da sale + total).
        Assert.NotEmpty(detail.Payments);
    }

    [Fact]
    public void GetSaleDetailLocal_Inexistente_LancaPdvException()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();

        var ex = Assert.Throws<PdvException>(() => PdvQueryService.GetSaleDetailLocal(999_999));
        Assert.Equal("Venda não encontrada.", ex.Message);
    }

    // ——— GetResumoDiaLocal ———

    [Fact]
    public void GetResumoDiaLocal_FaturamentoFormasEAgregados()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        OpenCash(opening: 80);

        var bebidas = SeedProductWithGroup(50, 10, 4, "B1", "Refrigerante", "Bebidas");
        var snacks = SeedProductWithGroup(50, 5, 2, "S1", "Salgadinho", "Snacks");
        var customerId = SeedCustomer("Cliente Resumo");

        TestDataHelper.FinalizeSimpleCashSale(bebidas, 2, 10, 20); // 20 Dinheiro
        FinalizePix(snacks, qty: 3, unitPrice: 5); // 15 Pix
        FinalizeMixed(bebidas, dinheiro: 4, pix: 6); // 10 DIN+PIX
        PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = snacks, Code = "S1", Name = "Salgadinho", Unit = "UN",
                    Quantity = 1, UnitPrice = 5, StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Fiado",
            Payments = [new PdvPaymentPart { PaymentType = "Fiado", Amount = 5 }],
            CustomerPersonId = customerId,
        });

        var resumo = PdvQueryService.GetResumoDiaLocal();

        Assert.True(resumo.CaixaOpen);
        Assert.Equal(80, resumo.EntradaCaixa);
        Assert.Equal(50, resumo.Faturamento); // 20+15+10+5
        Assert.Equal(4, resumo.QtdVendas);
        Assert.Equal(12.5, resumo.TicketMedio);
        Assert.Equal(0, resumo.QtdCancelados);
        Assert.Equal(5, resumo.FiadoTotal);
        Assert.Equal(1, resumo.FiadoCount);

        // Formas via ListSales (payment_type gravado): Dinheiro, Pix, DIN+PIX, Fiado
        Assert.Contains(resumo.Formas, f => f.Forma == "Dinheiro" && f.Total == 20 && f.Count == 1);
        Assert.Contains(resumo.Formas, f => f.Forma == "Pix" && f.Total == 15 && f.Count == 1);
        Assert.Contains(resumo.Formas, f => f.Forma == "DIN+PIX" && f.Total == 10 && f.Count == 1);
        Assert.Contains(resumo.Formas, f => f.Forma == "Fiado" && f.Total == 5 && f.Count == 1);

        Assert.Contains(resumo.Grupos, g => g.GroupName == "Bebidas" && g.Total == 30); // 20+10
        Assert.Contains(resumo.Grupos, g => g.GroupName == "Snacks" && g.Total == 20); // 15+5

        Assert.Contains(resumo.TopProdutos, t => t.ProductName == "Refrigerante" && t.Qty == 3 && t.Total == 30);
        Assert.True(resumo.LucroReal > 0);
        Assert.True(resumo.MargemPercent > 0);
        Assert.True(resumo.SaldoGaveta > 0);
    }

    [Fact]
    public void GetResumoDiaLocal_Cancelamento_ExcluiDoFaturamentoEFormas()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        OpenCash();
        TestDataHelper.GrantPdvCancelPermission();

        var p = SeedProductWithGroup(40, 10, 4, "CAN", "Cancel Resumo", "Geral");
        var keep = TestDataHelper.FinalizeSimpleCashSale(p, 1, 10, 10);
        var cancel = TestDataHelper.FinalizeSimpleCashSale(p, 2, 10, 20);
        PdvService.CancelSale(cancel.SaleId);

        var resumo = PdvQueryService.GetResumoDiaLocal();
        Assert.Equal(10, resumo.Faturamento);
        Assert.Equal(1, resumo.QtdVendas);
        Assert.Equal(1, resumo.QtdCancelados); // 1 item na venda cancelada → +1 por linha do JOIN
        Assert.DoesNotContain(resumo.Formas, f => f.Total == 20);
        Assert.Contains(resumo.Formas, f => f.Forma == "Dinheiro" && f.Total == 10 && f.Count == 1);
        Assert.Contains(resumo.TopProdutos, t => t.ProductName == "Cancel Resumo" && t.Qty == 1 && t.Total == 10);

        // Sale ativa ainda listável
        Assert.Contains(PdvQueryService.ListSalesLocal(includeCancelled: false), r => r.Id == keep.SaleId);
    }

    // ——— Paridade público ↔ Local (standalone) ———

    [Fact]
    public void Paridade_PublicoIgualAosLocal_EmStandalone()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        OpenCash();

        var p = TestDataHelper.SeedSimpleProduct(30, 10, 4, "PAR", "Paridade");
        var sale = FinalizeMixed(p, dinheiro: 6, pix: 4);

        var listLocal = PdvQueryService.ListSalesLocal(includeCancelled: true);
        var listPublic = PdvQueryService.ListSales(includeCancelled: true);
        Assert.Equal(listLocal.Count, listPublic.Count);
        for (var i = 0; i < listLocal.Count; i++)
        {
            Assert.Equal(listLocal[i].Id, listPublic[i].Id);
            Assert.Equal(listLocal[i].Total, listPublic[i].Total);
            Assert.Equal(listLocal[i].PaymentType, listPublic[i].PaymentType);
            Assert.Equal(listLocal[i].Cancelled, listPublic[i].Cancelled);
            Assert.Equal(listLocal[i].ItemsCount, listPublic[i].ItemsCount);
        }

        var detailLocal = PdvQueryService.GetSaleDetailLocal(sale.SaleId);
        var detailPublic = PdvQueryService.GetSaleDetail(sale.SaleId);
        Assert.Equal(detailLocal.Id, detailPublic.Id);
        Assert.Equal(detailLocal.Total, detailPublic.Total);
        Assert.Equal(detailLocal.PaymentType, detailPublic.PaymentType);
        Assert.Equal(detailLocal.Payments.Count, detailPublic.Payments.Count);
        Assert.Equal(detailLocal.Items.Count, detailPublic.Items.Count);

        var resumoLocal = PdvQueryService.GetResumoDiaLocal();
        var resumoPublic = PdvQueryService.GetResumoDia();
        Assert.Equal(resumoLocal.Faturamento, resumoPublic.Faturamento);
        Assert.Equal(resumoLocal.QtdVendas, resumoPublic.QtdVendas);
        Assert.Equal(resumoLocal.TicketMedio, resumoPublic.TicketMedio);
        Assert.Equal(resumoLocal.Formas.Count, resumoPublic.Formas.Count);
        Assert.Equal(resumoLocal.EntradaCaixa, resumoPublic.EntradaCaixa);
        Assert.Equal(resumoLocal.CaixaOpen, resumoPublic.CaixaOpen);
    }

    /// <summary>
    /// GetResumoDiaLocal monta formas via ListSalesLocal (ETAPA 30).
    /// </summary>
    [Fact]
    public void GetResumoDiaLocal_FormasEquivalentesAListSalesLocal_Standalone()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        OpenCash();

        var p = TestDataHelper.SeedSimpleProduct(20, 10, 4, "RISK", "Risco ListSales");
        TestDataHelper.FinalizeSimpleCashSale(p, 1, 10, 10);
        FinalizePix(p, 1, 10);

        var resumo = PdvQueryService.GetResumoDiaLocal();
        var list = PdvQueryService.ListSalesLocal(includeCancelled: false);
        Assert.Equal(list.Count, resumo.QtdVendas);
        Assert.Equal(list.Sum(r => r.Total), resumo.Formas.Sum(f => f.Total));
    }

    // ——— helpers ———

    private static PdvFinalizeResult FinalizeMixed(int productId, double dinheiro, double pix)
    {
        var total = dinheiro + pix;
        return PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = productId,
                    Code = "X",
                    Name = "X",
                    Unit = "UN",
                    Quantity = 1,
                    UnitPrice = total,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Dinheiro",
            Payments =
            [
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = dinheiro },
                new PdvPaymentPart { PaymentType = "Pix", Amount = pix },
            ],
            CashReceived = dinheiro,
        });
    }

    private static PdvFinalizeResult FinalizePix(int productId, double qty, double unitPrice)
    {
        var total = qty * unitPrice;
        return PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = productId,
                    Code = "PIX",
                    Name = "Pix Item",
                    Unit = "UN",
                    Quantity = qty,
                    UnitPrice = unitPrice,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Pix",
            Payments = [new PdvPaymentPart { PaymentType = "Pix", Amount = total }],
            CashReceived = 0,
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

    private static int SeedProductWithGroup(
        double stock, double sale, double cost, string code, string name, string group)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                $code, $name, $group, 'UN', $sale, $stock, $cost, 1, '{}'
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$group", group);
        cmd.Parameters.AddWithValue("$sale", sale);
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$cost", cost);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static double ReadStockQty(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(stock_qty, 0) FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }
}
