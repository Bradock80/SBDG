using System.Globalization;
using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// Motor analítico 70C-B1/B1R — VMV físico, cobertura comercial, última venda.
/// Sem UI. Sem ABC. Sem schema/índice novo.
///
/// Fonte física preferencial: movements ligados a venda/troca/swap
/// (ref_type/operation/ref_id). Nunca SUM cru de saídas.
///
/// Fallback sale_items: venda cancelled=0 sem movement ref_type='sale' daquela venda.
/// Kit no fallback: não conta SKU nem explode composição atual.
///
/// Trocas — por evento (sale_exchanges.id):
///   movements com ref_id = exchange.id → fonte física dessa troca;
///   Confirm() atual NÃO grava ref_id (limitação do PDV, não alterado aqui);
///   esses movements ficam "não atribuídos" (ref_id NULL) e já entram na série física;
///   fallback tabular da troca E / produto P só se:
///     não existe movement.ref_id = E.id
///     E não existe movement sale_exchange com ref_id NULL para o produto P.
///   Assim troca moderna em A não bloqueia fallback legado em B.
///   Limitação: legado do MESMO produto que já tem movement não atribuído
///   não pode ser separado com segurança (sem inventar data+qty).
///
/// VMV operacional = MAX(0, GrossPhysicalSales − PhysicalReturns) / denominador.
/// LastValidSaleDate = última saída de venda válida, independente do líquido do dia.
///
/// Início observável: MIN de entradas físicas confiáveis e primeira venda válida.
/// products.created_at só é fallback sem evidência física.
/// </summary>
public static class InventoryIntelligenceService
{
    public const int ExpectedQueryCount = 6;

    /// <summary>
    /// Entradas que provam disponibilidade física. Saída, cancelamento, devolução,
    /// transferência e ajuste de baixa NÃO entram.
    /// </summary>
    internal static bool IsTrustedInboundOperation(string? operation)
    {
        var op = (operation ?? "").Trim().ToLowerInvariant();
        return op is "entrada_compra" or "entrada_nfe" or "entrada_manual" or "ajuste_manual";
    }

    public static InventoryIntelligenceSnapshot Load(DateTime? today = null)
    {
        var day = (today ?? DateTime.Today).Date;
        using var conn = DatabaseService.OpenConnection();
        var queryCount = 0;

        var products = LoadProducts(conn);
        queryCount++;

        var firstPurchase = LoadFirstClosedPurchaseDates(conn);
        queryCount++;

        var firstInbound = LoadFirstTrustedInboundDates(conn);
        queryCount++;

        var events = LoadPhysicalMovementEvents(conn);
        queryCount++;

        var saleFallback = LoadSaleItemFallback(conn);
        queryCount++;

        var boundExchangeIds = new HashSet<int>();
        var unattributedExchangeProducts = new HashSet<int>();
        foreach (var ev in events)
        {
            if (ev.Kind != PhysicalEventKind.Exchange)
                continue;
            if (ev.ExchangeId is int xid)
                boundExchangeIds.Add(xid);
            else
                unattributedExchangeProducts.Add(ev.ProductId);
        }

        var exchangeFallback = LoadExchangeTableFallback(conn, boundExchangeIds, unattributedExchangeProducts);
        queryCount++;

        var daily = new Dictionary<int, Dictionary<DateTime, Acc>>();
        var firstValidSale = new Dictionary<int, DateTime>();

        void AddEvent(PhysicalEvent ev)
        {
            if (ev.ProductId <= 0 || !InventoryIntelligenceEngine.IsFinite(ev.Qty))
                return;
            var date = ev.Date.Date;
            var qty = Math.Abs(ev.Qty);
            if (qty <= InventoryIntelligenceEngine.Epsilon)
                return;

            if (!daily.TryGetValue(ev.ProductId, out var byDay))
            {
                byDay = new Dictionary<DateTime, Acc>();
                daily[ev.ProductId] = byDay;
            }
            byDay.TryGetValue(date, out var acc);
            if (ev.IsReturn)
                acc.Returns += qty;
            else
                acc.Gross += qty;
            if (ev.IsValidSaleOutflow)
                acc.HasValidSale = true;
            byDay[date] = acc;

            if (ev.IsValidSaleOutflow
                && (!firstValidSale.TryGetValue(ev.ProductId, out var existing) || date < existing))
                firstValidSale[ev.ProductId] = date;
        }

        foreach (var ev in events)
            AddEvent(ev);
        foreach (var ev in saleFallback)
            AddEvent(ev);
        foreach (var ev in exchangeFallback)
            AddEvent(ev);

        var rows = new List<ProductTurnoverRow>(products.Count);
        foreach (var p in products)
        {
            DateTime? purchaseDate = firstPurchase.TryGetValue(p.Id, out var purchase) ? purchase : null;
            DateTime? inboundDate = firstInbound.TryGetValue(p.Id, out var inbound) ? inbound : null;
            DateTime? saleDate = firstValidSale.TryGetValue(p.Id, out var sale) ? sale : null;

            DateTime? trustedInbound = MinDate(purchaseDate, inboundDate);

            var life = InventoryIntelligenceEngine.ResolveLifeStart(
                day, p.CreatedAt, trustedInbound, saleDate);

            IReadOnlyList<InventoryIntelligenceEngine.DailyFlow> flows = [];
            if (daily.TryGetValue(p.Id, out var byDay))
            {
                var list = new List<InventoryIntelligenceEngine.DailyFlow>(byDay.Count);
                foreach (var kv in byDay)
                {
                    list.Add(new InventoryIntelligenceEngine.DailyFlow(
                        kv.Key, kv.Value.Gross, kv.Value.Returns, kv.Value.HasValidSale));
                }
                flows = list;
            }

            rows.Add(InventoryIntelligenceEngine.BuildRow(
                p.Id, p.Code, p.Name, p.Stock, p.StockFridge, day, life, flows));
        }

        rows.Sort((a, b) =>
        {
            var c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : a.ProductId.CompareTo(b.ProductId);
        });

        return new InventoryIntelligenceSnapshot
        {
            Today = day,
            QueryCount = queryCount,
            Rows = rows,
        };
    }

