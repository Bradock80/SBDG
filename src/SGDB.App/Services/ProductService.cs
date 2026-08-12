using Microsoft.Data.Sqlite;
using SGDB.Domain.Products;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public sealed class ProductInput
{
    public string? Code { get; set; }
    public string? Barcode { get; set; }
    public required string Name { get; set; }
    public string? GroupName { get; set; }
    public string Unit { get; set; } = "UN";
    public double CostPrice { get; set; }
    public double SalePrice { get; set; }
    public int MinStock { get; set; } = 5;
    public double Stock { get; set; }
    public double StockFridge { get; set; }
    public int StockFridgeMin { get; set; }
    public string? Location { get; set; }
    public ProductExtra Extra { get; set; } = new();
    public bool Active { get; set; } = true;
}

public static class ProductService
{
    public static IReadOnlyList<Product> List(
        string? search = null,
        string ativo = "ativos",
        string? group = null,
        string? dateFrom = null,
        string? dateTo = null,
        string dateMode = "none")
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.ListProducts(search, ativo, group, dateFrom, dateTo, dateMode);
        return ListLocal(search, ativo, group, dateFrom, dateTo, dateMode);
    }

    public static IReadOnlyList<Product> ListLocal(
        string? search = null,
        string ativo = "ativos",
        string? group = null,
        string? dateFrom = null,
        string? dateTo = null,
        string dateMode = "none")
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();

        var sql = """
            SELECT id, code, barcode, name, group_name, unit, cost_price, sale_price,
                   min_stock, stock, location, extra_json, active, created_at, IFNULL(stock_fridge, 0), IFNULL(stock_fridge_min, 0)
            FROM products
            WHERE 1=1
            """;

        if (ativo == "ativos")
            sql += " AND active = 1";
        else if (ativo == "inativos")
            sql += " AND active = 0";

        if (!string.IsNullOrWhiteSpace(group))
        {
            sql += " AND group_name = $group";
            cmd.Parameters.AddWithValue("$group", group.Trim().ToUpperInvariant());
        }

        var mode = (dateMode ?? "none").Trim().ToLowerInvariant();
        var isoFrom = DateBrHelper.ToIso(dateFrom);
        var isoTo = DateBrHelper.ToIso(dateTo);
        if ((mode is "created" or "entry") && (!string.IsNullOrEmpty(isoFrom) || !string.IsNullOrEmpty(isoTo)))
        {
            if (mode == "created")
            {
                if (!string.IsNullOrEmpty(isoFrom))
                {
                    sql += " AND date(created_at) >= $from";
                    cmd.Parameters.AddWithValue("$from", isoFrom);
                }
                if (!string.IsNullOrEmpty(isoTo))
                {
                    sql += " AND date(created_at) <= $to";
                    cmd.Parameters.AddWithValue("$to", isoTo);
                }
            }
            else
            {
                sql += """
                     AND EXISTS (
                        SELECT 1
                        FROM purchase_items pi
                        INNER JOIN purchases pu ON pu.id = pi.purchase_id
                        WHERE pi.product_id = products.id
                          AND pu.status != 'cancelada'
                    """;
                if (!string.IsNullOrEmpty(isoFrom))
                {
                    sql += " AND pu.entry_date >= $from";
                    cmd.Parameters.AddWithValue("$from", isoFrom);
                }
                if (!string.IsNullOrEmpty(isoTo))
                {
                    sql += " AND pu.entry_date <= $to";
                    cmd.Parameters.AddWithValue("$to", isoTo);
                }
                sql += ")";
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var raw = search.Trim();
            sql += """
                 AND (
                    UPPER(IFNULL(code,'')) LIKE $like ESCAPE '\'
                    OR UPPER(name) LIKE $like ESCAPE '\'
                    OR IFNULL(barcode,'') LIKE $like ESCAPE '\'
                 )
                """;
            var escaped = raw.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
            cmd.Parameters.AddWithValue("$like", $"%{escaped.ToUpperInvariant()}%");
        }

        sql += " ORDER BY name LIMIT 1000";
        cmd.CommandText = sql;
        var list = ReadAll(cmd);
        AttachLastEntries(list);
        return list;
    }

    private static void AttachLastEntries(List<Product> products)
    {
        if (products.Count == 0)
            return;
        var map = PurchaseService.GetLastEntries(products.Select(p => p.Id));
        foreach (var p in products)
        {
            if (map.TryGetValue(p.Id, out var entry))
                p.LastEntryDisplay = PurchaseService.FormatLastEntryDisplay(entry, p.StockUnitLabel);
        }
    }

    public static Product? GetById(int id)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.GetProduct(id);
        return GetByIdLocal(id);
    }

    public static Product? GetByIdLocal(int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, code, barcode, name, group_name, unit, cost_price, sale_price,
                   min_stock, stock, location, extra_json, active, created_at, IFNULL(stock_fridge, 0), IFNULL(stock_fridge_min, 0)
            FROM products WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        return ReadAll(cmd).FirstOrDefault();
    }

    /// <summary>Busca produto ativo por código de barras, tolerando zeros à esquerda.</summary>
    public static Product? FindByBarcode(string? barcode)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.FindProductByBarcode(barcode);
        return FindByBarcodeLocal(barcode);
    }

    public static Product? FindByBarcodeLocal(string? barcode)
    {
        var digits = TextNorm.NormalizeBarcode(barcode);
        if (digits is null)
            return null;

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, code, barcode, name, group_name, unit, cost_price, sale_price,
                   min_stock, stock, location, extra_json, active, created_at, IFNULL(stock_fridge, 0), IFNULL(stock_fridge_min, 0)
            FROM products WHERE active = 1 AND barcode = $bc LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$bc", digits);
        var hit = ReadAll(cmd).FirstOrDefault();
        if (hit is not null)
            return hit;

        var stripped = digits.TrimStart('0');
        if (string.IsNullOrEmpty(stripped) || stripped == digits)
            return null;

        using var conn2 = DatabaseService.OpenConnection();
        using var likeCmd = conn2.CreateCommand();
        likeCmd.CommandText = """
            SELECT id, code, barcode, name, group_name, unit, cost_price, sale_price,
                   min_stock, stock, location, extra_json, active, created_at, IFNULL(stock_fridge, 0), IFNULL(stock_fridge_min, 0)
            FROM products
            WHERE active = 1 AND barcode IS NOT NULL AND barcode != '' AND barcode LIKE $like LIMIT 20;
            """;
        likeCmd.Parameters.AddWithValue("$like", $"%{stripped}%");
        foreach (var p in ReadAll(likeCmd))
        {
            var stored = (p.Barcode ?? "").TrimStart('0');
            if (stored == stripped)
                return p;
        }
        return null;
    }

    /// <summary>Busca pelo código de barras da unidade ou do fardo (extra_json).</summary>
    public static Product? FindByBarcodeOrPack(string? barcode)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.FindProductByBarcode(barcode);
        return FindByBarcodeOrPackLocal(barcode);
    }

    public static Product? FindByBarcodeOrPackLocal(string? barcode)
    {
        var hit = FindByBarcodeLocal(barcode);
        if (hit is not null)
            return hit;

        var digits = TextNorm.NormalizeBarcode(barcode);
        if (digits is null)
            return null;

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, code, barcode, name, group_name, unit, cost_price, sale_price,
                   min_stock, stock, location, extra_json, active, created_at, IFNULL(stock_fridge, 0), IFNULL(stock_fridge_min, 0)
            FROM products
            WHERE active = 1 AND IFNULL(extra_json,'') LIKE $like
            LIMIT 40;
            """;
        cmd.Parameters.AddWithValue("$like", "%\"barcode_embalagem\":\"" + digits + "%");
        foreach (var p in ReadAll(cmd))
        {
            var pack = ProductExtra.Parse(p.ExtraJson).BarcodeEmbalagem;
            if (TextNorm.NormalizeBarcode(pack) == digits)
                return p;
        }
        return null;
    }

    /// <summary>
    /// Completa marca/grupo em produtos que ainda estão sem (ex.: importações antigas).
    /// </summary>
    public static int BackfillMissingClassifications()
    {
        if (StoreNetworkMode.IsClient)
            return 0;
        var updated = 0;
        foreach (var product in List(ativo: "todos"))
        {
            var extra = ProductExtra.Parse(product.ExtraJson);
            var group = product.GroupName;
            var brandBefore = extra.Marca ?? "";
            var groupBefore = group ?? "";
            var packBefore = extra.BarcodeEmbalagem ?? "";
            var fatorBefore = extra.FatorEmbalagem;
            var costBefore = product.CostPrice;
            ProductClassificationHelper.FillMissing(product.Name, ref group, extra);

            // Barras fardo igual às barras da unidade → limpa (evita bipe virar CX no PDV).
            var distinctPack = TextNorm.DistinctPackBarcode(extra.BarcodeEmbalagem, product.Barcode);
            if (!string.Equals(distinctPack ?? "", packBefore, StringComparison.Ordinal))
                extra.BarcodeEmbalagem = distinctPack;

            // DP16X29G etc.: preenche fator se ainda estiver 1.
            if (extra.FatorEmbalagem < 2)
            {
                var inferred = NfeXmlImportService.InferPackFactorFromProductName(product.Name);
                if (inferred >= 2)
                {
                    extra.FatorEmbalagem = inferred;
                    if (extra.QtdAtacado < 2)
                        extra.QtdAtacado = inferred;
                }
            }

            var factor = extra.FatorEmbalagem >= 2 ? extra.FatorEmbalagem
                : (extra.QtdAtacado >= 2 ? extra.QtdAtacado : 1);
            var compraBefore = extra.PrecoCompra;
            var cost = product.CostPrice;
            // Preço Compra antigo = total da CX → grava unitário (só se realmente parecer fardo)
            if (factor >= 2 && extra.PrecoCompra > 0)
            {
                var packTotal = cost * factor;
                if (cost > 0 && Math.Abs(extra.PrecoCompra - packTotal) <= Math.Max(0.05, packTotal * 0.2))
                    extra.PrecoCompra = Math.Round(cost, 4);
                else if (Math.Abs(extra.PrecoCompra - cost) < 0.05
                         && product.SalePrice > 0
                         && extra.PrecoCompra >= product.SalePrice * 2)
                {
                    extra.PrecoCompra = Math.Round(extra.PrecoCompra / factor, 4);
                    cost = extra.PrecoCompra;
                }
            }

            var changed =
                !string.Equals(brandBefore, extra.Marca ?? "", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(groupBefore, group ?? "", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(packBefore, extra.BarcodeEmbalagem ?? "", StringComparison.Ordinal)
                || Math.Abs(fatorBefore - extra.FatorEmbalagem) > 0.0001
                || Math.Abs(compraBefore - extra.PrecoCompra) > 0.0001
                || Math.Abs(costBefore - cost) > 0.0001;

            if (!changed)
                continue;

            Update(product.Id, new ProductInput
            {
                Code = product.Code,
                Barcode = product.Barcode,
                Name = product.Name ?? "",
                GroupName = group,
                Unit = string.IsNullOrWhiteSpace(product.Unit) ? "UN" : product.Unit,
                CostPrice = cost,
                SalePrice = product.SalePrice,
                MinStock = product.MinStock,
                Stock = product.Stock,
                StockFridge = product.StockFridge,
                StockFridgeMin = product.StockFridgeMin,
                Location = product.Location,
                Extra = extra,
                Active = product.Active,
            });
            updated++;
        }
        return updated;
    }

    /// <summary>
    /// Corrige custo/compra divididos 1× ou 2× pelo fator do fardo
    /// (ex.: Guaraná Diet 0,17 → 6,29; Chokito 0,08 → 2,40).
    /// </summary>
    public static int BackfillFixDoubleDividedUnitCosts()
    {
        var updated = 0;
        foreach (var product in List(ativo: "ativos"))
        {
            var extra = ProductExtra.Parse(product.ExtraJson);
            var factor = extra.FatorEmbalagem >= 2 ? extra.FatorEmbalagem
                : extra.QtdAtacado >= 2 ? extra.QtdAtacado : 0;
            if (factor < 2)
            {
                var inferred = NfeXmlImportService.InferPackFactorFromProductName(product.Name);
                if (inferred >= 2)
                    factor = inferred;
            }
            if (factor < 2)
                continue;

            // Isqueiro / cigarro: não usar heurística de × fator (cigarro trata maço à parte)
            var nameUp = (product.Name ?? "").ToUpperInvariant();
            var groupUp = (product.GroupName ?? "").ToUpperInvariant();
            if (nameUp.Contains("ISQ") || nameUp.Contains("ISQUEIRO") || nameUp.Contains("BIC MIN")
                || groupUp.Contains("CIGARR") || nameUp.Contains("DUNHILL") || nameUp.Contains("ROTH")
                || nameUp.Contains("LUCKY STRIKE") || nameUp.Contains("MARLBORO")
                || nameUp.Contains("HOLLYWOOD") || nameUp.Contains("CARLTON"))
                continue;

            var cost = product.CostPrice;
            var sale = product.SalePrice;
            if (cost <= 0 || sale <= 0)
                continue;

            var marginNow = ProductPriceHelper.MarginOnSale(cost, sale);
            if (marginNow < 85 || cost / sale >= 0.15)
                continue;

            var once = ProductPriceHelper.RoundPrice(cost * factor);
            var twice = ProductPriceHelper.RoundPrice(cost * factor * factor);
            var marginOnce = once < sale ? ProductPriceHelper.MarginOnSale(once, sale) : -1;
            var marginTwice = twice < sale ? ProductPriceHelper.MarginOnSale(twice, sale) : -1;

            double fixedCost;
            double marginFixed;

            // Ainda absurdo após ×1 fator → provavelmente dividiu 2 vezes
            if (once < sale && marginOnce >= 80 && marginTwice is >= 10 and <= 70)
            {
                fixedCost = twice;
                marginFixed = marginTwice;
            }
            else if (once < sale && marginOnce is >= 15 and <= 70 && marginNow >= 88)
            {
                fixedCost = once;
                marginFixed = marginOnce;
            }
            else
                continue;

            if (fixedCost <= cost * 1.5)
                continue;

            var compra = extra.PrecoCompra;
            if (compra <= 0 || Math.Abs(compra - cost) < 0.05 || compra < sale / factor)
                extra.PrecoCompra = fixedCost;

            if (extra.FatorEmbalagem < 2)
                extra.FatorEmbalagem = factor;
            if (extra.QtdAtacado < 2)
                extra.QtdAtacado = factor;

            extra.LucroPercent = marginFixed;

            Update(product.Id, new ProductInput
            {
                Code = product.Code,
                Barcode = product.Barcode,
                Name = product.Name ?? "",
                GroupName = product.GroupName,
                Unit = string.IsNullOrWhiteSpace(product.Unit) ? "UN" : product.Unit,
                CostPrice = fixedCost,
                SalePrice = product.SalePrice,
                MinStock = product.MinStock,
                Stock = product.Stock,
                StockFridge = product.StockFridge,
                StockFridgeMin = product.StockFridgeMin,
                Location = product.Location,
                Extra = extra,
                Active = product.Active,
            });
            updated++;
        }
        return updated;
    }

    /// <summary>
    /// Cigarros: Preço Compra, Custo e Venda = valor do maço (ex.: 13,35 / 14,50).
    /// Estoque continua em cigarros; PDV do maço lança fator (20).
    /// </summary>
    public static int BackfillFixCigarettePrices()
    {
        var updated = 0;
        using var conn = DatabaseService.OpenConnection();

        foreach (var product in List(ativo: "ativos"))
        {
            if (!ProductClassificationHelper.UsesPackPurchasePrice(product.Name, product.GroupName))
                continue;

            var extra = ProductExtra.Parse(product.ExtraJson);
            var costBefore = product.CostPrice;
            var saleBefore = product.SalePrice;
            var compraBefore = extra.PrecoCompra;
            var factor = extra.FatorEmbalagem >= 2 ? extra.FatorEmbalagem
                : extra.QtdAtacado >= 2 ? extra.QtdAtacado : 20;

            double? purchaseUnit = null;
            double? purchaseQty = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT pi.unit_price, pi.quantity
                    FROM purchase_items pi
                    JOIN purchases pu ON pu.id = pi.purchase_id
                    WHERE pi.product_id = $pid
                    ORDER BY pu.id DESC
                    LIMIT 1;
                    """;
                cmd.Parameters.AddWithValue("$pid", product.Id);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    purchaseUnit = reader.GetDouble(0);
                    purchaseQty = reader.GetDouble(1);
                }
            }

            var cigsPerPack = ProductPriceHelper.ResolveCigarettesPerPack(product.Name, factor);

            // Venda do maço (antes do custo, para detectar cartela vs maço)
            var packSale = extra.PrecoAtacado;
            if (packSale <= 0)
            {
                if (product.SalePrice >= 5)
                    packSale = product.SalePrice;
                else if (product.SalePrice > 0)
                    packSale = ProductPriceHelper.RoundPrice(product.SalePrice * cigsPerPack);
            }

            // Compra/custo do maço = total ÷ qtd de maços
            double packCost;
            if (purchaseUnit is > 0 && purchaseQty is > 0)
            {
                packCost = ProductPriceHelper.CigarettePackCostFromTotal(
                    purchaseUnit.Value * purchaseQty.Value, purchaseQty.Value, cigsPerPack);
            }
            else if (product.CostPrice > 0 && product.CostPrice < 5)
                packCost = ProductPriceHelper.PackCostFromUnit(product.CostPrice, cigsPerPack);
            else
                packCost = product.CostPrice > 0 ? product.CostPrice : extra.PrecoCompra;

            // Cartela gravada como custo do maço (ex.: 142,60 com venda 8,50 e fator 20)
            packCost = ProductPriceHelper.UnitCostForSoldLine(
                packCost, packSale > 0 ? packSale : product.SalePrice, extra,
                product.Name, product.GroupName);

            extra.FatorEmbalagem = cigsPerPack;
            extra.QtdAtacado = cigsPerPack;
            extra.PrecoCompra = packCost;
            extra.PrecoAtacado = packSale;
            if (packSale > 0)
                extra.LucroPercent = ProductPriceHelper.MarginOnSale(packCost, packSale);

            var newSale = packSale > 0 ? packSale : product.SalePrice;
            if (Math.Abs(costBefore - packCost) < 0.005
                && Math.Abs(saleBefore - newSale) < 0.005
                && Math.Abs(compraBefore - packCost) < 0.005)
                continue;

            Update(product.Id, new ProductInput
            {
                Code = product.Code,
                Barcode = product.Barcode,
                Name = product.Name ?? "",
                GroupName = product.GroupName,
                Unit = string.IsNullOrWhiteSpace(product.Unit) ? "UN" : product.Unit,
                CostPrice = packCost,
                SalePrice = newSale,
                MinStock = product.MinStock,
                Stock = product.Stock,
                StockFridge = product.StockFridge,
                StockFridgeMin = product.StockFridgeMin,
                Location = product.Location,
                Extra = extra,
                Active = product.Active,
            });
            updated++;
        }
        return updated;
    }

    public static Product Create(ProductInput input)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.CreateProduct(input);
        return CreateLocal(input);
    }

    public static Product CreateLocal(ProductInput input)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("criar produto");
        // Sempre limpa embalagem/caixa do nome no cadastro (NF-e e demais origens).
        input.Name = ProductClassificationHelper.SanitizeProductName(input.Name);
        var data = Normalize(input);
        data.Code = ResolveCode(data.Code, data.Name);

        if (string.IsNullOrWhiteSpace(data.Name))
            throw new InvalidOperationException("Informe a descrição do produto.");

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, barcode, name, group_name, unit, cost_price, sale_price,
                min_stock, stock, stock_fridge, stock_fridge_min, location, extra_json, active, created_at
            ) VALUES (
                $code, $barcode, $name, $group_name, $unit, $cost_price, $sale_price,
                $min_stock, $stock, $stock_fridge, $stock_fridge_min, $location, $extra_json, $active,
                datetime('now','localtime')
            );
            SELECT last_insert_rowid();
            """;
        BindProduct(cmd, data);
        var id = Convert.ToInt32(cmd.ExecuteScalar());
        SyncCatalogFromProduct(data);
        return GetByIdLocal(id) ?? throw new InvalidOperationException("Falha ao criar produto.");
    }

    /// <summary>
    /// Se o nome do produto (ou o da NF) ainda tiver embalagem, grava a versão limpa no cadastro.
    /// </summary>
    public static Product EnsureCleanCatalogName(Product product, string? nfeName = null)
    {
        var fromProduct = ProductClassificationHelper.SanitizeProductName(product.Name);
        var fromNfe = ProductClassificationHelper.SanitizeProductName(nfeName);
        var preferred = !string.IsNullOrWhiteSpace(fromNfe) && fromNfe.Length >= Math.Min(8, fromProduct.Length)
            ? fromNfe
            : fromProduct;

        if (string.IsNullOrWhiteSpace(preferred)
            || string.Equals(preferred, product.Name, StringComparison.OrdinalIgnoreCase))
            return product;

        var extra = ProductExtra.Parse(product.ExtraJson);
        return Update(product.Id, new ProductInput
        {
            Code = product.Code,
            Barcode = product.Barcode,
            Name = preferred,
            GroupName = product.GroupName,
            Unit = string.IsNullOrWhiteSpace(product.Unit) ? "UN" : product.Unit,
            CostPrice = product.CostPrice,
            SalePrice = product.SalePrice,
            MinStock = product.MinStock,
            Stock = product.Stock,
            StockFridge = product.StockFridge,
            StockFridgeMin = product.StockFridgeMin,
            Location = product.Location,
            Extra = extra,
            Active = product.Active,
        });
    }

    /// <summary>Limpa nomes sujos de embalagem em todo o cadastro (uma vez por versão).</summary>
    public static int SanitizeAllCatalogNamesOnce(string versionKey = "product_name_sanitize_v6")
    {
        if (AppSettingsService.GetSetting(versionKey) == "1")
            return 0;

        var updated = 0;
        foreach (var product in List(null, "todos"))
        {
            var clean = ProductClassificationHelper.SanitizeProductName(product.Name);
            if (string.IsNullOrWhiteSpace(clean)
                || string.Equals(clean, product.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            EnsureCleanCatalogName(product);
            updated++;
        }

        AppSettingsService.SetSetting(versionKey, "1");
        return updated;
    }

    /// <summary>
    /// Produtos com unidade CX/FD/EB e fator de embalagem ≥ 2 passam a unit=UN (estoque em unidade de venda).
    /// Não altera o número do estoque.
    /// </summary>
    public static int NormalizePackUnitsToUnOnce(string versionKey = "product_unit_un_v1")
    {
        if (AppSettingsService.GetSetting(versionKey) == "1")
            return 0;

        // Ajuste no banco da loja (servidor). Cliente só marca como feito localmente.
        if (StoreNetworkMode.IsClient)
        {
            AppSettingsService.SetSetting(versionKey, "1");
            return 0;
        }

        var updated = 0;
        using var conn = DatabaseService.OpenConnection();
        using (var listCmd = conn.CreateCommand())
        {
            listCmd.CommandText = """
                SELECT id, unit, IFNULL(extra_json, '{}')
                FROM products
                WHERE active = 1 OR active = 0;
                """;
            using var reader = listCmd.ExecuteReader();
            var toFix = new List<int>();
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                var unit = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var extraJson = reader.IsDBNull(2) ? "{}" : reader.GetString(2);
                if (!Product.IsPackUnitLabel(unit))
                    continue;
                if (ProductExtra.Parse(extraJson).FatorEmbalagem < 2)
                    continue;
                toFix.Add(id);
            }

            reader.Close();
            foreach (var id in toFix)
            {
                using var upd = conn.CreateCommand();
                upd.CommandText = "UPDATE products SET unit = 'UN' WHERE id = $id;";
                upd.Parameters.AddWithValue("$id", id);
                upd.ExecuteNonQuery();
                updated++;
            }
        }

        AppSettingsService.SetSetting(versionKey, "1");
        return updated;
    }

    public static Product Update(int id, ProductInput input)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.UpdateProduct(id, input);
        return UpdateLocal(id, input);
    }

    public static Product UpdateLocal(int id, ProductInput input)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("atualizar produto");
        var existing = GetByIdLocal(id) ?? throw new InvalidOperationException("Produto não encontrado.");
        input.Name = ProductClassificationHelper.SanitizeProductName(input.Name);
        var data = Normalize(input);

        if (!string.IsNullOrWhiteSpace(data.Code))
        {
            if (CodeExists(data.Code, id))
                throw new InvalidOperationException("Referência já cadastrada.");
        }
        else
        {
            data.Code = existing.Code;
        }

        if (string.IsNullOrWhiteSpace(data.Name))
            throw new InvalidOperationException("Informe a descrição do produto.");

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE products SET
                code = $code,
                barcode = $barcode,
                name = $name,
                group_name = $group_name,
                unit = $unit,
                cost_price = $cost_price,
                sale_price = $sale_price,
                min_stock = $min_stock,
                stock = $stock,
                stock_fridge = $stock_fridge,
                stock_fridge_min = $stock_fridge_min,
                location = $location,
                extra_json = $extra_json,
                active = $active
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        BindProduct(cmd, data);
        cmd.ExecuteNonQuery();
        SyncCatalogFromProduct(data);
        LogProductAudit(existing, data, id);
        return GetByIdLocal(id) ?? throw new InvalidOperationException("Falha ao atualizar produto.");
    }

    private static void LogProductAudit(Product existing, ProductInput data, int id)
    {
        var changes = new Dictionary<string, object>();
        if (Math.Abs(existing.SalePrice - data.SalePrice) > 0.001)
            changes["preco_venda"] = new { de = existing.SalePrice, para = data.SalePrice };
        if (Math.Abs(existing.Stock - data.Stock) > 0.001)
            changes["estoque"] = new { de = existing.Stock, para = data.Stock };
        if (Math.Abs(existing.CostPrice - data.CostPrice) > 0.001)
            changes["preco_custo"] = new { de = existing.CostPrice, para = data.CostPrice };
        if (changes.Count == 0)
            return;

        var parts = new List<string>();
        if (changes.ContainsKey("preco_venda"))
            parts.Add($"preço R$ {existing.SalePrice:N2} → R$ {data.SalePrice:N2}");
        if (changes.ContainsKey("estoque"))
            parts.Add($"estoque {existing.Stock:G} → {data.Stock:G}");
        if (changes.ContainsKey("preco_custo"))
            parts.Add($"custo R$ {existing.CostPrice:N2} → R$ {data.CostPrice:N2}");

        AuditService.LogJson("alterar", "produto", id.ToString(),
            AuditPayloadBuilder.ProductChange(id, data.Code ?? existing.Code ?? "", data.Name, changes, "cadastro"),
            $"{data.Name}: {string.Join(" · ", parts)}");
    }

    public static void SoftDelete(int id)
    {
        if (StoreNetworkMode.IsClient)
        {
            StoreNetworkClient.SoftDeleteProduct(id);
            return;
        }
        SoftDeleteLocal(id);
    }

    public static void SoftDeleteLocal(int id)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("inativar produto");
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET active = 0 WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Produto não encontrado.");
    }

    /// <summary>
    /// Une o produto <paramref name="absorbId"/> no <paramref name="keepId"/>:
    /// soma estoque, copia barcode/referência/grupo se vazios, remapeia FKs e inativa o duplicado.
    /// Mantém preço de venda do produto principal.
    /// </summary>
    public static Product MergeProducts(int keepId, int absorbId)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.MergeProducts(keepId, absorbId);
        return MergeProductsLocal(keepId, absorbId);
    }

    public static Product MergeProductsLocal(int keepId, int absorbId)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("mesclar produtos");
        if (keepId <= 0 || absorbId <= 0)
            throw new InvalidOperationException("Selecione os dois produtos.");
        if (keepId == absorbId)
            throw new InvalidOperationException("Escolha dois produtos diferentes.");

        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        var keep = LoadProductTx(conn, tx, keepId)
            ?? throw new InvalidOperationException("Produto principal não encontrado.");
        var absorb = LoadProductTx(conn, tx, absorbId)
            ?? throw new InvalidOperationException("Produto a juntar não encontrado.");

        var newStock = keep.Stock + absorb.Stock;
        var newFridge = keep.StockFridge + absorb.StockFridge;
        var fridgeMin = Math.Max(keep.StockFridgeMin, absorb.StockFridgeMin);
        var barcode = string.IsNullOrWhiteSpace(keep.Barcode) ? absorb.Barcode : keep.Barcode;
        var code = string.IsNullOrWhiteSpace(keep.Code) ? absorb.Code : keep.Code;
        var group = string.IsNullOrWhiteSpace(keep.GroupName) ? absorb.GroupName : keep.GroupName;
        var location = string.IsNullOrWhiteSpace(keep.Location) ? absorb.Location : keep.Location;

        // Custo médio ponderado (cigarro: média por maços).
        double cost;
        var isCig = ProductClassificationHelper.UsesPackPurchasePrice(keep.Name, keep.GroupName)
                    || ProductClassificationHelper.UsesPackPurchasePrice(absorb.Name, absorb.GroupName);
        if (keep.Stock > 0.0001 && absorb.Stock > 0.0001
            && keep.CostPrice > 0.009 && absorb.CostPrice > 0.009)
        {
            if (isCig)
            {
                var cigs = ProductPriceHelper.ResolveCigarettesPerPack(
                    keep.Name,
                    ProductExtra.Parse(keep.ExtraJson).FatorEmbalagem);
                if (cigs < 2) cigs = 20;
                cost = ProductPriceHelper.WeightedAverageCost(
                    keep.Stock / cigs, keep.CostPrice,
                    absorb.Stock / cigs, absorb.CostPrice);
            }
            else
            {
                cost = ProductPriceHelper.WeightedAverageCost(
                    keep.Stock, keep.CostPrice, absorb.Stock, absorb.CostPrice);
            }
        }
        else
        {
            cost = keep.CostPrice > 0.009 ? keep.CostPrice : absorb.CostPrice;
        }

        var mergedExtra = MergeExtraJson(keep.ExtraJson, absorb.ExtraJson, keep.Barcode, absorb.Barcode);

        if (!string.IsNullOrWhiteSpace(barcode)
            && BarcodeUsedByOther(conn, tx, barcode, keepId, absorbId))
            throw new InvalidOperationException(
                $"O código de barras {barcode} já está em outro produto. Ajuste antes de unificar.");

        // Libera barcode do duplicado antes de gravar no principal (unique / conflito).
        using (var clearAbs = conn.CreateCommand())
        {
            clearAbs.Transaction = tx;
            clearAbs.CommandText = "UPDATE products SET barcode = NULL WHERE id = $id;";
            clearAbs.Parameters.AddWithValue("$id", absorbId);
            clearAbs.ExecuteNonQuery();
        }

        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = """
                UPDATE products SET
                    stock = $stock,
                    stock_fridge = $fridge,
                    stock_fridge_min = $fridge_min,
                    barcode = $barcode,
                    code = $code,
                    group_name = $group,
                    location = $loc,
                    cost_price = $cost,
                    extra_json = $extra
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$stock", newStock);
            upd.Parameters.AddWithValue("$fridge", newFridge);
            upd.Parameters.AddWithValue("$fridge_min", fridgeMin);
            upd.Parameters.AddWithValue("$barcode", (object?)barcode ?? DBNull.Value);
            upd.Parameters.AddWithValue("$code", (object?)code ?? DBNull.Value);
            upd.Parameters.AddWithValue("$group", (object?)group ?? DBNull.Value);
            upd.Parameters.AddWithValue("$loc", (object?)location ?? DBNull.Value);
            upd.Parameters.AddWithValue("$cost", cost);
            upd.Parameters.AddWithValue("$extra", mergedExtra);
            upd.Parameters.AddWithValue("$id", keepId);
            upd.ExecuteNonQuery();
        }

        RemapProductId(conn, tx, "sale_items", absorbId, keepId);
        RemapProductId(conn, tx, "purchase_items", absorbId, keepId);
        RemapProductId(conn, tx, "movements", absorbId, keepId);
        RemapProductId(conn, tx, "product_lots", absorbId, keepId);
        RemapProductId(conn, tx, "open_tab_items", absorbId, keepId);
        RemapInventoryItems(conn, tx, absorbId, keepId);
        RemapCompositionReferences(conn, tx, absorbId, keepId);

        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "UPDATE products SET active = 0 WHERE id = $id;";
            del.Parameters.AddWithValue("$id", absorbId);
            del.ExecuteNonQuery();
        }

        tx.Commit();

        AuditService.Log("unificar", "produto", keepId.ToString(),
            $"#{absorbId} {absorb.Name} → #{keepId} {keep.Name} · estoque {keep.Stock:G}+{absorb.Stock:G}={newStock:G}");

        return GetByIdLocal(keepId)!;
    }

    private static Product? LoadProductTx(SqliteConnection conn, SqliteTransaction tx, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, code, barcode, name, group_name, unit, cost_price, sale_price,
                   min_stock, stock, location, extra_json, active, created_at, IFNULL(stock_fridge, 0), IFNULL(stock_fridge_min, 0)
            FROM products WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        return ReadAll(cmd).FirstOrDefault();
    }

    private static bool BarcodeUsedByOther(
        SqliteConnection conn, SqliteTransaction tx, string barcode, int keepId, int absorbId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT 1 FROM products
            WHERE barcode = $b AND id <> $keep AND id <> $absorb AND active = 1
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$b", barcode.Trim());
        cmd.Parameters.AddWithValue("$keep", keepId);
        cmd.Parameters.AddWithValue("$absorb", absorbId);
        return cmd.ExecuteScalar() is not null;
    }

    private static void RemapProductId(
        SqliteConnection conn, SqliteTransaction tx, string table, int fromId, int toId)
    {
        if (!TableExists(conn, tx, table))
            return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"UPDATE {table} SET product_id = $to WHERE product_id = $from;";
        cmd.Parameters.AddWithValue("$to", toId);
        cmd.Parameters.AddWithValue("$from", fromId);
        cmd.ExecuteNonQuery();
    }

    private static void RemapInventoryItems(
        SqliteConnection conn, SqliteTransaction tx, int fromId, int toId)
    {
        if (!TableExists(conn, tx, "inventory_items"))
            return;

        var pairs = new List<(int SessionId, double Counted, double Theoretical)>();
        using (var q = conn.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = """
                SELECT session_id, IFNULL(counted_qty,0), IFNULL(theoretical_qty,0)
                FROM inventory_items WHERE product_id = $from;
                """;
            q.Parameters.AddWithValue("$from", fromId);
            using var r = q.ExecuteReader();
            while (r.Read())
                pairs.Add((r.GetInt32(0), r.GetDouble(1), r.GetDouble(2)));
        }

        foreach (var (sessionId, counted, theoretical) in pairs)
        {
            using var exists = conn.CreateCommand();
            exists.Transaction = tx;
            exists.CommandText = """
                SELECT id FROM inventory_items
                WHERE session_id = $s AND product_id = $to LIMIT 1;
                """;
            exists.Parameters.AddWithValue("$s", sessionId);
            exists.Parameters.AddWithValue("$to", toId);
            var keepItemId = exists.ExecuteScalar();
            if (keepItemId is not null)
            {
                using var add = conn.CreateCommand();
                add.Transaction = tx;
                add.CommandText = """
                    UPDATE inventory_items SET
                      counted_qty = IFNULL(counted_qty,0) + $c,
                      theoretical_qty = IFNULL(theoretical_qty,0) + $t
                    WHERE id = $id;
                    """;
                add.Parameters.AddWithValue("$c", counted);
                add.Parameters.AddWithValue("$t", theoretical);
                add.Parameters.AddWithValue("$id", Convert.ToInt32(keepItemId));
                add.ExecuteNonQuery();

                using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = """
                    DELETE FROM inventory_items
                    WHERE session_id = $s AND product_id = $from;
                    """;
                del.Parameters.AddWithValue("$s", sessionId);
                del.Parameters.AddWithValue("$from", fromId);
                del.ExecuteNonQuery();
            }
            else
            {
                using var rem = conn.CreateCommand();
                rem.Transaction = tx;
                rem.CommandText = """
                    UPDATE inventory_items SET product_id = $to
                    WHERE session_id = $s AND product_id = $from;
                    """;
                rem.Parameters.AddWithValue("$to", toId);
                rem.Parameters.AddWithValue("$s", sessionId);
                rem.Parameters.AddWithValue("$from", fromId);
                rem.ExecuteNonQuery();
            }
        }
    }

    private static void RemapCompositionReferences(
        SqliteConnection conn, SqliteTransaction tx, int fromId, int toId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, IFNULL(extra_json,'') FROM products
            WHERE id <> $from
              AND extra_json LIKE $like;
            """;
        cmd.Parameters.AddWithValue("$from", fromId);
        cmd.Parameters.AddWithValue("$like", "%\"product_id\":%" + fromId + "%");
        var updates = new List<(int Id, string Json)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                var json = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var extra = ProductExtra.Parse(json);
                var changed = false;
                foreach (var item in extra.ComposicaoItens)
                {
                    if (item.ProductId == fromId)
                    {
                        item.ProductId = toId;
                        changed = true;
                    }
                }
                if (changed)
                    updates.Add((id, extra.ToJson()));
            }
        }

        foreach (var (id, json) in updates)
        {
            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE products SET extra_json = $j WHERE id = $id;";
            upd.Parameters.AddWithValue("$j", json);
            upd.Parameters.AddWithValue("$id", id);
            upd.ExecuteNonQuery();
        }
    }

    private static string MergeExtraJson(
        string? keepJson, string? absorbJson, string? keepBarcode = null, string? absorbBarcode = null)
    {
        var keep = ProductExtra.Parse(keepJson);
        var absorb = ProductExtra.Parse(absorbJson);

        if (string.IsNullOrWhiteSpace(keep.Marca) && !string.IsNullOrWhiteSpace(absorb.Marca))
            keep.Marca = absorb.Marca;
        if (string.IsNullOrWhiteSpace(keep.BarcodeEmbalagem) && !string.IsNullOrWhiteSpace(absorb.BarcodeEmbalagem))
            keep.BarcodeEmbalagem = absorb.BarcodeEmbalagem;
        // EAN diferente no duplicado (ex.: NF Souza Cruz nova) → guarda como barras do maço/fardo
        // para o próximo XML achar o produto principal.
        if (string.IsNullOrWhiteSpace(keep.BarcodeEmbalagem)
            && !string.IsNullOrWhiteSpace(absorbBarcode)
            && !string.Equals(
                TextNorm.NormalizeBarcode(keepBarcode),
                TextNorm.NormalizeBarcode(absorbBarcode),
                StringComparison.Ordinal))
        {
            keep.BarcodeEmbalagem = TextNorm.DistinctPackBarcode(absorbBarcode, keepBarcode);
        }
        if (keep.FatorEmbalagem < 1.001 && absorb.FatorEmbalagem >= 1.001)
            keep.FatorEmbalagem = absorb.FatorEmbalagem;
        if (keep.FatorEmbalagem < 2 && absorb.FatorEmbalagem >= 2)
            keep.FatorEmbalagem = absorb.FatorEmbalagem;
        if (keep.QtdAtacado < 2 && absorb.QtdAtacado >= 2)
            keep.QtdAtacado = absorb.QtdAtacado;
        if (keep.PrecoAtacado <= 0.009 && absorb.PrecoAtacado > 0.009)
            keep.PrecoAtacado = absorb.PrecoAtacado;
        if (keep.PrecoCompra <= 0.009 && absorb.PrecoCompra > 0.009)
            keep.PrecoCompra = absorb.PrecoCompra;
        if (keep.PriceTableId is null or <= 0 && absorb.PriceTableId is > 0)
            keep.PriceTableId = absorb.PriceTableId;
        if (keep.VasilhameTipoId is null or <= 0 && absorb.VasilhameTipoId is > 0)
        {
            keep.VasilhameTipoId = absorb.VasilhameTipoId;
            keep.VasilhameQty = absorb.VasilhameQty;
        }
        if (keep.ComposicaoItens.Count == 0 && absorb.ComposicaoItens.Count > 0)
        {
            keep.Composicao = absorb.Composicao;
            keep.ComposicaoItens = absorb.ComposicaoItens;
        }

        return keep.ToJson();
    }

    private static bool TableExists(SqliteConnection conn, SqliteTransaction tx, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n LIMIT 1;";
        cmd.Parameters.AddWithValue("$n", table);
        return cmd.ExecuteScalar() is not null;
    }

    public static Product Duplicate(int id)
    {
        var source = GetById(id) ?? throw new InvalidOperationException("Selecione um produto.");
        var extra = ProductExtra.Parse(source.ExtraJson);
        var input = new ProductInput
        {
            Code = UniquifyCode($"{source.Code}-COPIA"),
            Barcode = null,
            Name = source.Name,
            GroupName = source.GroupName,
            Unit = source.Unit,
            CostPrice = source.CostPrice,
            SalePrice = source.SalePrice,
            MinStock = source.MinStock,
            Stock = 0,
            StockFridge = 0,
            StockFridgeMin = source.StockFridgeMin,
            Location = source.Location,
            Extra = extra,
            Active = true,
        };
        return Create(input);
    }

    private static ProductInput Normalize(ProductInput input)
    {
        var extra = input.Extra;
        var groupName = input.GroupName;
        ProductClassificationHelper.FillMissing(input.Name, ref groupName, extra);

        if (input.SalePrice > 0 && input.CostPrice >= 0)
            extra.LucroPercent = ProductPriceCalculator.MarginOnSale(input.CostPrice, input.SalePrice);

        return new ProductInput
        {
            Code = TextNorm.UpperStr(input.Code),
            Barcode = TextNorm.NormalizeBarcode(input.Barcode),
            Name = TextNorm.UpperStr(input.Name) ?? "",
            GroupName = TextNorm.UpperStr(groupName),
            Unit = string.IsNullOrWhiteSpace(input.Unit) ? "UN" : input.Unit.Trim().ToUpperInvariant()[..Math.Min(10, input.Unit.Trim().Length)],
            CostPrice = Math.Max(0, input.CostPrice),
            SalePrice = Math.Max(0, input.SalePrice),
            MinStock = Math.Max(0, input.MinStock),
            Stock = input.Stock,
            StockFridge = Math.Max(0, input.StockFridge),
            StockFridgeMin = Math.Max(0, input.StockFridgeMin),
            Location = TextNorm.UpperStr(input.Location),
            Extra = extra,
            Active = input.Active,
        };
    }

    private static string? ResolveCode(string? code, string name)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            if (CodeExists(code))
                throw new InvalidOperationException("Referência já cadastrada.");
            return code;
        }

        var generated = TextNorm.ReferenciaFromName(name);
        if (string.IsNullOrWhiteSpace(generated))
            throw new InvalidOperationException("Informe a descrição do produto.");
        return UniquifyCode(generated);
    }

    private static string UniquifyCode(string code)
    {
        var baseCode = code.Length > 40 ? code[..40] : code;
        var candidate = baseCode;
        var n = 2;
        while (CodeExists(candidate))
        {
            var suffix = $"-{n}";
            candidate = baseCode.Length + suffix.Length > 40
                ? baseCode[..(40 - suffix.Length)] + suffix
                : baseCode + suffix;
            n++;
        }
        return candidate;
    }

    private static bool CodeExists(string code, int? excludeId = null)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = excludeId is null
            ? "SELECT 1 FROM products WHERE code = $code LIMIT 1;"
            : "SELECT 1 FROM products WHERE code = $code AND id != $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$code", code);
        if (excludeId is not null)
            cmd.Parameters.AddWithValue("$id", excludeId.Value);
        return cmd.ExecuteScalar() is not null;
    }

    private static void BindProduct(SqliteCommand cmd, ProductInput data)
    {
        cmd.Parameters.AddWithValue("$code", (object?)data.Code ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$barcode", (object?)data.Barcode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", data.Name);
        cmd.Parameters.AddWithValue("$group_name", (object?)data.GroupName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$unit", data.Unit);
        cmd.Parameters.AddWithValue("$cost_price", data.CostPrice);
        cmd.Parameters.AddWithValue("$sale_price", data.SalePrice);
        cmd.Parameters.AddWithValue("$min_stock", data.MinStock);
        cmd.Parameters.AddWithValue("$stock", data.Stock);
        cmd.Parameters.AddWithValue("$stock_fridge", data.StockFridge);
        cmd.Parameters.AddWithValue("$stock_fridge_min", data.StockFridgeMin);
        cmd.Parameters.AddWithValue("$location", (object?)data.Location ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$extra_json", data.Extra.ToJson());
        cmd.Parameters.AddWithValue("$active", data.Active ? 1 : 0);
    }

    private static void SyncCatalogFromProduct(ProductInput data)
    {
        ProductCatalogService.EnsureUnit(data.Unit);
        ProductCatalogService.EnsureGroup(data.GroupName);
        ProductCatalogService.EnsureBrand(data.Extra.Marca);
    }

    private static List<Product> ReadAll(SqliteCommand cmd)
    {
        var list = new List<Product>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Product
            {
                Id = reader.GetInt32(0),
                Code = reader.IsDBNull(1) ? null : reader.GetString(1),
                Barcode = reader.IsDBNull(2) ? null : reader.GetString(2),
                Name = reader.GetString(3),
                GroupName = reader.IsDBNull(4) ? null : reader.GetString(4),
                Unit = reader.IsDBNull(5) ? "UN" : reader.GetString(5),
                CostPrice = reader.IsDBNull(6) ? 0 : reader.GetDouble(6),
                SalePrice = reader.IsDBNull(7) ? 0 : reader.GetDouble(7),
                MinStock = reader.IsDBNull(8) ? 5 : reader.GetInt32(8),
                Stock = reader.IsDBNull(9) ? 0 : reader.GetDouble(9),
                Location = reader.IsDBNull(10) ? null : reader.GetString(10),
                ExtraJson = reader.IsDBNull(11) ? "{}" : reader.GetString(11),
                Active = !reader.IsDBNull(12) && reader.GetInt32(12) != 0,
                CreatedAt = reader.IsDBNull(13) ? "" : reader.GetString(13),
                StockFridge = reader.FieldCount > 14 && !reader.IsDBNull(14) ? reader.GetDouble(14) : 0,
                StockFridgeMin = reader.FieldCount > 15 && !reader.IsDBNull(15) ? Convert.ToInt32(reader.GetValue(15)) : 0,
            });
        }
        return list;
    }
}
