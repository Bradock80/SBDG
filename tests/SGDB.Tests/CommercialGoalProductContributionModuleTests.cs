using System.Globalization;
using System.IO;
using System.Text;
using SGDB.Domain.Commercial;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 71B-B8D — integração da contribuição por produto na Meta Comercial.
/// Banco TEMP; nunca deposito.db. Sem instanciar UserControl WPF.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class CommercialGoalProductContributionModuleTests
{
    static readonly DateOnly Sep15 = new(2026, 9, 15);
    static readonly DateOnly Aug15 = new(2026, 8, 15);
    static readonly DateOnly Oct5 = new(2026, 10, 5);
    static readonly CommercialCompetence Sep2026 = CommercialCompetence.Create(2026, 9);
    static readonly CommercialCompetence Aug2026 = CommercialCompetence.Create(2026, 8);
    static readonly CommercialCompetence Oct2026 = CommercialCompetence.Create(2026, 10);

    static readonly string[] Forbidden =
    [
        "venda mais",
        "vender mais",
        "vai gerar",
        "irá gerar",
        "promova",
        "faça promoção",
        "para atingir a meta",
        "para bater a meta",
        "para fechar a meta",
        "deve vender",
        "produto recomendado",
        "produto campeão",
    ];

    static TempDatabase Begin()
    {
        PdvService.TestBeforeInsertSaleItems = null;
        PdvService.TestAfterInsertSaleItems = null;
        PdvService.TestAfterSwapItemUpdate = null;
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(100, "71b-b8d");
        return db;
    }

    [Fact]
    public void Loader_carrega_B8_na_mesma_competencia()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 9, 10);
        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 6, "S1", "Simples");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(LastSaleId(), day);

        var presented = CommercialGoalLoader.Load(Sep2026, Sep15);
        Assert.Equal(Sep2026, presented.Competence);
        Assert.Equal(Sep2026, presented.ProductContribution.Competence);
        Assert.Equal(0, presented.ProductContribution.QueryCount);
        Assert.Equal(
            CommercialGoalProductContributionPresentation.SectionTitle,
            presented.ProductContribution.SectionTitle);
        Assert.Equal(
            CommercialGoalProductContributionPresentationState.Historical,
            presented.ProductContribution.State);
        Assert.Single(presented.ProductContribution.Rows);
        Assert.Equal(pid, presented.ProductContribution.Rows[0].ProductId);
        Assert.Equal("R$ 4,00", presented.ProductContribution.Rows[0].GrossProfitText);
    }

    [Fact]
    public void Top5_preserva_ordem_B8B()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 9, 11);
        var ids = new int[6];
        var gp = new[] { 10m, 50m, 30m, 5m, 40m, 20m };
        for (var i = 0; i < 6; i++)
        {
            var price = (double)(gp[i] + 10m);
            ids[i] = TestDataHelper.SeedSimpleProduct(20, price, 10, $"P{i}", $"Prod {i}");
            TestDataHelper.FinalizeSimpleCashSale(ids[i], 1, price, price);
        }

        SetSessionDateAll(day);
        var financial = CommercialGoalProductContributionService.Load(Sep2026);
        var presented = CommercialGoalLoader.Load(Sep2026, Sep15).ProductContribution;
        var visible = CommercialGoalUi.VisibleContributionRows(presented);

        Assert.Equal(6, financial.Rows.Count);
        Assert.Equal(5, presented.TopContributors.Count);
        Assert.Equal(5, visible.Count);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(financial.Rows[i].ProductId, presented.TopContributors[i].ProductId);
            Assert.Equal(financial.Rows[i].ProductId, visible[i].ProductId);
            Assert.Equal(i + 1, visible[i].Rank);
        }

        Assert.Equal(ids[1], visible[0].ProductId);
        Assert.DoesNotContain(visible, r => r.ProductId == ids[3]);
    }

    [Fact]
    public void NoGoal_e_InvalidGoal_ainda_mostram_B8()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 9, 12);
        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 6, "NG", "Sem meta");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(LastSaleId(), day);

        var noGoal = CommercialGoalLoader.Load(Sep2026, Sep15);
        Assert.Equal(CommercialGoalSettingSource.None, noGoal.GoalSource);
        Assert.False(noGoal.ProductContribution.IsEmpty);
        Assert.Equal(pid, noGoal.ProductContribution.Rows[0].ProductId);

        AppSettingsService.SetSetting(CommercialGoalSettingKeys.Default, "12,000.00");
        var invalid = CommercialGoalLoader.Load(Sep2026, Sep15);
        Assert.Equal(CommercialGoalSettingSource.InvalidDefault, invalid.GoalSource);
        Assert.False(invalid.ProductContribution.IsEmpty);
        Assert.Equal(pid, invalid.ProductContribution.Rows[0].ProductId);
        Assert.Equal("R$ 4,00", invalid.ProductContribution.Rows[0].GrossProfitText);
    }

    [Fact]
    public void Estimated_Unavailable_Empty_Unattributed()
    {
        using var _ = Begin();
        var estimatedPid = TestDataHelper.SeedSimpleProduct(20, 10, 6, "L1", "Legado");
        TestDataHelper.FinalizeSimpleCashSale(estimatedPid, 1, 10, 10);
        InsertLegacySale(estimatedPid, 1, 10, new DateTime(2026, 9, 4));
        SetSessionDateAll(new DateTime(2026, 9, 4));
        var estimated = CommercialGoalLoader.Load(Sep2026, Sep15).ProductContribution;
        Assert.Equal(CommercialGoalProductContributionPresentationState.Estimated, estimated.State);
        Assert.True(estimated.ShowEstimatedBadge);
        Assert.Equal(CommercialGoalProductContributionPresentation.EstimatedBadge, estimated.QualityText);
        Assert.Equal(
            CommercialGoalProductContributionPresentation.QualityEstimated,
            estimated.Rows[0].CostQualityText);
    }

    [Fact]
    public void Unavailable_nao_vira_zero_e_lista_receita()
    {
        using var _ = Begin();
        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 6, "U1", "Indisp");
        var sale = TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(sale.SaleId, new DateTime(2026, 9, 6));
        SetItemQuantityUnavailable(sale.SaleId);

        var presented = CommercialGoalLoader.Load(Sep2026, Sep15).ProductContribution;
        Assert.Equal(CommercialGoalProductContributionPresentationState.ProfitUnavailable, presented.State);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.Rows[0].GrossProfitText);
        Assert.DoesNotContain("R$ 0", presented.Rows[0].GrossProfitText, StringComparison.Ordinal);
        Assert.Equal("R$ 10,00", presented.Rows[0].RevenueText);
        var visible = CommercialGoalUi.VisibleContributionRows(presented);
        Assert.Single(visible);
        Assert.Equal(pid, visible[0].ProductId);
    }

    [Fact]
    public void Empty_e_UnattributedOnly()
    {
        using var _ = Begin();
        var empty = CommercialGoalLoader.Load(Sep2026, Sep15).ProductContribution;
        Assert.Equal(CommercialGoalProductContributionPresentationState.Empty, empty.State);
        Assert.True(empty.IsEmpty);
        Assert.Equal(CommercialGoalProductContributionPresentation.HeadlineEmpty, empty.Headline);
        Assert.Empty(CommercialGoalUi.VisibleContributionRows(empty));

        InsertItemlessSale(new DateTime(2026, 9, 1), 12.50m);
        var only = CommercialGoalLoader.Load(Sep2026, Sep15).ProductContribution;
        Assert.Equal(CommercialGoalProductContributionPresentationState.UnattributedOnly, only.State);
        Assert.True(only.HasUnattributedRevenue);
        Assert.True(only.HasUnattributedGrossProfit);
        Assert.Equal("R$ 12,50", only.UnattributedRevenueText);
        Assert.Equal("R$ 12,50", only.UnattributedGrossProfitText);
        Assert.Empty(CommercialGoalUi.VisibleContributionRows(only));
        Assert.Contains(
            only.Limitations,
            l => l.Key == "unattributed" || l.Title.Contains("receita sem produto", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Gp_negativo_receita_e_limitacoes()
    {
        using var _ = Begin();
        var loss = TestDataHelper.SeedSimpleProduct(20, 10, 40, "N1", "Negativo");
        TestDataHelper.FinalizeSimpleCashSale(loss, 1, 10, 10);
        var kit = TestDataHelper.SeedSimpleProduct(10, 20, 6, "KIT", "Kit");
        var comp = TestDataHelper.SeedSimpleProduct(10, 5, 2, "C1", "Comp");
        SetKit(kit, comp, qty: 2);
        InsertSaleWithItems(new DateTime(2026, 9, 8), 20, (kit, 1, 20, 20, 6));
        SetSessionDateAll(new DateTime(2026, 9, 8));

        var presented = CommercialGoalLoader.Load(Sep2026, Sep15).ProductContribution;
        var negative = presented.Rows.Single(r => r.ProductId == loss);
        Assert.Contains("-", negative.GrossProfitText, StringComparison.Ordinal);
        Assert.Equal(CommercialGoalPresentationTone.Warning, negative.Tone);
        Assert.Contains(
            CommercialGoalProductContributionPresentation.IndicatorNegativeGp,
            negative.Indicators);
        Assert.DoesNotContain("Pare de vender", string.Join(' ', negative.Indicators), StringComparison.OrdinalIgnoreCase);

        Assert.Contains(presented.Limitations, l => l.Key == "exchanges");
        Assert.Contains(presented.Limitations, l => l.Key == "kit");
        Assert.Contains(
            presented.Limitations,
            l => l.Title == CommercialGoalProductContributionPresentation.LimitationKitTitle);
        Assert.DoesNotContain("BOM", string.Join(' ', presented.Limitations.Select(l => l.Title + l.Body)));
    }

    [Fact]
    public void Competencia_anterior_seguinte_e_reload()
    {
        using var _ = Begin();
        var sepPid = TestDataHelper.SeedSimpleProduct(20, 10, 6, "SEP", "Setembro");
        TestDataHelper.FinalizeSimpleCashSale(sepPid, 1, 10, 10);
        SetSessionDate(LastSaleId(), new DateTime(2026, 9, 10));
        var augPid = TestDataHelper.SeedSimpleProduct(20, 20, 5, "AUG", "Agosto");
        TestDataHelper.FinalizeSimpleCashSale(augPid, 1, 20, 20);
        SetSessionDate(LastSaleId(), new DateTime(2026, 8, 10));

        var sep = CommercialGoalLoader.Load(Sep2026, Sep15);
        var aug = CommercialGoalLoader.Load(Aug2026, Aug15);
        var reload = CommercialGoalLoader.Load(Sep2026, Sep15);
        var future = CommercialGoalLoader.Load(Oct2026, Sep15);

        Assert.Equal(sepPid, sep.ProductContribution.Rows[0].ProductId);
        Assert.Equal(augPid, aug.ProductContribution.Rows[0].ProductId);
        Assert.Equal(sepPid, reload.ProductContribution.Rows[0].ProductId);
        Assert.NotEqual(augPid, sep.ProductContribution.Rows[0].ProductId);
        Assert.Equal(CommercialGoalProductContributionPresentationState.Empty, future.ProductContribution.State);
        Assert.True(future.ProductContribution.IsEmpty);
    }

    [Fact]
    public void Query_budget_e_fontes_sem_N1()
    {
        Assert.Equal(0, CommercialGoalLoader.ExpectedQueryCount);
        Assert.Equal(1, CommercialGoalLoader.InheritedProductContributionQueryCount);
        Assert.Equal(1, CommercialGoalProductContributionService.ExpectedQueryCount);
        Assert.Equal(0, CommercialGoalProductContributionPresentation.ExpectedQueryCount);

        var loader = ReadSource("src", "SGDB.App", "Services", "CommercialGoalLoader.cs");
        Assert.DoesNotContain("SELECT", loader, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(loader, "CommercialGoalProductContributionService.Load"));
        Assert.Equal(1, CountOccurrences(loader, "CommercialGoalProductContributionPresentation.Apply"));
        Assert.DoesNotContain("foreach", loader, StringComparison.Ordinal);

        var view = ReadSource("src", "SGDB.App", "Views", "CommercialGoalModuleView.xaml.cs");
        Assert.DoesNotContain("CommercialGoalProductContributionService", view, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", view, StringComparison.Ordinal);

        var b8b = ReadSource("src", "SGDB.App", "Services", "CommercialGoalProductContributionService.cs");
        Assert.Equal(1, CountOccurrences(b8b, "cmd.CommandText"));
    }

    [Fact]
    public void Sem_dependencia_70E_70F_70G_71A_na_B8()
    {
        var files = new[]
        {
            ReadSource("src", "SGDB.App", "Services", "CommercialGoalProductContributionService.cs"),
            ReadSource("src", "SGDB.App", "Models", "CommercialGoalProductContributionPresentation.cs"),
            ContributionXaml(),
        };
        var joined = string.Join('\n', files);
        Assert.DoesNotContain("InventoryAttention", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("PromotionSuggestion", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("PurchaseGuidance", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("ComboIntelligence", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("70E", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("70F", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("70G", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("71A", joined, StringComparison.Ordinal);
    }

    [Fact]
    public void Linguagem_segura_na_secao_B8()
    {
        using var _ = Begin();
        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 6, "LNG", "Ling");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(LastSaleId(), new DateTime(2026, 9, 9));
        var presented = CommercialGoalLoader.Load(Sep2026, Sep15).ProductContribution;
        var visible = Flatten(presented) + ContributionXaml();
        foreach (var phrase in Forbidden)
            Assert.DoesNotContain(phrase, visible, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VisibleContributionRows_unavailable_usa_linhas()
    {
        var row = new CommercialGoalProductContributionRow
        {
            ProductId = 7,
            ProductCode = "U",
            ProductName = "Indisp",
            Revenue = 100m,
            CostQuality = CommercialGoalCostQuality.Unavailable,
        };
        var snap = new CommercialGoalProductContributionSnapshot
        {
            Competence = Sep2026,
            Revenue = 100m,
            CostQuality = CommercialGoalCostQuality.Unavailable,
            GrossProfitAvailable = false,
            QueryCount = 1,
            Rows = [row],
        };
        var presented = CommercialGoalProductContributionPresentation.Apply(snap);
        var visible = CommercialGoalUi.VisibleContributionRows(presented);
        Assert.Equal(CommercialGoalProductContributionPresentationState.ProfitUnavailable, presented.State);
        Assert.Empty(presented.TopContributors);
        Assert.Single(visible);
        Assert.Equal(7, visible[0].ProductId);
        Assert.Equal("R$ 100,00", visible[0].RevenueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, visible[0].GrossProfitText);
    }

    static string Flatten(CommercialGoalProductContributionPresentationSnapshot presented)
    {
        var sb = new StringBuilder();
        sb.Append(presented.Headline).Append(' ')
            .Append(presented.SupportingText).Append(' ')
            .Append(presented.QualityText).Append(' ')
            .Append(presented.QualityExplanation).Append(' ')
            .Append(presented.EmptyText).Append(' ')
            .Append(presented.UnattributedRevenueTitle).Append(' ')
            .Append(presented.UnattributedRevenueExplanation).Append(' ')
            .Append(presented.UnattributedGrossProfitTitle).Append(' ')
            .Append(presented.UnattributedGrossProfitExplanation);
        foreach (var row in presented.Rows)
        {
            sb.Append(row.ProductTitle).Append(' ')
                .Append(row.CostQualityText).Append(' ')
                .Append(row.CostQualityExplanation).Append(' ')
                .Append(string.Join(' ', row.Indicators));
        }

        foreach (var limitation in presented.Limitations)
            sb.Append(limitation.Title).Append(' ').Append(limitation.Body);
        return sb.ToString();
    }

    static string ContributionXaml()
    {
        var xaml = ReadSource("src", "SGDB.App", "Views", "CommercialGoalModuleView.xaml");
        var start = xaml.IndexOf("x:Name=\"ContributionSection\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("AboutNumbersTitle", start, StringComparison.Ordinal);
        return xaml[start..end];
    }

    static void InsertItemlessSale(DateTime day, decimal total)
    {
        using var conn = DatabaseService.OpenConnection();
        using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO sales (session_date, total, payment_type, cancelled, created_at)
            VALUES ($d, $t, 'Fiado', 0, datetime('now','localtime'));
            """;
        ins.Parameters.AddWithValue("$d", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        ins.Parameters.AddWithValue("$t", (double)total);
        ins.ExecuteNonQuery();
    }

    static int InsertLegacySale(int productId, double qty, double unitPrice, DateTime sessionDate)
    {
        var total = ProductPriceHelper.RoundPrice(qty * unitPrice);
        using var conn = DatabaseService.OpenConnection();
        using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO sales (session_date, total, payment_type, cancelled, created_at)
            VALUES ($d, $t, 'Dinheiro', 0, datetime('now','localtime'));
            SELECT last_insert_rowid();
            """;
        ins.Parameters.AddWithValue("$d", sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        ins.Parameters.AddWithValue("$t", total);
        var saleId = Convert.ToInt32(ins.ExecuteScalar());
        using var item = conn.CreateCommand();
        item.CommandText = """
            INSERT INTO sale_items (sale_id, product_id, product_name, quantity, unit_price, subtotal)
            VALUES ($s, $p, 'LEGADO B8D', $q, $u, $t);
            """;
        item.Parameters.AddWithValue("$s", saleId);
        item.Parameters.AddWithValue("$p", productId);
        item.Parameters.AddWithValue("$q", qty);
        item.Parameters.AddWithValue("$u", unitPrice);
        item.Parameters.AddWithValue("$t", total);
        item.ExecuteNonQuery();
        return saleId;
    }

    static int InsertSaleWithItems(
        DateTime day,
        decimal total,
        params (int ProductId, double Qty, double UnitPrice, double Subtotal, double Cost)[] items)
    {
        using var conn = DatabaseService.OpenConnection();
        using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO sales (session_date, total, payment_type, cancelled, created_at)
            VALUES ($d, $t, 'Dinheiro', 0, datetime('now','localtime'));
            SELECT last_insert_rowid();
            """;
        ins.Parameters.AddWithValue("$d", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        ins.Parameters.AddWithValue("$t", (double)total);
        var saleId = Convert.ToInt32(ins.ExecuteScalar());
        foreach (var item in items)
        {
            using var row = conn.CreateCommand();
            row.CommandText = """
                INSERT INTO sale_items (
                  sale_id, product_id, product_code, product_name, unit,
                  quantity, unit_price, subtotal, cost_at_sale)
                VALUES ($s, $p, $c, $n, 'UN', $q, $u, $sub, $cost);
                """;
            row.Parameters.AddWithValue("$s", saleId);
            row.Parameters.AddWithValue("$p", item.ProductId);
            row.Parameters.AddWithValue("$c", item.ProductId.ToString(CultureInfo.InvariantCulture));
            row.Parameters.AddWithValue("$n", "ITEM");
            row.Parameters.AddWithValue("$q", item.Qty);
            row.Parameters.AddWithValue("$u", item.UnitPrice);
            row.Parameters.AddWithValue("$sub", item.Subtotal);
            row.Parameters.AddWithValue("$cost", item.Cost);
            row.ExecuteNonQuery();
        }

        return saleId;
    }

    static void SetKit(int kitId, int componentId, double qty)
    {
        var extra = new ProductExtra
        {
            Composicao = true,
            ComposicaoItens =
            [
                new ProductCompositionItem { ProductId = componentId, Quantity = qty, Name = "Comp" },
            ],
        };
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET extra_json = $j WHERE id = $id;";
        cmd.Parameters.AddWithValue("$j", extra.ToJson());
        cmd.Parameters.AddWithValue("$id", kitId);
        cmd.ExecuteNonQuery();
    }

    static void SetSessionDate(int saleId, DateTime day)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sales SET session_date = $d WHERE id = $id;";
        cmd.Parameters.AddWithValue("$d", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$id", saleId);
        cmd.ExecuteNonQuery();
    }

    static void SetSessionDateAll(DateTime day)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sales SET session_date = $d;";
        cmd.Parameters.AddWithValue("$d", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    static void SetItemQuantityUnavailable(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sale_items SET quantity = 1e999 WHERE sale_id = $s;";
        cmd.Parameters.AddWithValue("$s", saleId);
        cmd.ExecuteNonQuery();
    }

    static int LastSaleId()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(id) FROM sales;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(value, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += value.Length;
        }
        return count;
    }

    static string ReadSource(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relative).ToArray());
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, relative));
    }
}
