using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

[Collection(TempDatabaseCollection.Name)]
public class ValidityControlTests
{
    static readonly DateTime Today = new(2026, 8, 24);

    [Theory]
    [InlineData(-2, ProductExpiryStatusKind.Expired)]
    [InlineData(0, ProductExpiryStatusKind.Today)]
    [InlineData(1, ProductExpiryStatusKind.Within7)]
    [InlineData(7, ProductExpiryStatusKind.Within7)]
    [InlineData(8, ProductExpiryStatusKind.Within15)]
    [InlineData(15, ProductExpiryStatusKind.Within15)]
    [InlineData(16, ProductExpiryStatusKind.Within30)]
    [InlineData(30, ProductExpiryStatusKind.Within30)]
    [InlineData(31, ProductExpiryStatusKind.Within60)]
    [InlineData(60, ProductExpiryStatusKind.Within60)]
    [InlineData(61, ProductExpiryStatusKind.Within90)]
    [InlineData(90, ProductExpiryStatusKind.Within90)]
    [InlineData(91, ProductExpiryStatusKind.Ok)]
    public void FaixasExclusivas(int offset, ProductExpiryStatusKind kind)
    {
        var status = ProductExpiryService.Classify(Today.AddDays(offset), Today);
        Assert.Equal(kind, status.Kind);
    }

    [Fact]
    public void CardsSemDuplaContagem()
    {
        var product = Product("P", lots:
        [
            Lot("A", -1),
            Lot("B", 0),
            Lot("C", 3),
            Lot("D", 10),
            Lot("E", 20),
            Lot("F", 40),
            Lot("G", 70),
            Lot("H", 120),
            Lot("I", null),
        ]);
        var snap = ValidityControlEngine.Snapshot([product], Today);
        Assert.Equal(1, snap.Cards.Expired);
        Assert.Equal(1, snap.Cards.Today);
        Assert.Equal(1, snap.Cards.Days7);
        Assert.Equal(1, snap.Cards.Days15);
        Assert.Equal(1, snap.Cards.Days30);
        Assert.Equal(1, snap.Cards.Days60);
        Assert.Equal(1, snap.Cards.Days90);
        Assert.Equal(1, snap.Cards.Ok);
        Assert.Equal(1, snap.Cards.Uninformed);
        Assert.Equal(9, snap.Cards.Total);
        Assert.Equal(9, snap.Rows.Count);
    }

    [Fact]
    public void FiltroPorFaixa()
    {
        var snap = ValidityControlEngine.Snapshot([Product("P", lots: [Lot("A", -1), Lot("B", 3), Lot("C", 40)])], Today);
        var seven = ValidityControlEngine.Apply(snap.Rows, ValidityControlFilterKind.Days7);
        Assert.Equal("B", Assert.Single(seven).LotDisplay);
        var expired = ValidityControlEngine.Apply(snap.Rows, ValidityControlFilterKind.Expired);
        Assert.Equal("A", Assert.Single(expired).LotDisplay);
    }

    [Fact]
    public void Ordenacao_VencidoHojeMenorValidadeSemValidadeProduto()
    {
        var rows = ValidityControlEngine.BuildRows(
        [
            Product("Zebra", lots: [Lot("Z", 40)]),
            Product("Antarctica", lots: [Lot("S", null), Lot("V", -2), Lot("H", 0), Lot("N", 5)]),
        ], Today);
        Assert.Equal(["V", "H", "N", "Z", "S"], rows.Select(r => r.LotDisplay).ToArray());
    }

    [Fact]
    public void QuantityZeroFora()
    {
        using var db = TempDatabase.Create();
        var pid = TestDataHelper.SeedSimpleProduct(0, 5, 2, "Q0", "ZERO");
        Receive(pid, 4, "ATIVO", Today.AddDays(3));
        InsertLot(pid, "MORTO", Today.AddDays(3), 0);
        var snap = ValidityControlService.GetSnapshotLocal(Today);
        Assert.Equal("ATIVO", Assert.Single(snap.Rows).LotDisplay);
    }

    [Fact]
    public void LegacyExtraJsonIgnorado()
    {
        using var db = TempDatabase.Create();
        var pid = TestDataHelper.SeedSimpleProduct(8, 5, 2, "LEG", "LEGADO");
        SetExtra(pid, """{"data_validade":"2026-08-25","controle_validade":false}""");
        var snap = ValidityControlService.GetSnapshotLocal(Today);
        Assert.DoesNotContain(snap.Rows, r => r.ProductId == pid);
        Assert.Null(ProductExpiryService.GetNextExpiry(pid));
    }

