using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// Caracterização do comportamento atual de PdvService.ChangeSalePayment.
/// Não altera produção — documenta efeitos observados no DB.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PdvChangeSalePaymentTests
{
    private static void EnsureStandalone()
    {
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
    }

    [Fact]
    public void ChangeSalePayment_DinheiroParaPix_AtualizaFormaECaixa_SemMexerEstoque()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(100, 10, 4);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 3, 10, 30);
        var stockBefore = TestDataHelper.GetProductStock(productId);
        var movStockBefore = CountStockMovements(sale.SaleId);
        var itemsBefore = CountSaleItems(sale.SaleId);

        var detail = PdvService.ChangeSalePayment(
            sale.SaleId,
            [new PdvPaymentPart { PaymentType = "Pix", Amount = 30 }]);

        Assert.Equal(sale.SaleId, detail.Id);
        Assert.Equal(30, detail.Total);
        Assert.Equal("Pix", GetPaymentType(sale.SaleId));
        Assert.Null(GetCashReceived(sale.SaleId));
        Assert.Equal(0, GetChangeAmount(sale.SaleId));

        var cash = GetCashMovements(sale.SaleId);
        Assert.Single(cash);
        Assert.Equal("Pix", cash[0].PaymentType);
        Assert.Equal(30, cash[0].AmountIn);
        Assert.Equal("venda", cash[0].Kind.ToLowerInvariant());

        Assert.Equal(stockBefore, TestDataHelper.GetProductStock(productId));
        Assert.Equal(movStockBefore, CountStockMovements(sale.SaleId));
        Assert.Equal(itemsBefore, CountSaleItems(sale.SaleId));
        Assert.Equal(30, GetSaleTotal(sale.SaleId));
    }

    [Fact]
    public void ChangeSalePayment_PixParaDinheiro_CriaMovimentoDinheiro()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(50, 10, 4);
        var sale = FinalizeSale(productId, 2, 10, "Pix", cashReceived: 0);

        var detail = PdvService.ChangeSalePayment(
            sale.SaleId,
            [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 20 }],
            cashReceived: 20);

        Assert.Equal("Dinheiro", GetPaymentType(sale.SaleId));
        var cash = GetCashMovements(sale.SaleId);
        Assert.Single(cash);
        Assert.Equal("Dinheiro", cash[0].PaymentType);
        Assert.Equal(20, cash[0].AmountIn);
        Assert.Equal(48, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void ChangeSalePayment_Misto_DinheiroMaisPix_GeraDoisMovimentos()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(40, 15, 5);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 2, 15, 30);

        var detail = PdvService.ChangeSalePayment(
            sale.SaleId,
            [
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 10 },
                new PdvPaymentPart { PaymentType = "Pix", Amount = 20 },
            ],
            cashReceived: 10);

        Assert.Equal(30, detail.Total);
        var label = GetPaymentType(sale.SaleId);
        Assert.Contains("DIN", label.ToUpperInvariant());
        Assert.Contains("PIX", label.ToUpperInvariant());

        var cash = GetCashMovements(sale.SaleId);
        Assert.Equal(2, cash.Count);
        Assert.Contains(cash, c => c.PaymentType == "Dinheiro" && c.AmountIn == 10);
        Assert.Contains(cash, c => c.PaymentType == "Pix" && c.AmountIn == 20);
        Assert.All(cash, c => Assert.Equal("venda", c.Kind.ToLowerInvariant()));
    }

    [Fact]
    public void ChangeSalePayment_DinheiroParaFiado_ExigeCliente_ESaldoDerivado()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(30, 10, 4);
        var customerId = SeedCustomer("Cliente Fiado");
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 1, 10, 10);

        var ex = Assert.Throws<PdvException>(() =>
            PdvService.ChangeSalePayment(
                sale.SaleId,
                [new PdvPaymentPart { PaymentType = "Fiado", Amount = 10 }]));
        Assert.Contains("cliente", ex.Message, StringComparison.OrdinalIgnoreCase);

        var detail = PdvService.ChangeSalePayment(
            sale.SaleId,
            [new PdvPaymentPart { PaymentType = "Fiado", Amount = 10 }],
            customerPersonId: customerId);

        Assert.Equal("Fiado", GetPaymentType(sale.SaleId));
        Assert.Equal(customerId, GetCustomerId(sale.SaleId));
        Assert.Equal(0, Count("fiado_payments")); // saldo derivado da venda, não INSERT em fiado_payments

        var cash = GetCashMovements(sale.SaleId);
        Assert.Single(cash);
        Assert.Equal("venda_fiado", cash[0].Kind.ToLowerInvariant());
        Assert.False(cash[0].AffectsBalance);

        var fiado = FiadoService.GetDetail(customerId);
        Assert.Equal(10, fiado.Balance);
    }

    [Fact]
    public void ChangeSalePayment_FiadoParaDinheiro_PreservaCustomerId_EZeraSaldoFiado()
    {
        // Antes (bug): customer_id era limpo ao sair do fiado.
        // Depois (correção): cliente da venda permanece; saldo fiado some (payment_type não é mais Fiado).
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(30, 10, 4);
        var customerId = SeedCustomer("Cliente Fiado 2");
        var sale = FinalizeSale(productId, 1, 10, "Fiado", cashReceived: 0, customerId);

        Assert.Equal(10, FiadoService.GetDetail(customerId).Balance);

        PdvService.ChangeSalePayment(
            sale.SaleId,
            [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 10 }],
            cashReceived: 10);

        Assert.Equal("Dinheiro", GetPaymentType(sale.SaleId));
        Assert.Equal(customerId, GetCustomerId(sale.SaleId));
        Assert.Equal(0, FiadoService.GetDetail(customerId).Balance);

        var cash = GetCashMovements(sale.SaleId);
        Assert.Single(cash);
        Assert.Equal("venda", cash[0].Kind.ToLowerInvariant());
        Assert.True(cash[0].AffectsBalance);
    }

    [Fact]
    public void ChangeSalePayment_PixComCliente_ParaDinheiro_PreservaCustomerId()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(30, 10, 4);
        var customerId = SeedCustomer("Cliente PIX");
        var sale = FinalizeSale(productId, 1, 10, "Pix", 0, customerId);

        PdvService.ChangeSalePayment(
            sale.SaleId,
            [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 10 }],
            cashReceived: 10);

        Assert.Equal(customerId, GetCustomerId(sale.SaleId));
        Assert.Equal("Dinheiro", GetPaymentType(sale.SaleId));
    }

    [Fact]
    public void ChangeSalePayment_DinheiroComCliente_ParaFiado_UsaClienteExistente()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(30, 10, 4);
        var customerId = SeedCustomer("Cliente Existente");
        var sale = FinalizeSale(productId, 1, 10, "Dinheiro", 10, customerId);

        // customerPersonId null → usa existingCustomerId
        PdvService.ChangeSalePayment(
            sale.SaleId,
            [new PdvPaymentPart { PaymentType = "Fiado", Amount = 10 }]);

        Assert.Equal(customerId, GetCustomerId(sale.SaleId));
        Assert.Equal("Fiado", GetPaymentType(sale.SaleId));
        Assert.Equal(10, FiadoService.GetDetail(customerId).Balance);
    }

    [Fact]
    public void ChangeSalePayment_Troco_GravaCashReceivedEChangeAmount()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(20, 10, 4);
        var sale = FinalizeSale(productId, 1, 10, "Pix", 0);

        PdvService.ChangeSalePayment(
            sale.SaleId,
            [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 10 }],
            cashReceived: 50);

        Assert.Equal(50, GetCashReceived(sale.SaleId));
        Assert.Equal(40, GetChangeAmount(sale.SaleId));
    }

    [Fact]
    public void ChangeSalePayment_SemDinheiro_CashReceivedZero_ZeraTroco()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(20, 10, 4);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 1, 10, cashReceived: 50);

        PdvService.ChangeSalePayment(
            sale.SaleId,
            [new PdvPaymentPart { PaymentType = "Pix", Amount = 10 }],
            cashReceived: 0);

        Assert.Null(GetCashReceived(sale.SaleId));
        Assert.Equal(0, GetChangeAmount(sale.SaleId));
    }

    [Fact]
    public void ChangeSalePayment_PixComCashReceivedPositivo_NaoGeraTroco()
    {
        // Antes (bug): dinheiroAmt=0 → cash_received=recv e change=recv.
        // Depois (correção): sem componente dinheiro → (null, 0).
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(20, 10, 4);
        var sale = FinalizeSale(productId, 1, 10, "Pix", 0);

        PdvService.ChangeSalePayment(
            sale.SaleId,
            [new PdvPaymentPart { PaymentType = "Pix", Amount = 10 }],
            cashReceived: 50);

        Assert.Null(GetCashReceived(sale.SaleId));
        Assert.Equal(0, GetChangeAmount(sale.SaleId));
        var cash = GetCashMovements(sale.SaleId);
        Assert.Single(cash);
        Assert.Equal("Pix", cash[0].PaymentType);
        Assert.Equal(10, cash[0].AmountIn);
    }

    [Fact]
    public void ChangeSalePayment_CartaoComCashReceivedPositivo_NaoGeraTroco()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(20, 10, 4);
        var sale = FinalizeSale(productId, 1, 10, "Pix", 0);

        PdvService.ChangeSalePayment(
            sale.SaleId,
            [new PdvPaymentPart { PaymentType = "Cartão Débito", Amount = 10 }],
            cashReceived: 80);

        Assert.Null(GetCashReceived(sale.SaleId));
        Assert.Equal(0, GetChangeAmount(sale.SaleId));
    }

    [Fact]
    public void ChangeSalePayment_MistoComDinheiro_CalculaTrocoSobreComponenteDinheiro()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(40, 15, 5);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 2, 15, 30);

        PdvService.ChangeSalePayment(
            sale.SaleId,
            [
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 10 },
                new PdvPaymentPart { PaymentType = "Pix", Amount = 20 },
            ],
            cashReceived: 50);

        // Troco = 50 - 10 (só o dinheiro), não sobre o total 30.
        Assert.Equal(50, GetCashReceived(sale.SaleId));
        Assert.Equal(40, GetChangeAmount(sale.SaleId));
        Assert.Equal(30, GetSaleTotal(sale.SaleId));
        Assert.Equal(1, CountSaleItems(sale.SaleId));
        Assert.Equal(38, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void ChangeSalePayment_Client_NaoAlteraVendaLocal()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(20, 10, 4);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 1, 10, 10);
        var payBefore = GetPaymentType(sale.SaleId);
        var cashBefore = CountCashMovements(sale.SaleId);

        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);

        Assert.Throws<StoreNetworkClientBlockedException>(() =>
            PdvService.ChangeSalePayment(
                sale.SaleId,
                [new PdvPaymentPart { PaymentType = "Pix", Amount = 10 }]));

        Assert.Equal(payBefore, GetPaymentType(sale.SaleId));
        Assert.Equal(cashBefore, CountCashMovements(sale.SaleId));
        Assert.Equal(19, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, CountAuditAlterarPagamento(sale.SaleId));

        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
    }

    [Fact]
    public void ChangeSalePayment_Admin_AlteraEGeraAudit()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(100, 10, 4);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 2, 10, 20);

        var detail = PdvService.ChangeSalePayment(
            sale.SaleId,
            [new PdvPaymentPart { PaymentType = "Pix", Amount = 20 }]);

        Assert.Equal("Pix", GetPaymentType(sale.SaleId));
        Assert.Single(GetCashMovements(sale.SaleId));
        Assert.Equal(1, CountAuditAlterarPagamento(sale.SaleId));

        var row = GetLatestAlterarPagamentoAudit(sale.SaleId);
        Assert.Equal("alterar_pagamento", row.Action);
        Assert.Equal("venda", row.Entity);
        Assert.Equal(sale.SaleId.ToString(), row.EntityId);
        Assert.Contains("Dinheiro", row.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pix", row.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("old_payment_type", row.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("new_payment_type", row.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(sale.SaleId, detail.Id);
    }

    [Fact]
    public void ChangeSalePayment_Gestor_PermiteAlterar()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(50, 10, 4);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 1, 10, 10);
        TestDataHelper.SetSessionRole("gestor");

        PdvService.ChangeSalePayment(
            sale.SaleId,
            [new PdvPaymentPart { PaymentType = "Pix", Amount = 10 }]);

        Assert.Equal("Pix", GetPaymentType(sale.SaleId));
        Assert.Equal(1, CountAuditAlterarPagamento(sale.SaleId));
    }

    [Fact]
    public void ChangeSalePayment_VendedorSemPermissao_NaoAlteraVenda()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(50, 10, 4);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 1, 10, 10);
        var payBefore = GetPaymentType(sale.SaleId);
        var cashBefore = CountCashMovements(sale.SaleId);
        var cashSnapshot = GetCashMovements(sale.SaleId);

        TestDataHelper.SetSessionRole("vendedor");

        var ex = Assert.Throws<PdvException>(() =>
            PdvService.ChangeSalePayment(
                sale.SaleId,
                [new PdvPaymentPart { PaymentType = "Pix", Amount = 10 }]));
        Assert.Contains("permissão", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(payBefore, GetPaymentType(sale.SaleId));
        Assert.Equal(cashBefore, CountCashMovements(sale.SaleId));
        Assert.Equal(cashSnapshot, GetCashMovements(sale.SaleId));
        Assert.Equal(0, CountAuditAlterarPagamento(sale.SaleId));
    }

    [Fact]
    public void ChangeSalePayment_VendedorComOverride_PodeAlterar()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(50, 10, 4);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 1, 10, 10);

        TestDataHelper.SetSessionCustomPermissions("vendedor", p => p.PdvAlterarPagamento = true);

        PdvService.ChangeSalePayment(
            sale.SaleId,
            [new PdvPaymentPart { PaymentType = "Pix", Amount = 10 }]);

        Assert.Equal("Pix", GetPaymentType(sale.SaleId));
        Assert.Equal(1, CountAuditAlterarPagamento(sale.SaleId));
    }

    [Fact]
    public void ChangeSalePayment_Falha_NaoGeraAudit()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(20, 10, 4);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 1, 10, 10);

        Assert.Throws<PdvException>(() =>
            PdvService.ChangeSalePayment(
                sale.SaleId,
                [new PdvPaymentPart { PaymentType = "Pix", Amount = 5 }]));

        Assert.Equal(0, CountAuditAlterarPagamento(sale.SaleId));
    }

    [Fact]
    public void ChangeSalePayment_VendaCancelada_Bloqueia()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        TestDataHelper.GrantPdvCancelPermission();
        var productId = TestDataHelper.SeedSimpleProduct(20, 10, 4);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 1, 10, 10);
        PdvService.CancelSale(sale.SaleId);

        var ex = Assert.Throws<PdvException>(() =>
            PdvService.ChangeSalePayment(
                sale.SaleId,
                [new PdvPaymentPart { PaymentType = "Pix", Amount = 10 }]));
        Assert.Contains("cancelada", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, CountAuditAlterarPagamento(sale.SaleId));
    }

    [Fact]
    public void ChangeSalePayment_SaleInexistente_Lanca()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");

        var ex = Assert.Throws<PdvException>(() =>
            PdvService.ChangeSalePayment(
                999999,
                [new PdvPaymentPart { PaymentType = "Pix", Amount = 10 }]));
        Assert.Contains("não encontrada", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, CountAuditAlterarPagamento(999999));
    }

    [Fact]
    public void ChangeSalePayment_SomaPagamentosDiverge_NaoAlteraCaixaNemSales()
    {
        // Falha natural antes de ApplySalePaymentUpdate → estado intacto.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(20, 10, 4);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 1, 10, 10);
        var payBefore = GetPaymentType(sale.SaleId);
        var cashBefore = CountCashMovements(sale.SaleId);

        var ex = Assert.Throws<PdvException>(() =>
            PdvService.ChangeSalePayment(
                sale.SaleId,
                [new PdvPaymentPart { PaymentType = "Pix", Amount = 5 }])); // total é 10

        Assert.Contains("difere", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(payBefore, GetPaymentType(sale.SaleId));
        Assert.Equal(cashBefore, CountCashMovements(sale.SaleId));
        Assert.Equal(0, CountAuditAlterarPagamento(sale.SaleId));
    }

    [Fact]
    public void ChangeSalePayment_NaoRecalculaTaxasNemAlteraTotal()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(20, 10, 4);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 2, 10, 20);

        PdvService.ChangeSalePayment(
            sale.SaleId,
            [new PdvPaymentPart { PaymentType = "Cartão Crédito", Amount = 20 }]);

        Assert.Equal(20, GetSaleTotal(sale.SaleId));
        Assert.Equal("Cartão Crédito", GetPaymentType(sale.SaleId));
        // Sem INSERT em payment_method_fees / receivables — só cash_movements da venda.
        Assert.Equal(1, CountCashMovements(sale.SaleId));
    }

    // ——— helpers ———

    private static PdvFinalizeResult FinalizeSale(
        int productId, double qty, double unitPrice, string paymentType,
        double cashReceived, int? customerId = null)
    {
        return PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = productId,
                    Quantity = qty,
                    UnitPrice = unitPrice,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = paymentType,
            Payments = [new PdvPaymentPart { PaymentType = paymentType, Amount = qty * unitPrice }],
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

    private static string GetPaymentType(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payment_type FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return (string)(cmd.ExecuteScalar() ?? "");
    }

    private static double GetSaleTotal(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT total FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToDouble(cmd.ExecuteScalar());
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
        cmd.CommandText = "SELECT IFNULL(change_amount, 0) FROM sales WHERE id = $id;";
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

    private static int CountSaleItems(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sale_items WHERE sale_id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountStockMovements(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM movements
            WHERE IFNULL(ref_type,'') = 'sale' AND ref_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountCashMovements(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM cash_movements
            WHERE IFNULL(ref_type,'') = 'sale' AND ref_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int Count(string table)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountAuditAlterarPagamento(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM audit_log
            WHERE action = 'alterar_pagamento'
              AND entity = 'venda'
              AND entity_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId.ToString());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static (string Action, string Entity, string EntityId, string Details) GetLatestAlterarPagamentoAudit(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT action, entity, IFNULL(entity_id,''), IFNULL(details,'')
            FROM audit_log
            WHERE action = 'alterar_pagamento'
              AND entity = 'venda'
              AND entity_id = $id
            ORDER BY id DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", saleId.ToString());
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        return (r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3));
    }

    private static List<(string Kind, string PaymentType, double AmountIn, bool AffectsBalance)> GetCashMovements(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT kind, IFNULL(payment_type,''), IFNULL(amount_in,0), IFNULL(affects_balance,1)
            FROM cash_movements
            WHERE IFNULL(ref_type,'') = 'sale' AND ref_id = $id
            ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        var list = new List<(string, string, double, bool)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), r.GetString(1), r.GetDouble(2), r.GetInt32(3) != 0));
        return list;
    }
}
