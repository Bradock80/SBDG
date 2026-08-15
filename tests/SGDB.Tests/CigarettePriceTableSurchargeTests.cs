using SGDB.Domain.Finance;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69B — Cigarro AVULSO não participa da tabela de preço (cartão/PIX);
/// MAÇO e demais produtos continuam seguindo surcharge_fixed / surcharge_percent.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class CigarettePriceTableSurchargeTests
{
    private const double AvulsoPrice = 1.50;
    private const double MacoPrice = 10.00;
    private const double PackQty = 20;
    private const double OtherPrice = 5.00;

    private static void BeginStandalone() =>
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

    [Theory]
    [InlineData("dinheiro")]
    [InlineData("pix")]
    [InlineData("debito")]
    [InlineData("credito")]
    public void Avulso_NaoRecebeAcrescimoDaTabela(string method)
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var ctx = SeedDefault();

        var got = Alloc(
            [Avulso(ctx.CigaretteId)],
            Pay(method, AvulsoPrice));

        Assert.Equal(0, got);
    }

    [Fact]
    public void Maco_Dinheiro_SemAcrescimo()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var ctx = SeedDefault();

        var got = Alloc([Maco(ctx.CigaretteId)], Pay("dinheiro", MacoPrice));
        Assert.Equal(0, got);
    }

    [Theory]
    [InlineData("pix")]
    [InlineData("debito")]
    [InlineData("credito")]
    public void Maco_FormaDaTabela_RecebeAcrescimoFixo(string method)
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var ctx = SeedDefault();

        var got = Alloc([Maco(ctx.CigaretteId)], Pay(method, MacoPrice));
        Assert.Equal(ctx.FixedSurcharge, got);
    }

    [Fact]
    public void CincoAvulsos_Pix_AcrescimoZero_NaoUsaQuantityComoModo()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var ctx = SeedDefault();

        var got = Alloc(
            [(ctx.CigaretteId, AvulsoPrice, 5, 1)],
            Pay("pix", AvulsoPrice * 5));

        Assert.Equal(0, got);
    }

    [Fact]
    public void DoisMacos_Pix_AcrescimoDuasVezesOFixo()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var ctx = SeedDefault();

        var got = Alloc(
            [(ctx.CigaretteId, MacoPrice, 2, PackQty)],
            Pay("pix", MacoPrice * 2));

        Assert.Equal(ProductPriceHelper.RoundPrice(ctx.FixedSurcharge * 2), got);
    }

    [Fact]
    public void AvulsoEMaco_MesmoProduto_SoMacoRecebe()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var ctx = SeedDefault();

        var got = Alloc(
            [Avulso(ctx.CigaretteId), Maco(ctx.CigaretteId)],
            Pay("pix", AvulsoPrice + MacoPrice));

        Assert.Equal(ctx.FixedSurcharge, got);
    }

    [Theory]
    [InlineData("pix")]
    [InlineData("debito")]
    [InlineData("credito")]
    public void OutroProduto_FormaDaTabela_ContinuaRecebendo(string method)
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var ctx = SeedDefault();

        var got = Alloc(
            [(ctx.OtherId, OtherPrice, 1, 1)],
            Pay(method, OtherPrice));

        Assert.Equal(ctx.FixedSurcharge, got);
    }

    [Fact]
    public void PixChave_NaoMarcadoNaTabela_Zero()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var ctx = SeedDefault();

        var maco = Alloc([Maco(ctx.CigaretteId)], Pay("pix_chave", MacoPrice));
        var other = Alloc([(ctx.OtherId, OtherPrice, 1, 1)], Pay("pix_chave", OtherPrice));

        Assert.Equal(0, maco);
        Assert.Equal(0, other);
    }

    [Fact]
    public void PixChave_Marcado_MacoEOutroSeguemTabela_AvulsoContinuaZero()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var table = PriceTablesService.Create(new PriceTableInput
        {
            Description = "TABELA PIX CHAVE",
            SurchargePercent = 0,
            SurchargeFixed = 1.00,
            ApplyPaymentMethods = ["pix_chave"],
            Active = true,
        });
        var cig = SeedCigarette(table.Id);
        var other = SeedOther(table.Id);

        var avulso = Alloc([Avulso(cig)], Pay("pix_chave", AvulsoPrice));
        var maco = Alloc([Maco(cig)], Pay("pix_chave", MacoPrice));
        var beer = Alloc([(other, OtherPrice, 1, 1)], Pay("pix_chave", OtherPrice));

        Assert.Equal(0, avulso);
        Assert.Equal(table.SurchargeFixed, maco);
        Assert.Equal(table.SurchargeFixed, beer);
    }

    [Fact]
    public void PagamentoMisto_DinheiroNaoCobreMaco_AvulsoIgnorado_MacoRecebe()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var ctx = SeedDefault();

        var got = Alloc(
            [Avulso(ctx.CigaretteId), Maco(ctx.CigaretteId)],
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["dinheiro"] = AvulsoPrice,
                ["pix"] = MacoPrice,
            });

        Assert.Equal(ctx.FixedSurcharge, got);
    }

    [Fact]
    public void PagamentoMisto_DinheiroCobreMaco_SemAcrescimo()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var ctx = SeedDefault();

        var got = Alloc(
            [Avulso(ctx.CigaretteId), Maco(ctx.CigaretteId)],
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["dinheiro"] = MacoPrice,
                ["pix"] = AvulsoPrice,
            });

        Assert.Equal(0, got);
    }

    [Fact]
    public void Maco_PercentualDaTabela_Preservado()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var table = PriceTablesService.Create(new PriceTableInput
        {
            Description = "TABELA PERCENTUAL",
            SurchargePercent = 10,
            SurchargeFixed = 0,
            ApplyPaymentMethods = ["debito", "credito", "pix"],
            Active = true,
        });
        var cig = SeedCigarette(table.Id);
        var expected = FinancialCalculator.CalculateUnitSurcharge(
            MacoPrice, table.SurchargePercent, table.SurchargeFixed);

        var maco = Alloc([Maco(cig)], Pay("pix", MacoPrice));
        var avulso = Alloc([Avulso(cig)], Pay("pix", AvulsoPrice));

        Assert.Equal(expected, maco);
        Assert.Equal(1.00, expected);
        Assert.Equal(0, avulso);
    }

    [Fact]
    public void Maco_FixoMaisPercentual_Preservado_AvulsoNuncaRecebe()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var table = PriceTablesService.Create(new PriceTableInput
        {
            Description = "TABELA FIXO PERCENTUAL",
            SurchargePercent = 10,
            SurchargeFixed = 1.00,
            ApplyPaymentMethods = ["debito", "credito", "pix"],
            Active = true,
        });
        var cig = SeedCigarette(table.Id);
        var expected = FinancialCalculator.CalculateUnitSurcharge(
            MacoPrice, table.SurchargePercent, table.SurchargeFixed);

        var maco = Alloc([Maco(cig)], Pay("credito", MacoPrice));
        var avulso = Alloc([Avulso(cig)], Pay("credito", AvulsoPrice));

        Assert.Equal(expected, maco);
        Assert.Equal(2.00, expected);
        Assert.Equal(0, avulso);
    }

    [Fact]
    public void Avulso_PixChaveMarcado_ContinuaZero()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var table = PriceTablesService.Create(new PriceTableInput
        {
            Description = "TABELA PIX CHAVE AVULSO",
            SurchargePercent = 5,
            SurchargeFixed = 1.00,
            ApplyPaymentMethods = ["pix_chave"],
            Active = true,
        });
        var cig = SeedCigarette(table.Id);

        var got = Alloc([Avulso(cig)], Pay("pix_chave", AvulsoPrice));
        Assert.Equal(0, got);
    }

    private static Fixture SeedDefault()
    {
        var table = PriceTablesService.Create(new PriceTableInput
        {
            Description = "TABELA CIGARRO CARTAO PIX",
            SurchargePercent = 0,
            SurchargeFixed = 1.00,
            ApplyPaymentMethods = ["debito", "credito", "pix"],
            Active = true,
        });
        return new Fixture(
            table.Id,
            table.SurchargeFixed,
            table.SurchargePercent,
            SeedCigarette(table.Id),
            SeedOther(table.Id));
    }

    private static int SeedCigarette(int tableId)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = PackQty,
            QtdAtacado = PackQty,
            PrecoAtacado = MacoPrice,
            PrecoAvulso = AvulsoPrice,
            PriceTableId = tableId,
        }.ToJson();

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                $code, $name, 'Cigarros', 'UN', $sale, 200, 8, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", "CIG69B");
        cmd.Parameters.AddWithValue("$name", "Rothmans Blue");
        cmd.Parameters.AddWithValue("$sale", MacoPrice);
        cmd.Parameters.AddWithValue("$extra", extra);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int SeedOther(int tableId)
    {
        var extra = new ProductExtra { PriceTableId = tableId }.ToJson();
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                $code, $name, 'Bebidas', 'UN', $sale, 50, 3, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", "CERV69B");
        cmd.Parameters.AddWithValue("$name", "Cerveja Lata");
        cmd.Parameters.AddWithValue("$sale", OtherPrice);
        cmd.Parameters.AddWithValue("$extra", extra);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static (int ProductId, double UnitPrice, double Qty, double StockUnitsPerSale) Avulso(int productId) =>
        (productId, AvulsoPrice, 1, 1);

    private static (int ProductId, double UnitPrice, double Qty, double StockUnitsPerSale) Maco(int productId) =>
        (productId, MacoPrice, 1, PackQty);

    private static Dictionary<string, double> Pay(string method, double amount) =>
        new(StringComparer.OrdinalIgnoreCase) { [method] = amount };

    private static double Alloc(
        IEnumerable<(int ProductId, double UnitPrice, double Qty, double StockUnitsPerSale)> lines,
        IReadOnlyDictionary<string, double> amounts) =>
        PriceTablesService.CalcCartSurchargeAllocated(lines, amounts);

    private sealed record Fixture(
        int TableId,
        double FixedSurcharge,
        double PercentSurcharge,
        int CigaretteId,
        int OtherId);
}