    [Fact]
    public void SemValidade_LoteAtivoSemExpiry()
    {
        using var db = TempDatabase.Create();
        var pid = TestDataHelper.SeedSimpleProduct(0, 5, 2, "NV", "SEM DATA");
        Receive(pid, 6, "L1", expiry: null);
        var row = Assert.Single(ValidityControlService.GetSnapshotLocal(Today).Rows);
        Assert.Equal(ProductExpiryStatusKind.Uninformed, row.Status.Kind);
        Assert.Equal("SEM VALIDADE", row.StatusDisplay);
        Assert.Equal(ValidityControlRowKind.UninformedLot, row.RowKind);
    }

    [Fact]
    public void ProdutoComControleSemLote_AlertaSemInventarLote()
    {
        using var db = TempDatabase.Create();
        var pid = TestDataHelper.SeedSimpleProduct(12, 5, 2, "NL", "SEM LOTE");
        SetExtra(pid, """{"controle_validade":true}""");
        var row = Assert.Single(ValidityControlService.GetSnapshotLocal(Today).Rows);
        Assert.Equal(pid, row.ProductId);
        Assert.Null(row.LotId);
        Assert.Equal(ValidityControlEngine.MissingExpiryLabel, row.StatusDisplay);
        Assert.Equal(12, row.Quantity);
        Assert.Null(row.ExpiryDate);
    }

    [Fact]
    public void EstoqueSemLoteIdentificado_NaoInventaValidade()
    {
        using var db = TempDatabase.Create();
        var pid = TestDataHelper.SeedSimpleProduct(10, 5, 2, "DIV", "DIVERGENTE");
        SetExtra(pid, """{"controle_validade":true}""");
        Receive(pid, 4, "COM DATA", Today.AddDays(20));
        var snap = ValidityControlService.GetSnapshotLocal(Today);
        Assert.Equal(2, snap.Rows.Count);
        var alert = Assert.Single(snap.Rows, r => r.RowKind == ValidityControlRowKind.UntrackedStock);
        Assert.Equal(ValidityControlEngine.UntrackedStockLabel, alert.StatusDisplay);
        Assert.Equal(6, alert.Quantity);
        Assert.Null(alert.ExpiryDate);
        Assert.Equal(Today.AddDays(20).Date, ProductExpiryService.GetNextExpiry(pid));
    }

    [Fact]
    public void ProximaValidadeCoerenteComLotes()
    {
        using var db = TempDatabase.Create();
        var pid = TestDataHelper.SeedSimpleProduct(0, 5, 2, "NX", "NEXT");
        Receive(pid, 2, "FAR", Today.AddDays(40));
        Receive(pid, 3, "NEAR", Today.AddDays(5));
        Assert.Equal(Today.AddDays(5).Date, ProductExpiryService.GetNextExpiry(pid));
        var near = ValidityControlService.GetSnapshotLocal(Today).Rows
            .Where(r => r.ProductId == pid)
            .OrderBy(r => r.DaysRemaining)
            .First();
        Assert.Equal("NEAR", near.LotDisplay);
        Assert.Equal(5, near.DaysRemaining);
    }

    [Fact]
    public void HomeContador()
    {
        var cards = new ValidityControlCards { Expired = 1, Days7 = 3, Days15 = 2, Days30 = 5 };
        Assert.True(ValidityControlEngine.ShouldShowHomeAlert(cards));
        Assert.Equal("Validades: 1 vencido • 3 até 7 dias • 7 até 30 dias",
            ValidityControlEngine.FormatHomeSummary(cards));
        Assert.False(ValidityControlEngine.ShouldShowHomeAlert(new ValidityControlCards { Ok = 9 }));
    }

    [Fact]
    public void RefreshRecalcula()
    {
        using var db = TempDatabase.Create();
        var pid = TestDataHelper.SeedSimpleProduct(0, 5, 2, "RF", "REFRESH");
        Receive(pid, 1, "A", Today.AddDays(2));
        Assert.Equal(1, ValidityControlService.GetSnapshotLocal(Today).Cards.Days7);
        Receive(pid, 1, "B", Today.AddDays(-1));
        var snap = ValidityControlService.GetSnapshotLocal(Today);
        Assert.Equal(1, snap.Cards.Expired);
        Assert.Equal(1, snap.Cards.Days7);
        Assert.Equal(2, snap.Rows.Count);
    }

