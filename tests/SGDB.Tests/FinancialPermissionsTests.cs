using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 68B — permissões mínimas de Contas a Pagar e Fiado, sem quebrar o balcão.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class FinancialPermissionsTests
{
    private static string TodayBr => DateBrHelper.TodayBr();

    [Theory]
    [InlineData("admin")]
    [InlineData("gestor")]
    public void AdminGestor_CanAccessPagarAndSensitiveFlags(string role)
    {
        TestDataHelper.SetSessionRole(role);
        Assert.True(AccessControl.CanAccessModule("pagar"));
        Assert.True(AccessControl.Can("ContasPagarAcesso"));
        Assert.True(AccessControl.Can("ContasPagarEstornar"));
        Assert.True(AccessControl.Can("FiadoReceber"));
        Assert.True(AccessControl.Can("FiadoEstornar"));
        Assert.True(AccessControl.Can("FiadoExcluir"));
        Assert.True(AccessControl.Can("FinanceiroAcesso"));
    }

    [Fact]
    public void Vendedor_CannotAccessPagar_KeepsBalcao()
    {
        TestDataHelper.SetSessionRole("vendedor");
        Assert.False(AccessControl.CanAccessModule("pagar"));
        Assert.Equal("Sem permissão", AccessControl.DenyReason("pagar"));
        Assert.False(AccessControl.Can("ContasPagarAcesso"));
        Assert.False(AccessControl.Can("ContasPagarEstornar"));
        Assert.False(AccessControl.Can("FiadoEstornar"));
        Assert.False(AccessControl.Can("FiadoExcluir"));

        Assert.True(AccessControl.Can("PdvVenda"));
        Assert.True(AccessControl.Can("ClientesConsultar"));
        Assert.True(AccessControl.Can("ProdutosConsultar"));
        Assert.True(AccessControl.Can("FinanceiroAcesso"));
        Assert.True(AccessControl.Can("FiadoReceber"));
        Assert.True(AccessControl.CanAccessModule("caixa"));
        Assert.True(AccessControl.CanAccessModule("fiado"));
        Assert.True(AccessControl.CanAccessModule("pdv"));
        Assert.True(AccessControl.CanAccessModule("clientes"));
        Assert.True(AccessControl.CanAccessModule("produtos"));
    }

    [Fact]
    public void Catalog_ContainsNewFinancialFlags()
    {
        var keys = UserPermissions.Catalog.Select(c => c.Key).ToList();
        Assert.Contains("ContasPagarAcesso", keys);
        Assert.Contains("ContasPagarEstornar", keys);
        Assert.Contains("FiadoReceber", keys);
        Assert.Contains("FiadoEstornar", keys);
        Assert.Contains("FiadoExcluir", keys);
        Assert.Contains("FinanceiroAcesso", keys);
    }

    [Fact]
    public void LegacyJson_AdminCustomized_InheritsSensitiveTrue()
    {
        var json = """{"Customized":true,"PdvVenda":true,"SistemaUsuarios":true,"FinanceiroAcesso":true}""";
        var p = UserPermissions.Parse(json, "admin");
        Assert.True(p.ContasPagarAcesso);
        Assert.True(p.ContasPagarEstornar);
        Assert.True(p.FiadoReceber);
        Assert.True(p.FiadoEstornar);
        Assert.True(p.FiadoExcluir);
    }

    [Fact]
    public void LegacyJson_GestorCustomized_InheritsSensitiveTrue()
    {
        var json = """{"Customized":true,"PdvVenda":true,"FinanceiroAcesso":true,"RelatoriosAcesso":true}""";
        var p = UserPermissions.Parse(json, "gestor");
        Assert.True(p.ContasPagarAcesso);
        Assert.True(p.ContasPagarEstornar);
        Assert.True(p.FiadoReceber);
        Assert.True(p.FiadoEstornar);
        Assert.True(p.FiadoExcluir);
    }

    [Fact]
    public void LegacyJson_VendedorCustomized_SensitiveFlagsStayFalse()
    {
        var json = """{"Customized":true,"PdvVenda":true,"ClientesConsultar":true,"ProdutosConsultar":true,"FinanceiroAcesso":true}""";
        var p = UserPermissions.Parse(json, "vendedor");
        Assert.True(p.FinanceiroAcesso);
        Assert.True(p.FiadoReceber);
        Assert.False(p.ContasPagarAcesso);
        Assert.False(p.ContasPagarEstornar);
        Assert.False(p.FiadoEstornar);
        Assert.False(p.FiadoExcluir);
    }

    [Fact]
    public void NotCustomized_IgnoresJsonAndUsesRole()
    {
        var json = """{"Customized":false,"ContasPagarAcesso":true,"FiadoExcluir":true}""";
        var p = UserPermissions.Parse(json, "vendedor");
        Assert.False(p.ContasPagarAcesso);
        Assert.False(p.FiadoExcluir);
        Assert.True(p.FiadoReceber);
    }

    [Fact]
    public void EmptyJson_UsesRoleDefaults()
    {
        var v = UserPermissions.Parse(null, "vendedor");
        Assert.False(v.ContasPagarAcesso);
        Assert.True(v.FiadoReceber);
        var a = UserPermissions.Parse("  ", "admin");
        Assert.True(a.ContasPagarAcesso);
        Assert.True(a.FiadoExcluir);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("gestor")]
    public void AdminGestor_CanPayAndReversePayable(string role)
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole(role);
        var id = NewInstallment(100);
        PayableService.PayInstallment(id, PayInput(100));
        Assert.Equal("pago", PayableService.GetInstallment(id)!.Status);
        PayableService.ReversePayment(id);
        Assert.Equal("pendente", PayableService.GetInstallment(id)!.Status);
    }

    [Fact]
    public void Vendedor_PayInstallment_Blocks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = NewInstallment(100);
        TestDataHelper.SetSessionRole("vendedor");
        var ex = Assert.Throws<PayableException>(() =>
            PayableService.PayInstallment(id, PayInput(100)));
        Assert.Contains("permissão", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("pendente", PayableService.GetInstallment(id)!.Status);
        Assert.Equal(0, PayableService.GetInstallment(id)!.PaidAmount);
    }

    [Fact]
    public void Vendedor_PayInstallmentLocal_Blocks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = NewInstallment(100);
        TestDataHelper.SetSessionRole("vendedor");
        Assert.Throws<PayableException>(() =>
            PayableService.PayInstallmentLocal(id, PayInput(100)));
        Assert.Equal("pendente", PayableService.GetInstallment(id)!.Status);
    }

    [Fact]
    public void Vendedor_ReversePaymentLocal_Blocks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = NewInstallment(100);
        PayableService.PayInstallment(id, PayInput(100));
        TestDataHelper.SetSessionRole("vendedor");
        var ex = Assert.Throws<PayableException>(() =>
            PayableService.ReversePaymentLocal(id));
        Assert.Contains("permissão", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("pago", PayableService.GetInstallment(id)!.Status);
    }

    [Fact]
    public void Vendedor_CreateTitle_Blocks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("vendedor");
        Assert.Throws<PayableException>(() => NewInstallment(50));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("gestor")]
    public void AdminGestor_CanReceiveReverseAndClearFiado(string role)
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole(role);
        CashService.OpenSession(80, "68b");
        var (customerId, _) = SeedFiadoSale(40);
        var payId = ReceiveFiado(customerId, 20);
        Assert.True(payId > 0);

        FiadoService.ReversePayment(payId);
        var (sales, payments, _) = FiadoService.ClearCustomerFiado(customerId);
        Assert.True(sales >= 1);
        Assert.True(payments >= 0);
    }

    [Fact]
    public void Vendedor_CanReceiveFiado_CannotReverseOrClear()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(80, "68b");
        var (customerId, _) = SeedFiadoSale(40);

        TestDataHelper.SetSessionRole("vendedor");
        var payId = ReceiveFiado(customerId, 15);
        Assert.True(payId > 0);

        var reverseEx = Assert.Throws<FiadoException>(() => FiadoService.ReversePayment(payId));
        Assert.Contains("permissão", reverseEx.Message, StringComparison.OrdinalIgnoreCase);

        var clearEx = Assert.Throws<FiadoException>(() => FiadoService.ClearCustomerFiado(customerId));
        Assert.Contains("permissão", clearEx.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Vendedor_DiscardOrphanSales_Blocks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("vendedor");
        var ex = Assert.Throws<FiadoException>(() => FiadoService.DiscardOrphanSales());
        Assert.Contains("permissão", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Vendedor_CanSellFiadoAndCashAndConsult()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("vendedor");
        CashService.OpenSession(50, "balcao");
        var productId = TestDataHelper.SeedSimpleProduct(20, 10, 4);
        var customer = PersonService.Create(new PersonInput { Name = "Cli Balcao", PersonKind = "fisica" });

        var cash = TestDataHelper.FinalizeSimpleCashSale(productId, 1, 10, 10);
        Assert.True(cash.SaleId > 0);

        var fiado = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = productId,
                    Code = "T001",
                    Name = "Produto Teste",
                    Unit = "UN",
                    Quantity = 1,
                    UnitPrice = 10,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Fiado",
            CustomerPersonId = customer.Id,
        });
        Assert.True(fiado.SaleId > 0);
        Assert.Equal(18, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void Client_PayInstallment_DoesNotWriteLocal_WhenVendedor()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = NewInstallment(100);
        try
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
            TestDataHelper.SetSessionRole("vendedor");
            Assert.Throws<PayableException>(() =>
                PayableService.PayInstallment(id, PayInput(100)));
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }

        Assert.Equal("pendente", PayableService.GetInstallment(id)!.Status);
        Assert.Equal(0, PayableService.GetInstallment(id)!.PaidAmount);
    }

    [Fact]
    public void Residual68C_HostRpc_DoesNotTreatServerSessionAsNotebookUser()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = NewInstallment(100);
        TestDataHelper.SetSessionRole("vendedor");
        using (AccessControl.EnterRemoteStoreRequest())
        {
            // PIN-only até a 68C: host não deve recusar com o usuário do PC servidor.
            PayableService.PayInstallmentLocal(id, PayInput(100));
        }

        Assert.Equal("pago", PayableService.GetInstallment(id)!.Status);
    }

    private static int NewInstallment(double amount)
    {
        var supplier = PersonService.Create(new PersonInput
        {
            Name = "Forn 68B",
            PersonKind = "juridica",
            Roles = new PersonRoles { Ativo = true, Fornecedores = true },
        }, requireClienteRole: false);
        var titleId = PayableService.CreateTitle(new PayableTitleCreateInput
        {
            SupplierId = supplier.Id,
            Number = "NF-" + Guid.NewGuid().ToString("N")[..8],
            EmissionDate = TodayBr,
            DueDate = TodayBr,
            TotalAmount = amount,
            PaymentType = "Boleto",
        });
        return PayableService.ListInstallmentsOfTitle(titleId).Single().Id;
    }

    private static PayablePayInput PayInput(double paid) =>
        new()
        {
            PaidAmount = paid,
            PaidDate = TodayBr,
            PaymentType = "Boleto",
        };

    private static (int CustomerId, int SaleId) SeedFiadoSale(double amount)
    {
        var customer = PersonService.Create(new PersonInput { Name = "Cli Fiado 68B", PersonKind = "fisica" });
        var productId = TestDataHelper.SeedSimpleProduct(50, amount, 2, "F68", "Fiado 68B");
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = productId,
                    Code = "F68",
                    Name = "Fiado 68B",
                    Unit = "UN",
                    Quantity = 1,
                    UnitPrice = amount,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Fiado",
            CustomerPersonId = customer.Id,
        });
        return (customer.Id, sale.SaleId);
    }

    private static int ReceiveFiado(int customerId, double amount) =>
        FiadoService.RegisterPayment(customerId, new FiadoReceberInput
        {
            Amount = amount,
            PrincipalAmount = amount,
            InterestAmount = 0,
            PaymentDate = TodayBr,
            Payments = [new FiadoReceberPart { PaymentType = "Dinheiro", Amount = amount }],
        });
}