    public static ProductTurnoverRow? GetByProductId(int productId, DateTime? today = null)
    {
        foreach (var row in Load(today).Rows)
        {
            if (row.ProductId == productId)
                return row;
        }
        return null;
    }

    private enum PhysicalEventKind { Sale, Swap, Exchange }

    private readonly record struct PhysicalEvent(
        int ProductId,
        DateTime Date,
        double Qty,
        PhysicalEventKind Kind,
        bool IsReturn,
        bool IsValidSaleOutflow,
        int? ExchangeId);

    private struct Acc
    {
        public double Gross;
        public double Returns;
        public bool HasValidSale;
    }

    private readonly record struct ProductSeed(
        int Id, string Code, string Name, double Stock, double StockFridge, DateTime? CreatedAt);

    private static DateTime? MinDate(DateTime? a, DateTime? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a.Value <= b.Value ? a : b;
    }

    private static List<ProductSeed> LoadProducts(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id,
                   IFNULL(code, ''),
                   IFNULL(name, ''),
                   IFNULL(stock, 0),
                   IFNULL(stock_fridge, 0),
                   created_at
            FROM products
            WHERE IFNULL(active, 1) = 1
            ORDER BY id;
            """;
        var list = new List<ProductSeed>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ProductSeed(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                ParseCatalogDate(reader.IsDBNull(5) ? null : reader.GetString(5))));
        }
        return list;
    }

    private static Dictionary<int, DateTime> LoadFirstClosedPurchaseDates(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT pi.product_id, MIN(p.entry_date)
            FROM purchase_items pi
            INNER JOIN purchases p ON p.id = pi.purchase_id
            WHERE pi.product_id IS NOT NULL
              AND p.status = 'fechada'
              AND IFNULL(p.gerar_estoque, 1) = 1
            GROUP BY pi.product_id;
            """;
        var map = new Dictionary<int, DateTime>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(1))
                continue;
            var d = ParseCivilDate(reader.GetString(1));
            if (d is DateTime dt)
                map[reader.GetInt32(0)] = dt;
        }
        return map;
    }

    private static Dictionary<int, DateTime> LoadFirstTrustedInboundDates(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT product_id, MIN(created_at)
            FROM movements
            WHERE product_id IS NOT NULL
              AND movement_type = 'entrada'
              AND IFNULL(operation, '') IN (
                    'entrada_compra', 'entrada_nfe', 'entrada_manual', 'ajuste_manual')
            GROUP BY product_id;
            """;
        var map = new Dictionary<int, DateTime>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(1))
                continue;
            var d = ParseLocalMovementDate(reader.GetString(1));
            if (d is DateTime dt)
                map[reader.GetInt32(0)] = dt;
        }
        return map;
    }

    /// <summary>
    /// Movements de venda/swap/troca. Canceladas (sales.cancelled != 0) não entram.
    /// </summary>
    private static List<PhysicalEvent> LoadPhysicalMovementEvents(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT m.product_id,
                   m.movement_type,
                   IFNULL(m.operation, ''),
                   m.quantity,
                   m.ref_type,
                   m.ref_id,
                   m.created_at,
                   s.session_date,
                   IFNULL(s.cancelled, 0),
                   ex.created_at
            FROM movements m
            LEFT JOIN sales s
              ON m.ref_type IN ('sale', 'sale_edit') AND s.id = m.ref_id
            LEFT JOIN sale_exchanges ex
              ON m.ref_type = 'sale_exchange' AND ex.id = m.ref_id
            WHERE m.ref_type IN ('sale', 'sale_edit', 'sale_exchange');
            """;

        var list = new List<PhysicalEvent>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var productId = reader.GetInt32(0);
            var type = (reader.IsDBNull(1) ? "" : reader.GetString(1)).Trim().ToLowerInvariant();
            var operation = (reader.IsDBNull(2) ? "" : reader.GetString(2)).Trim().ToLowerInvariant();
            var qty = reader.GetDouble(3);
            var refType = reader.IsDBNull(4) ? "" : reader.GetString(4);
            int? refId = reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5));
            var createdAt = reader.IsDBNull(6) ? null : reader.GetString(6);
            var sessionDate = reader.IsDBNull(7) ? null : reader.GetString(7);
            var cancelled = reader.IsDBNull(8) ? 0 : Convert.ToInt32(reader.GetValue(8));
            var exchangeCreated = reader.IsDBNull(9) ? null : reader.GetString(9);

            if (!InventoryIntelligenceEngine.IsFinite(qty) || Math.Abs(qty) <= InventoryIntelligenceEngine.Epsilon)
                continue;

            if (refType is "sale" or "sale_edit")
            {
                if (cancelled != 0 || sessionDate is null)
                    continue;
                var date = ParseCivilDate(sessionDate);
                if (date is null)
                    continue;

                if (refType == "sale")
                {
                    if (type != "saida" || operation != "venda")
                        continue;
                    list.Add(new PhysicalEvent(
                        productId, date.Value, qty, PhysicalEventKind.Sale,
                        IsReturn: false, IsValidSaleOutflow: true, ExchangeId: null));
                }
                else
                {
                    if (type == "saida")
                    {
                        list.Add(new PhysicalEvent(
                            productId, date.Value, qty, PhysicalEventKind.Swap,
                            IsReturn: false, IsValidSaleOutflow: true, ExchangeId: null));
                    }
                    else if (type == "entrada")
                    {
                        list.Add(new PhysicalEvent(
                            productId, date.Value, qty, PhysicalEventKind.Swap,
                            IsReturn: true, IsValidSaleOutflow: false, ExchangeId: null));
                    }
                }
            }
            else if (refType == "sale_exchange")
            {
                bool isReturn;
                bool isValidSale;
                if (type == "saida" && operation == "venda")
                {
                    isReturn = false;
                    isValidSale = true;
                }
                else if (type == "entrada" && operation == "devolucao_troca")
                {
                    isReturn = true;
                    isValidSale = false;
                }
                else
                    continue;

                var date = ParseUtcToCivil(exchangeCreated) ?? ParseLocalMovementDate(createdAt);
                if (date is null)
                    continue;
                list.Add(new PhysicalEvent(
                    productId, date.Value, qty, PhysicalEventKind.Exchange,
                    isReturn, isValidSale, refId));
            }
        }
        return list;
    }

    private static List<PhysicalEvent> LoadSaleItemFallback(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT si.product_id,
                   s.session_date,
                   si.quantity,
                   IFNULL(si.stock_qty, 0),
                   IFNULL(p.extra_json, '')
            FROM sale_items si
            INNER JOIN sales s ON s.id = si.sale_id
            INNER JOIN products p ON p.id = si.product_id
            WHERE IFNULL(s.cancelled, 0) = 0
              AND NOT EXISTS (
                    SELECT 1
                    FROM movements m
                    WHERE m.ref_type = 'sale' AND m.ref_id = s.id
              );
            """;
        var list = new List<PhysicalEvent>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (IsCompositionProduct(reader.IsDBNull(4) ? "" : reader.GetString(4)))
                continue;
            var date = ParseCivilDate(reader.IsDBNull(1) ? null : reader.GetString(1));
            if (date is null)
                continue;
            var qty = PhysicalQty(reader.GetDouble(2), reader.GetDouble(3));
            if (Math.Abs(qty) <= InventoryIntelligenceEngine.Epsilon)
                continue;
            list.Add(new PhysicalEvent(
                reader.GetInt32(0), date.Value, qty, PhysicalEventKind.Sale,
                IsReturn: false, IsValidSaleOutflow: true, ExchangeId: null));
        }
        return list;
    }

    /// <summary>
    /// Fallback tabular por evento. Não é global.
    /// Chave segura: sale_exchanges.id = movements.ref_id quando gravado.
    /// Confirm() atual deixa ref_id NULL — esses movements já entram na série física;
    /// o fallback do mesmo produto é omitido para não duplicar.
    /// </summary>
    private static List<PhysicalEvent> LoadExchangeTableFallback(
        SqliteConnection conn,
        HashSet<int> boundExchangeIds,
        HashSet<int> unattributedExchangeProducts)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT e.id, e.created_at, r.product_id, r.qty, IFNULL(p.extra_json, ''), -1
            FROM sale_exchanges e
            INNER JOIN sale_exchange_return_items r ON r.exchange_id = e.id
            INNER JOIN products p ON p.id = r.product_id
            UNION ALL
            SELECT e.id, e.created_at, n.product_id, n.qty, IFNULL(p.extra_json, ''), 1
            FROM sale_exchanges e
            INNER JOIN sale_exchange_new_items n ON n.exchange_id = e.id
            INNER JOIN products p ON p.id = n.product_id;
            """;
        var list = new List<PhysicalEvent>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var exchangeId = reader.GetInt32(0);
            if (boundExchangeIds.Contains(exchangeId))
                continue;

            var productId = reader.GetInt32(2);
            if (unattributedExchangeProducts.Contains(productId))
                continue;

            if (IsCompositionProduct(reader.IsDBNull(4) ? "" : reader.GetString(4)))
                continue;
            var date = ParseUtcToCivil(reader.IsDBNull(1) ? null : reader.GetString(1));
            if (date is null)
                continue;
            var qty = reader.GetDouble(3);
            var sign = Convert.ToInt32(reader.GetValue(5));
            var isReturn = sign < 0;
            if (Math.Abs(qty) <= InventoryIntelligenceEngine.Epsilon)
                continue;
            list.Add(new PhysicalEvent(
                productId, date.Value, qty, PhysicalEventKind.Exchange,
                IsReturn: isReturn,
                IsValidSaleOutflow: !isReturn,
                ExchangeId: exchangeId));
        }
        return list;
    }

    /// <summary>
    /// Semântica já usada no SGDB: stock_qty é a quantidade física normalizada
    /// quando preenchida; caso contrário quantity. Sem fator inventado.
    /// </summary>
    internal static double PhysicalQty(double quantity, double stockQty) =>
        stockQty > InventoryIntelligenceEngine.Epsilon ? stockQty : quantity;

    private static bool IsCompositionProduct(string extraJson)
    {
        var extra = ProductExtra.Parse(extraJson);
        return extra.Composicao;
    }

    private static DateTime? ParseCatalogDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
            return null;
        return DateBrHelper.ParseUtcToBrazil(iso).Date;
    }

    private static DateTime? ParseCivilDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var s = text.Trim();
        if (s.Length >= 10
            && DateTime.TryParseExact(
                s[..10],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var iso))
            return iso.Date;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.Date;
        return null;
    }

    private static DateTime? ParseLocalMovementDate(string? text) => ParseCivilDate(text);

    private static DateTime? ParseUtcToCivil(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
            return null;
        return DateBrHelper.ParseUtcToBrazil(iso).Date;
    }
}