    [Fact]
    public void Filtro7DiasNaoUsaRelatorioLegado()
    {
        Assert.Equal(ValidityControlFilterKind.Days7, ValidityControlService.FilterFromLegacyDays(7));
        Assert.Equal(ValidityControlFilterKind.All, ValidityControlService.FilterFromLegacyDays(null));
    }

    [Fact]
    public void RedeLoja_ClientUsaHost()
    {
        using var db = TempDatabase.Create();
        var pid = TestDataHelper.SeedSimpleProduct(0, 5, 2, "CL", "CLI");
        Receive(pid, 8, "LOCAL", Today.AddDays(2));

        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        StoreNetworkClient.TestStatusFeatures = ["session", ValidityControlService.Feature];
        StoreNetworkClient.TestGetValidityControl = () => new ValidityControlSnapshot
        {
            Rows =
            [
                new ValidityControlRow
                {
                    ProductId = pid,
                    ProductName = "HOST",
                    LotDisplay = "HOST",
                    Status = ProductExpiryService.Classify(Today.AddDays(2), Today),
                    StatusDisplay = "ATÉ 7 DIAS",
                },
            ],
            Cards = new ValidityControlCards { Days7 = 1 },
        };
        try
        {
            var snap = ValidityControlService.GetSnapshot(Today);
            Assert.Equal("HOST", Assert.Single(snap.Rows).LotDisplay);
            Assert.Equal(1, StoreNetworkClient.TestGetValidityControlSendCount);
            Assert.Equal("LOCAL", Assert.Single(ValidityControlService.GetSnapshotLocal(Today).Rows).LotDisplay);
        }
        finally
        {
            StoreNetworkClient.ResetPurchaseSalePriceTestHooks();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void HostAntigo_FalhaClaro()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        StoreNetworkClient.TestStatusFeatures = ["session", ProductExpiryService.LotsReadFeature];
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => ValidityControlService.GetSnapshot());
            Assert.Equal(ValidityControlService.HostNeedsUpgradeMessage, ex.Message);
            Assert.Equal(0, StoreNetworkClient.TestGetValidityControlSendCount);
            Assert.Equal(1, StoreNetworkClient.TestStatusFetchCount);
        }
        finally
        {
            StoreNetworkClient.ResetPurchaseSalePriceTestHooks();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void FeatureAnunciadaSemMudarApiVersion()
    {
        Assert.Contains(ValidityControlService.Feature, StoreNetworkHost.AdvertisedFeatures);
        Assert.Contains(ProductExpiryService.LotsReadFeature, StoreNetworkHost.AdvertisedFeatures);
    }

    [Fact]
    public void ProductLotsWindowContinuaFormatando()
    {
        var lot = new ProductLot
        {
            LotNumber = "X",
            Quantity = 2,
            ExpiryDateIso = Today.AddDays(7).ToString("yyyy-MM-dd"),
            PurchaseId = 3,
        };
        var row = Assert.Single(ProductLotListRow.FromLots([lot], Today));
        Assert.Equal("ATÉ 7 DIAS", row.StatusDisplay);
        Assert.True(ProductExpiryService.CanOpenLotsWindow(9));
    }

    static ValidityControlProductInput Product(string name, ProductLot[] lots) =>
        new()
        {
            ProductId = 1,
            Code = "C",
            Name = name,
            Lots = lots,
        };

    static ProductLot Lot(string number, int? days) =>
        new()
        {
            Id = number.GetHashCode(StringComparison.Ordinal),
            LotNumber = number,
            Quantity = 1,
            ExpiryDateIso = days is int d ? Today.AddDays(d).ToString("yyyy-MM-dd") : null,
        };

    static void Receive(int productId, double qty, string lot, DateTime? expiry) =>
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = qty,
            LotNumber = lot,
            ExpiryDate = expiry,
            UnitCost = 1.5,
            PurchaseId = 11,
        });

    static void SetExtra(int productId, string json)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET extra_json = $j WHERE id = $id;";
        cmd.Parameters.AddWithValue("$j", json);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    static void InsertLot(int productId, string lot, DateTime expiry, double qty)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO product_lots (product_id, lot_number, expiry_date, quantity, unit_cost)
            VALUES ($p, $l, $e, $q, 1);
            """;
        cmd.Parameters.AddWithValue("$p", productId);
        cmd.Parameters.AddWithValue("$l", lot);
        cmd.Parameters.AddWithValue("$e", expiry.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$q", qty);
        cmd.ExecuteNonQuery();
    }
}
