using SGDB;
using SGDB.Application.Sales;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// Cancelamento de venda PIX já gravada: refund/cancel Mercado Pago + persistência.
/// Cupom e consulta passam pelo mesmo <see cref="PdvService.CancelSale"/>.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PixSaleReverseTests
{
    [Fact]
    public void ApprovedSale_Cancel_CallsRefund()
    {
        using var db = TempDatabase.Create();
        var (sale, _, gw) = SeedPixSale();
        using (UseGateway(gw))
        {
            PdvService.CancelSale(sale.SaleId);
        }

        Assert.Equal(1, gw.RefundCount);
        Assert.Equal(0, gw.CancelCount);
        Assert.Contains("refund", gw.CallLog);
    }

    [Fact]
    public void ApprovedSale_Cancel_MarksRefunded()
    {
        using var db = TempDatabase.Create();
        var (sale, _, gw) = SeedPixSale();
        using (UseGateway(gw))
        {
            PdvService.CancelSale(sale.SaleId);
        }

        var intent = PixIntentService.GetBySaleId(sale.SaleId)!;
        Assert.Equal("refunded", intent.Status);
        Assert.False(string.IsNullOrWhiteSpace(intent.RefundedAt));
        Assert.Equal(gw.PaymentId, intent.MpPaymentId);
    }

    [Fact]
    public void ConsultaPath_Cancel_CallsRefund()
    {
        using var db = TempDatabase.Create();
        var (sale, _, gw) = SeedPixSale();
        using (UseGateway(gw))
        {
            ApplicationServices.CancelSale.Execute(new CancelSaleCommand { SaleId = sale.SaleId });
        }

        Assert.Equal(1, gw.RefundCount);
        Assert.Equal("refunded", PixIntentService.GetBySaleId(sale.SaleId)!.Status);
        Assert.True(IsSaleCancelled(sale.SaleId));
    }

    [Fact]
    public void RefundFails_MarksRefundPending()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway
        {
            PaymentId = 99001,
            DefaultGetStatus = "approved",
            RefundError = new InvalidOperationException("Mercado Pago (500): timeout"),
        };
        var (sale, _, _) = SeedPixSale(gw);
        using (UseGateway(gw))
        {
            PdvService.CancelSale(sale.SaleId);
        }

        var intent = PixIntentService.GetBySaleId(sale.SaleId)!;
        Assert.Equal("refund_pending", intent.Status);
        Assert.True(string.IsNullOrWhiteSpace(intent.RefundedAt));
        Assert.False(string.IsNullOrWhiteSpace(intent.LastError));
        Assert.Equal("refund_pending", PixSaleReverseService.LastResult.Outcome);
        Assert.Contains("pendente", PixSaleReverseService.LastResult.OperatorMessage, StringComparison.OrdinalIgnoreCase);

        var row = PdvQueryService.ListSales(includeCancelled: true).Single(r => r.Id == sale.SaleId);
        Assert.Equal("refund_pending", row.PixIntentStatus);
        Assert.Equal("PIX — estorno pendente", row.FormaDisplay);
        Assert.Equal("PIX pend.", row.StatusDisplay);
    }

    [Fact]
    public void RefundFails_StillCancelsLocally()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway
        {
            PaymentId = 99001,
            DefaultGetStatus = "approved",
            RefundError = new InvalidOperationException("sem internet"),
        };
        var (sale, productId, _) = SeedPixSale(gw);
        using (UseGateway(gw))
        {
            PdvService.CancelSale(sale.SaleId);
        }

        Assert.True(IsSaleCancelled(sale.SaleId));
        Assert.Equal(100, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, CountCashMovementsForSale(sale.SaleId));
        Assert.Equal("refund_pending", PixIntentService.GetBySaleId(sale.SaleId)!.Status);
    }

    [Fact]
    public void AlreadyRefunded_DoesNotCallRefundAgain()
    {
        using var db = TempDatabase.Create();
        var (sale, _, gw) = SeedPixSale();
        PixIntentService.MarkRefunded(gw.PaymentId);
        using (UseGateway(gw))
        {
            PdvService.CancelSale(sale.SaleId);
        }

        Assert.Equal(0, gw.RefundCount);
        Assert.Equal(0, gw.GetCount);
        Assert.Equal("refunded", PixIntentService.GetBySaleId(sale.SaleId)!.Status);
        Assert.True(IsSaleCancelled(sale.SaleId));
    }

    [Fact]
    public void NonPixSale_DoesNotCallMp()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.GrantPdvCancelPermission();
        CashService.OpenSession(openingAmount: 50, notes: "teste");
        var productId = TestDataHelper.SeedSimpleProduct(100, 10, 4);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 4, 10, 40);
        var gw = new FakeMercadoPagoPixGateway();
        using (UseGateway(gw))
        {
            PdvService.CancelSale(sale.SaleId);
        }

        Assert.Equal(0, gw.GetCount);
        Assert.Equal(0, gw.RefundCount);
        Assert.Equal(0, gw.CancelCount);
        Assert.Equal("none", PixSaleReverseService.LastResult.Outcome);
        Assert.True(IsSaleCancelled(sale.SaleId));
    }

    [Fact]
    public void PixChave_DoesNotCallMp()
    {
        using var db = TempDatabase.Create();
        var (sale, _, gw) = SeedPixSale(attachIntent: false, paymentType: "Pix Chave");
        using (UseGateway(gw))
        {
            PdvService.CancelSale(sale.SaleId);
        }

        Assert.Equal(0, gw.GetCount);
        Assert.Equal(0, gw.RefundCount);
        Assert.Equal(0, gw.CancelCount);
        Assert.Null(PixIntentService.GetBySaleId(sale.SaleId));
        Assert.True(IsSaleCancelled(sale.SaleId));
    }

    [Fact]
    public void LegacyPixWithoutPaymentId_WarnsManual()
    {
        using var db = TempDatabase.Create();
        var (sale, productId, gw) = SeedPixSale(attachIntent: false);
        using (UseGateway(gw))
        {
            PdvService.CancelSale(sale.SaleId);
        }

        Assert.Equal(0, gw.RefundCount);
        Assert.Equal(0, gw.GetCount);
        Assert.Equal("legacy_manual", PixSaleReverseService.LastResult.Outcome);
        Assert.True(PixSaleReverseService.LastResult.IsCriticalAlert);
        Assert.Contains("manual", PixSaleReverseService.LastResult.OperatorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(IsSaleCancelled(sale.SaleId));
        Assert.Equal(100, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void ApiPending_CallsCancelNotRefund()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway
        {
            PaymentId = 99001,
            DefaultGetStatus = "pending",
        };
        var (sale, _, _) = SeedPixSale(gw);
        using (UseGateway(gw))
        {
            PdvService.CancelSale(sale.SaleId);
        }

        Assert.Equal(1, gw.CancelCount);
        Assert.Equal(0, gw.RefundCount);
        Assert.Equal("cancelled", PixIntentService.GetBySaleId(sale.SaleId)!.Status);
        Assert.True(IsSaleCancelled(sale.SaleId));
    }

    [Fact]
    public void ApiRefunded_DoesNotDuplicateRefund()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway
        {
            PaymentId = 99001,
            DefaultGetStatus = "refunded",
        };
        var (sale, _, _) = SeedPixSale(gw);
        using (UseGateway(gw))
        {
            PdvService.CancelSale(sale.SaleId);
        }

        Assert.True(gw.GetCount >= 1);
        Assert.Equal(0, gw.RefundCount);
        Assert.Equal("refunded", PixIntentService.GetBySaleId(sale.SaleId)!.Status);
        Assert.True(IsSaleCancelled(sale.SaleId));
    }

    [Fact]
    public void StockRestoredOnce()
    {
        using var db = TempDatabase.Create();
        var (sale, productId, gw) = SeedPixSale();
        using (UseGateway(gw))
        {
            PdvService.CancelSale(sale.SaleId);
        }

        Assert.Equal(100, TestDataHelper.GetProductStock(productId));
        Assert.Equal(4, SumSaleCancelQty(productId, sale.SaleId));

        Assert.Throws<PdvException>(() => PdvService.CancelSale(sale.SaleId));
        Assert.Equal(100, TestDataHelper.GetProductStock(productId));
        Assert.Equal(4, SumSaleCancelQty(productId, sale.SaleId));
        Assert.Equal(1, gw.RefundCount);
    }

    [Fact]
    public void CashRevertedOnce()
    {
        using var db = TempDatabase.Create();
        var (sale, _, gw) = SeedPixSale();
        Assert.True(CountCashMovementsForSale(sale.SaleId) >= 1);
        using (UseGateway(gw))
        {
            PdvService.CancelSale(sale.SaleId);
        }

        Assert.Equal(0, CountCashMovementsForSale(sale.SaleId));
        Assert.Throws<PdvException>(() => PdvService.CancelSale(sale.SaleId));
        Assert.Equal(0, CountCashMovementsForSale(sale.SaleId));
        Assert.Equal(1, gw.RefundCount);
    }

    [Fact]
    public void MpError_PreservesPaymentId()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway
        {
            PaymentId = 99001,
            DefaultGetStatus = "approved",
            RefundError = new InvalidOperationException("falha mp"),
        };
        var (sale, _, _) = SeedPixSale(gw);
        using (UseGateway(gw))
        {
            PdvService.CancelSale(sale.SaleId);
        }

        var intent = PixIntentService.GetBySaleId(sale.SaleId)!;
        Assert.Equal(99001, intent.MpPaymentId);
        Assert.Equal("refund_pending", intent.Status);
        Assert.NotEqual("refunded", intent.Status);
    }

    [Fact]
    public void Token_NotInErrorOrAudit()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway
        {
            PaymentId = 99001,
            DefaultGetStatus = "approved",
            RefundError = new InvalidOperationException("Bearer APP_USR-SECRET-TOKEN denied"),
        };
        var (sale, _, _) = SeedPixSale(gw);
        using (UseGateway(gw))
        {
            PdvService.CancelSale(sale.SaleId);
        }

        var intent = PixIntentService.GetBySaleId(sale.SaleId)!;
        Assert.Equal("Falha no Mercado Pago.", intent.LastError);
        Assert.DoesNotContain("APP_USR", intent.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", intent.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET", intent.LastError, StringComparison.OrdinalIgnoreCase);

        var logs = AuditService.List(search: sale.SaleId.ToString());
        foreach (var log in logs)
        {
            var details = log.Details ?? "";
            Assert.DoesNotContain("APP_USR", details, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SECRET-TOKEN", details, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Bearer", details, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static (PdvFinalizeResult Sale, int ProductId, FakeMercadoPagoPixGateway Gw) SeedPixSale(
        FakeMercadoPagoPixGateway? gw = null,
        string paymentType = "Pix",
        bool attachIntent = true)
    {
        TestDataHelper.GrantPdvCancelPermission();
        CashService.OpenSession(openingAmount: 50, notes: "teste");
        var productId = TestDataHelper.SeedSimpleProduct(100, 10, 4);
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = productId,
                    Code = "T001",
                    Name = "Produto Teste",
                    Unit = "UN",
                    Quantity = 4,
                    UnitPrice = 10,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = paymentType,
        });
        var fake = gw ?? new FakeMercadoPagoPixGateway
        {
            PaymentId = 99001,
            DefaultGetStatus = "approved",
        };
        if (attachIntent)
        {
            PixIntentService.Create(fake.PaymentId, sale.Total, "idem-test", "approved");
            PixIntentService.MarkApproved(fake.PaymentId);
            PixIntentService.AttachSale(fake.PaymentId, sale.SaleId);
        }

        return (sale, productId, fake);
    }

    private static IDisposable UseGateway(IMercadoPagoPixGateway gw) => new GatewayScope(gw);

    private sealed class GatewayScope : IDisposable
    {
        private readonly IMercadoPagoPixGateway _prev;

        public GatewayScope(IMercadoPagoPixGateway gw)
        {
            _prev = MercadoPagoPixService.Gateway;
            MercadoPagoPixService.Gateway = gw;
        }

        public void Dispose() => MercadoPagoPixService.Gateway = _prev;
    }

    private static bool IsSaleCancelled(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT cancelled FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar()) != 0;
    }

    private static int CountCashMovementsForSale(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM cash_movements
            WHERE IFNULL(ref_type, '') = 'sale' AND ref_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static double SumSaleCancelQty(int productId, int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(SUM(quantity), 0) FROM movements
            WHERE product_id = $pid
              AND IFNULL(ref_type, '') = 'sale_cancel'
              AND ref_id = $sale;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$sale", saleId);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }
}
