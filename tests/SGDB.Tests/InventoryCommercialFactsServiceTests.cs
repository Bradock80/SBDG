using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 70F-B2 — batch SQL de fatos comerciais. Bancos isolados. Sem deposito.db. Sem UI.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class InventoryCommercialFactsServiceTests
{
    private static TempDatabase Begin()
    {
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        return db;
    }

    static int InsertProduct(
        string name,
        double sale,
        double cost,
        string? extraJson = null,
        string? group = null,
        string code = "F")
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, group_name, unit, sale_price, stock, cost_price, extra_json, active
            ) VALUES (
                $code, $name, $group, 'UN', $sale, 10, $cost, $extra, 1
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$group", (object?)group ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sale", sale);
        cmd.Parameters.AddWithValue("$cost", cost);
        cmd.Parameters.AddWithValue("$extra", extraJson ?? "{}");
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Fact]
    public void Lista_vazia_nao_consulta()
    {
        using var db = Begin();
        var snap = InventoryCommercialFactsService.Load([]);
        Assert.Equal(0, snap.QueryCount);
        Assert.Empty(snap.Rows);
        Assert.Empty(snap.ByProductId);
    }

    [Fact]
    public void Lista_nula_nao_consulta()
    {
        using var db = Begin();
        var snap = InventoryCommercialFactsService.Load(null);
        Assert.Equal(0, snap.QueryCount);
        Assert.Empty(snap.Rows);
    }

    [Fact]
    public void Ids_duplicados_nao_multiplicam_trabalho()
    {
        using var db = Begin();
        var id = InsertProduct("Leite", 8, 5, code: "DUP");
        var snap = InventoryCommercialFactsService.Load([id, id, id]);
        Assert.Equal(1, snap.QueryCount);
        Assert.Equal(id, Assert.Single(snap.RequestedProductIds));
        Assert.Single(snap.Rows);
        Assert.True(snap.ByProductId[id].CanEvaluateFinancialScenario);
    }

    [Fact]
    public void Multiplos_produtos_uma_query()
    {
        using var db = Begin();
        var a = InsertProduct("A", 10, 4, code: "A1");
        var b = InsertProduct("B", 0, 3, code: "B1");
        var snap = InventoryCommercialFactsService.Load([a, b]);
        Assert.Equal(1, snap.QueryCount);
        Assert.Equal(2, snap.Rows.Count);
        Assert.True(snap.ByProductId[a].CanEvaluateFinancialScenario);
        Assert.False(snap.ByProductId[b].CanEvaluateFinancialScenario);
        Assert.Equal(InventoryCommercialPriceQuality.Unusable, snap.ByProductId[b].PriceQuality);
    }

    [Fact]
    public void Produto_inexistente_explicitado()
    {
        using var db = Begin();
        var snap = InventoryCommercialFactsService.Load([9_999_999]);
        Assert.Equal(1, snap.QueryCount);
        var facts = Assert.Single(snap.Rows);
        Assert.False(facts.ProductFound);
        Assert.Equal(InventoryCommercialFactsReason.MissingProduct, Assert.Single(facts.LimitationReasons));
        Assert.False(facts.CanEvaluateFinancialScenario);
    }

    [Fact]
    public void PermiteVenda_false_vem_do_batch()
    {
        using var db = Begin();
        var extra = new ProductExtra { PermiteVenda = false };
        var id = InsertProduct("Bloqueado", 10, 4, extra.ToJson(), code: "NV");
        var facts = InventoryCommercialFactsService.Load([id]).ByProductId[id];
        Assert.False(facts.AllowsSale);
        Assert.False(facts.CanEvaluateFinancialScenario);
        Assert.Contains(InventoryCommercialFactsReason.SaleNotAllowed, facts.LimitationReasons);
    }

    [Fact]
    public void Atacado_configurado_nao_substitui_CatalogSalePrice()
    {
        using var db = Begin();
        var extra = new ProductExtra { QtdAtacado = 12, PrecoAtacado = 9.5 };
        var id = InsertProduct("Atacado", 12, 7, extra.ToJson(), code: "AT");
        var facts = InventoryCommercialFactsService.Load([id]).ByProductId[id];
        Assert.Equal(12, facts.CatalogSalePrice);
        Assert.Equal(9.5, facts.WholesalePrice);
        Assert.Equal(12, facts.WholesaleMinimumQuantity);
        Assert.True(facts.HasWholesalePricing);
        Assert.True(facts.CanEvaluateFinancialScenario);
    }

    [Fact]
    public void Cigarro_avulso_identificado_sem_dividir_custo()
    {
        using var db = Begin();
        var extra = new ProductExtra { PrecoAvulso = 1.15 };
        var id = InsertProduct("Marlboro HW20", 23, 18, extra.ToJson(), "Cigarros", "CG");
        var facts = InventoryCommercialFactsService.Load([id]).ByProductId[id];
        Assert.True(facts.IsCigaretteProduct);
        Assert.Equal(23, facts.CatalogSalePrice);
        Assert.Equal(18, facts.CurrentAverageCost);
        Assert.Equal(1.15, facts.UnitSalePrice);
        Assert.False(facts.CanEvaluateFinancialScenario);
        Assert.Contains(InventoryCommercialFactsReason.AmbiguousSaleUnit, facts.LimitationReasons);
    }

    [Fact]
    public void Composto_carregado_sem_explodir_bom()
    {
        using var db = Begin();
        var extra = new ProductExtra
        {
            Composicao = true,
            ComposicaoItens = [new ProductCompositionItem { ProductId = 2, Quantity = 1 }],
        };
        var id = InsertProduct("Kit", 30, 20, extra.ToJson(), code: "KIT");
        var facts = InventoryCommercialFactsService.Load([id]).ByProductId[id];
        Assert.True(facts.IsCompositionProduct);
        Assert.Equal(20, facts.CurrentAverageCost);
        Assert.False(facts.CanEvaluateFinancialScenario);
    }

    [Fact]
    public void Promo_morta_nao_influencia_preco_nem_qualidade()
    {
        using var db = Begin();
        var extra = new ProductExtra
        {
            PrecoPromocional = 1,
            PromoInicio = "2020-01-01",
            PromoFim = "2099-12-31",
            DescontoPercent = 90,
        };
        var id = InsertProduct("Promo", 15, 8, extra.ToJson(), code: "PR");
        var facts = InventoryCommercialFactsService.Load([id]).ByProductId[id];
        Assert.Equal(15, facts.CatalogSalePrice);
        Assert.Equal(8, facts.CurrentAverageCost);
        Assert.Equal(InventoryCommercialPriceQuality.Usable, facts.PriceQuality);
        Assert.True(facts.CanEvaluateFinancialScenario);
        Assert.DoesNotContain(1, new[] { facts.CatalogSalePrice, facts.CurrentAverageCost });
    }

    [Fact]
    public void Preco_compra_nao_substitui_cost_price()
    {
        using var db = Begin();
        var extra = new ProductExtra { PrecoCompra = 1.11 };
        var id = InsertProduct("Ultimo", 10, 7.77, extra.ToJson(), code: "UC");
        var facts = InventoryCommercialFactsService.Load([id]).ByProductId[id];
        Assert.Equal(7.77, facts.CurrentAverageCost);
        Assert.NotEqual(1.11, facts.CurrentAverageCost);
    }

    [Fact]
    public void Lot_unit_cost_nao_substitui_cost_price()
    {
        using var db = Begin();
        var id = InsertProduct("Lote", 10, 4.4, code: "LT");
        using (var conn = DatabaseService.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO product_lots (product_id, lot_number, quantity, unit_cost)
                VALUES ($id, 'L1', 8, 99.9);
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        var facts = InventoryCommercialFactsService.Load([id]).ByProductId[id];
        Assert.Equal(4.4, facts.CurrentAverageCost);
        Assert.NotEqual(99.9, facts.CurrentAverageCost);
    }

    [Fact]
    public void Cost_at_sale_nao_substitui_cost_price()
    {
        using var db = Begin();
        var id = InsertProduct("Venda", 10, 5.5, code: "CS");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        int saleId;
        using (var sale = conn.CreateCommand())
        {
            sale.Transaction = tx;
            sale.CommandText = """
                INSERT INTO sales (session_date, total, payment_type, cancelled)
                VALUES ('2026-09-01', 10, 'Dinheiro', 0);
                SELECT last_insert_rowid();
                """;
            saleId = Convert.ToInt32(sale.ExecuteScalar());
        }
        using (var item = conn.CreateCommand())
        {
            item.Transaction = tx;
            item.CommandText = """
                INSERT INTO sale_items (
                    sale_id, product_id, product_code, product_name, quantity, unit_price, subtotal, cost_at_sale
                ) VALUES ($sale, $pid, 'CS', 'Venda', 1, 10, 10, 0.01);
                """;
            item.Parameters.AddWithValue("$sale", saleId);
            item.Parameters.AddWithValue("$pid", id);
            item.ExecuteNonQuery();
        }
        tx.Commit();

        var facts = InventoryCommercialFactsService.Load([id]).ByProductId[id];
        Assert.Equal(5.5, facts.CurrentAverageCost);
        Assert.NotEqual(0.01, facts.CurrentAverageCost);
    }

    [Fact]
    public void Query_budget_B2_e_composicao_futura_8()
    {
        using var db = Begin();
        var id = InsertProduct("Q", 10, 6, code: "Q1");
        Assert.Equal(1, InventoryCommercialFactsService.Load([id]).QueryCount);
        Assert.Equal(1, InventoryCommercialFactsService.ExpectedQueryCount);
        Assert.Equal(0, InventoryCommercialEligibilityEngine.ExpectedQueryCount);
        Assert.Equal(6, InventoryIntelligenceService.ExpectedQueryCount);
        Assert.Equal(1, InventoryProjectionService.ExpectedLotsQueryCount);
        Assert.Equal(
            8,
            InventoryIntelligenceService.ExpectedQueryCount
            + InventoryProjectionService.ExpectedLotsQueryCount
            + InventoryCommercialEligibilityEngine.ExpectedQueryCount
            + InventoryCommercialFactsService.ExpectedQueryCount);
    }

    [Fact]
    public void Fatos_crus_nao_sao_arredondados_na_leitura()
    {
        using var db = Begin();
        var id = InsertProduct("Raw", 10.123, 6.789, code: "RW");
        var facts = InventoryCommercialFactsService.Load([id]).ByProductId[id];
        Assert.Equal(10.123, facts.CatalogSalePrice);
        Assert.Equal(6.789, facts.CurrentAverageCost);
        Assert.NotEqual(ProductPriceHelper.RoundPrice(10.123), facts.CatalogSalePrice);
    }

    [Fact]
    public void Deterministico_entre_loads()
    {
        using var db = Begin();
        var extra = new ProductExtra { QtdAtacado = 6, PrecoAtacado = 8 };
        var id = InsertProduct("Det", 10, 5, extra.ToJson(), code: "DT");
        var a = InventoryCommercialFactsService.Load([id]).ByProductId[id];
        var b = InventoryCommercialFactsService.Load([id]).ByProductId[id];
        Assert.Equal(a.CatalogSalePrice, b.CatalogSalePrice);
        Assert.Equal(a.CurrentAverageCost, b.CurrentAverageCost);
        Assert.Equal(a.LimitationReasons, b.LimitationReasons);
        Assert.Equal(a.CanEvaluateFinancialScenario, b.CanEvaluateFinancialScenario);
    }

    [Fact]
    public void Service_e_engine_nao_tem_n_mais_um_nem_promo_nem_fallback()
    {
        var service = File.ReadAllText(FindSource("src", "SGDB.App", "Services", "InventoryCommercialFactsService.cs"));
        var engine = File.ReadAllText(FindSource("src", "SGDB.App", "Services", "InventoryCommercialFactsEngine.cs"));
        var model = File.ReadAllText(FindSource("src", "SGDB.App", "Models", "InventoryCommercialFacts.cs"));
        foreach (var source in new[] { service, engine, model })
        {
            Assert.DoesNotContain("GetByProductId", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GetById", source, StringComparison.Ordinal);
            Assert.DoesNotContain("preco_promocional", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("promo_inicio", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("promo_fim", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("desconto_percent", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("System.Windows", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTime.Now", source, StringComparison.Ordinal);
            Assert.DoesNotContain("MarginOnSale", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SaleFromCostAndMargin", source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("product_lots", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sale_items", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preco_compra", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrecoCompra", service, StringComparison.Ordinal);
        Assert.DoesNotContain("PrecoPromocional", service, StringComparison.Ordinal);
        Assert.DoesNotContain("PdvService", service, StringComparison.Ordinal);
        Assert.DoesNotContain("FinalizeSale", service, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(service, "OpenConnection"));
        Assert.Equal(1, CountOccurrences(service, "FROM products"));
        Assert.Contains("ExpectedQueryCount = 1", service, StringComparison.Ordinal);
        Assert.Contains("WHERE p.id IN", service, StringComparison.Ordinal);

        var b1 = File.ReadAllText(FindSource("src", "SGDB.App", "Services", "InventoryCommercialEligibilityEngine.cs"));
        Assert.DoesNotContain("InventoryCommercialFacts", b1, StringComparison.Ordinal);
        Assert.DoesNotContain("sale_price", b1, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cost_price", b1, StringComparison.OrdinalIgnoreCase);
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

    static string FindSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
