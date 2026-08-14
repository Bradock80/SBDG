using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 67 — baixa de Contas a Pagar é quitação integral do valor devido.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PayablePayInstallmentTests
{
    private static string TodayBr => DateBrHelper.TodayBr();

    [Fact]
    public void Pay_100_Exact_MarksPaid()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        Pay(id, paid: 100);
        AssertPaid(id, paid: 100);
    }

    [Fact]
    public void Pay_100_With_50_Blocks_AndKeepsPending()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        var ex = Assert.Throws<PayableException>(() => Pay(id, paid: 50));
        Assert.Contains(ProductPriceHelper.MoneyBr(100), ex.Message);
        AssertPending(id);
        Assert.Equal(0, CountCash(id));
    }

    [Fact]
    public void Pay_100_With_0_Blocks()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        Assert.Throws<PayableException>(() => Pay(id, paid: 0));
        AssertPending(id);
    }

    [Fact]
    public void Pay_100_With_99_99_Blocks()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        Assert.Throws<PayableException>(() => Pay(id, paid: 99.99));
        AssertPending(id);
    }

    [Fact]
    public void Pay_100_With_100_01_Blocks()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        Assert.Throws<PayableException>(() => Pay(id, paid: 100.01));
        AssertPending(id);
    }

    [Fact]
    public void Pay_100_With_120_Blocks()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        Assert.Throws<PayableException>(() => Pay(id, paid: 120));
        AssertPending(id);
    }

    [Fact]
    public void Pay_Negative_Blocks()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        var ex = Assert.Throws<PayableException>(() => Pay(id, paid: -1));
        Assert.Contains("valor pago", ex.Message, StringComparison.OrdinalIgnoreCase);
        AssertPending(id);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Pay_NonFinitePaidAmount_Blocks(double paid)
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        var ex = Assert.Throws<PayableException>(() => Pay(id, paid: paid));
        Assert.Contains("inválido", ex.Message, StringComparison.OrdinalIgnoreCase);
        AssertPending(id);
        Assert.Equal(0, CountCash(id));
    }

    [Fact]
    public void Pay_Discount10_Paid90_Allowed()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        Pay(id, paid: 90, discount: 10);
        AssertPaid(id, paid: 90, discount: 10);
    }

    [Fact]
    public void Pay_Interest10_Paid110_Allowed()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        Pay(id, paid: 110, interest: 10);
        AssertPaid(id, paid: 110, interest: 10);
    }

    [Fact]
    public void Pay_Multa5_Paid105_Allowed()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        Pay(id, paid: 105, multa: 5);
        AssertPaid(id, paid: 105, multa: 5);
    }

    [Fact]
    public void Pay_Combo_100_Minus10_Plus5_Plus2_Paid97_Allowed()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        Pay(id, paid: 97, discount: 10, interest: 5, multa: 2);
        AssertPaid(id, paid: 97, discount: 10, interest: 5, multa: 2);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(98)]
    public void Pay_Combo97_WrongPaid_Blocks(double paid)
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        var ex = Assert.Throws<PayableException>(() =>
            Pay(id, paid: paid, discount: 10, interest: 5, multa: 2));
        Assert.Contains(ProductPriceHelper.MoneyBr(97), ex.Message);
        AssertPending(id);
    }

    [Fact]
    public void SecondPay_OnPaidInstallment_Blocks()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        Pay(id, paid: 100);
        var ex = Assert.Throws<PayableException>(() => Pay(id, paid: 100));
        Assert.Contains("já está paga", ex.Message, StringComparison.OrdinalIgnoreCase);
        AssertPaid(id, paid: 100);
    }

    [Fact]
    public void Reverse_ThenPayAgain_Allowed()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        Pay(id, paid: 100);
        PayableService.ReversePayment(id);
        AssertPending(id);
        Pay(id, paid: 100);
        AssertPaid(id, paid: 100);
    }

    [Fact]
    public void InvalidPay_Dinheiro_DoesNotCreateCashMovement()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        Assert.Throws<PayableException>(() => Pay(id, paid: 50, type: "Dinheiro"));
        AssertPending(id);
        Assert.Equal(0, CountCash(id));
    }

    [Fact]
    public void ValidPay_Dinheiro_CreatesCashMovement()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        Pay(id, paid: 100, type: "Dinheiro");
        AssertPaid(id, paid: 100);
        Assert.Equal(1, CountCash(id));
    }

    [Fact]
    public void SecondPay_DoesNotDuplicateCashMovement()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        Pay(id, paid: 100, type: "Dinheiro");
        Assert.Throws<PayableException>(() => Pay(id, paid: 100, type: "Dinheiro"));
        Assert.Equal(1, CountCash(id));
    }

    [Fact]
    public void CashClosed_ValidDinheiroPay_RollsBackInstallment()
    {
        using var db = TempDatabase.Create();
        CashService.OpenSession(50, "67-caixa");
        CashService.CloseSession(50, "fechar");
        var id = NewInstallment(100);
        Assert.Throws<CashOperationException>(() => Pay(id, paid: 100, type: "Dinheiro"));
        AssertPending(id);
        Assert.Equal(0, CountCash(id));
    }

    [Fact]
    public void LinkedPurchase_FullPay_StillBlocksCancel()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var purchaseId = CreateClosedPurchase(totalQty: 10, unitPrice: 10);
        var inst = Assert.Single(PayableService.ListInstallmentsLocal(purchaseId: purchaseId));
        Pay(inst.Id, paid: inst.Amount);

        var ex = Assert.Throws<PayableException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Contains("parcela paga", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("fechada", GetPurchaseStatus(purchaseId));
    }

    [Fact]
    public void LinkedPurchase_BlockedPartial_DoesNotTrapCancel()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var purchaseId = CreateClosedPurchase(totalQty: 10, unitPrice: 10);
        var inst = Assert.Single(PayableService.ListInstallmentsLocal(purchaseId: purchaseId));
        Assert.Equal(100, inst.Amount);
        Assert.Throws<PayableException>(() => Pay(inst.Id, paid: 50));
        AssertPending(inst.Id);

        PurchaseService.Cancel(purchaseId);
        Assert.Equal("cancelada", GetPurchaseStatus(purchaseId));
        Assert.Empty(PayableService.ListInstallmentsLocal(purchaseId: purchaseId));
    }

    [Fact]
    public void HostPath_PayInstallmentLocal_SameRule_Blocks50()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        Assert.Throws<PayableException>(() =>
            PayableService.PayInstallmentLocal(id, Input(50)));
        AssertPending(id);
    }

    [Fact]
    public void Client_PayInstallment_DoesNotWriteLocalSqlite()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        try
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
            Assert.ThrowsAny<Exception>(() => Pay(id, paid: 50));
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
        AssertPending(id);
    }

    [Fact]
    public void Rounding_99999999_MatchesDue100()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        Pay(id, paid: 99.999999);
        AssertPaid(id, paid: 100);
    }

    [Fact]
    public void FullDiscount_DueZero_AllowsQuitWithoutCash()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        Pay(id, paid: 0, discount: 100, type: "Dinheiro");
        AssertPaid(id, paid: 0, discount: 100);
        Assert.Equal(0, CountCash(id));
    }

    [Fact]
    public void FullDiscount_Paid50_Blocks()
    {
        using var db = TempDatabase.Create();
        var id = NewInstallment(100);
        var ex = Assert.Throws<PayableException>(() => Pay(id, paid: 50, discount: 100));
        Assert.Contains(ProductPriceHelper.MoneyBr(0), ex.Message);
        AssertPending(id);
    }

    private static int NewInstallment(double amount)
    {
        var supplierId = SeedSupplier();
        var titleId = PayableService.CreateTitle(new PayableTitleCreateInput
        {
            SupplierId = supplierId,
            Number = "NF-" + Guid.NewGuid().ToString("N")[..8],
            EmissionDate = TodayBr,
            DueDate = TodayBr,
            TotalAmount = amount,
            PaymentType = "Boleto",
        });
        return PayableService.ListInstallmentsOfTitle(titleId).Single().Id;
    }

    private static void Pay(
        int id, double paid, double discount = 0, double interest = 0, double multa = 0,
        string type = "Boleto") =>
        PayableService.PayInstallment(id, Input(paid, discount, interest, multa, type));

    private static PayablePayInput Input(
        double paid, double discount = 0, double interest = 0, double multa = 0,
        string type = "Boleto") =>
        new()
        {
            PaidAmount = paid,
            PaidDate = TodayBr,
            Discount = discount,
            Interest = interest,
            Multa = multa,
            PaymentType = type,
        };

    private static void AssertPaid(
        int id, double paid, double discount = 0, double interest = 0, double multa = 0)
    {
        var d = PayableService.GetInstallment(id)!;
        Assert.Equal("pago", d.Status);
        Assert.Equal(paid, d.PaidAmount);
        Assert.Equal(discount, d.Discount);
        Assert.Equal(interest, d.Interest);
        Assert.Equal(multa, d.Multa);
        Assert.False(string.IsNullOrEmpty(d.PaidDate));
    }

    private static void AssertPending(int id)
    {
        var d = PayableService.GetInstallment(id)!;
        Assert.Equal("pendente", d.Status);
        Assert.Equal(0, d.PaidAmount);
        Assert.Null(d.PaidDate);
    }

    private static int CountCash(int installmentId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM cash_movements
            WHERE ref_type = 'payable_installment' AND ref_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", installmentId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORN 67', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CreateClosedPurchase(double totalQty, double unitPrice)
    {
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "P67", "PROD 67");
        return PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-67",
            GerarEstoque = true,
            Items =
            [
                new PurchaseItemInput
                {
                    ProductId = productId,
                    ProductName = "PROD 67",
                    Quantity = totalQty,
                    UnitPrice = unitPrice,
                    LotNumber = "L67",
                    ExpiryDate = DateTime.Today.AddYears(1),
                },
            ],
        }, closeOnSave: true);
    }

    private static string GetPurchaseStatus(int purchaseId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM purchases WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", purchaseId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }
}
