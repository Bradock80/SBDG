using System.Text.Json;
using System.Text.Json.Serialization;

namespace SGDB.Services;

public sealed class AuditPayloadDocument
{
    public int V { get; set; } = 1;
    public string Summary { get; set; } = "";
    public JsonElement Payload { get; set; }
}

public static class AuditPayloadBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public static string Serialize(string summary, object payload)
    {
        var wrapper = new Dictionary<string, object?>
        {
            ["v"] = 1,
            ["summary"] = summary.Trim(),
            ["payload"] = payload,
        };
        return JsonSerializer.Serialize(wrapper, JsonOpts);
    }

    public static bool TryParse(string? details, out AuditPayloadDocument doc)
    {
        doc = new AuditPayloadDocument();
        if (string.IsNullOrWhiteSpace(details) || !details.TrimStart().StartsWith('{'))
            return false;
        try
        {
            using var json = JsonDocument.Parse(details);
            var root = json.RootElement;
            if (!root.TryGetProperty("v", out _))
                return false;
            doc.V = root.TryGetProperty("v", out var v) ? v.GetInt32() : 1;
            doc.Summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
            doc.Payload = root.TryGetProperty("payload", out var p)
                ? p.Clone()
                : default;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string? GetSummary(string? details)
    {
        if (TryParse(details, out var doc) && !string.IsNullOrWhiteSpace(doc.Summary))
            return doc.Summary;
        return null;
    }

    public static string Money(double value) => $"R$ {value:N2}";

    public static object CashOpen(double openingAmount, int sessionId, bool reopening, string? notes, DateTime sessionDate) =>
        new
        {
            op = reopening ? "reabrir_cx" : "abrir_cx",
            session_id = sessionId,
            session_date = sessionDate.ToString("yyyy-MM-dd"),
            opening_amount = openingAmount,
            reopening,
            notes,
        };

    public static object CashClose(
        int sessionId, double expected, double counted, double difference, string? notes, DateTime sessionDate,
        int? operatorId = null, string? operatorName = null) =>
        new
        {
            op = "fechar_cx",
            session_id = sessionId,
            session_date = sessionDate.ToString("yyyy-MM-dd"),
            expected,
            counted,
            difference,
            notes,
            operator_id = operatorId,
            user_id = operatorId,
            operator_name = operatorName,
        };

    public static object CashSangria(double amount, string reason, DateTime sessionDate) =>
        new { op = "sangria", amount, reason, session_date = sessionDate.ToString("yyyy-MM-dd") };

    public static object CashSuprimento(double amount, string? notes, DateTime sessionDate) =>
        new { op = "suprimento", amount, notes, session_date = sessionDate.ToString("yyyy-MM-dd") };

    public static object SaleCancel(int saleId, double total, IEnumerable<object> items, string? reason = null) =>
        new { op = "cancel_venda", sale_id = saleId, total, items, reason };

    public static object SaleExchange(
        int saleId, int exchangeId, double returnTotal, double newTotal, double difference,
        string? paymentType, int? operatorId, string? operatorName, string? notes,
        object? newItems = null) =>
        new
        {
            op = "troca_venda",
            sale_id = saleId,
            exchange_id = exchangeId,
            return_total = returnTotal,
            new_total = newTotal,
            difference,
            payment_type = paymentType,
            operator_id = operatorId,
            user_id = operatorId,
            operator_name = operatorName,
            notes,
            new_items = newItems,
        };


    public static object PdvRemoveItem(object line, double cartTotalAfter) =>
        new { op = "cancel_item", line, cart_total_after = cartTotalAfter };

    public static object PdvDiscount(int saleId, double subtotal, double discount, double discountPct, double totalAfter, string paymentType) =>
        new { op = "desconto", sale_id = saleId, subtotal, discount, discount_pct = discountPct, total_after = totalAfter, payment_type = paymentType };

    public static object ProductChange(int productId, string code, string name, Dictionary<string, object> changes, string source) =>
        new { op = "alterar_produto", product_id = productId, code, name, changes, source };

    public static object PurchaseEntry(int purchaseId, int supplierId, string? supplierName, string? number, string? nfeKey, double total, int itemsCount, bool gerarEstoque, string source) =>
        new { op = "entrada_compra", purchase_id = purchaseId, supplier_id = supplierId, supplier_name = supplierName, number, nfe_key = nfeKey, total, items_count = itemsCount, gerar_estoque = gerarEstoque, source };

    public static object PersonChange(int personId, string name, bool isNew, Dictionary<string, object>? changes = null) =>
        new { op = isNew ? "criar_pessoa" : "alterar_pessoa", person_id = personId, name, changes };
}
