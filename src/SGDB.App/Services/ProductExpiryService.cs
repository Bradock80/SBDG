using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public enum ProductExpiryStatusKind
{
    Uninformed,
    Expired,
    Today,
    Within7,
    Within15,
    Within30,
    Within60,
    Within90,
    Ok,
}

public readonly record struct ProductExpiryStatus(
    ProductExpiryStatusKind Kind,
    int? Days,
    string Label)
{
    public static ProductExpiryStatus Uninformed { get; } =
        new(ProductExpiryStatusKind.Uninformed, null, "SEM VALIDADE");
}

/// <summary>
/// Validade operacional: fonte de verdade em product_lots.
/// extra_json.data_validade é legado e não entra nesta regra.
/// </summary>
public static class ProductExpiryService
{
    public const string UninformedDisplay = "Não informada";
    public const string LotsReadFeature = "product_lots_read";
    public const string HostNeedsUpgradeForLotsMessage =
        "O PC da loja precisa ser atualizado para consultar os lotes deste produto.";

    /// <summary>
    /// Próxima validade = MIN(expiry_date) dos lotes do produto com quantity &gt; 0
    /// e expiry_date preenchida. Não grava em products. Não usa extra_json.
    /// </summary>
    public static DateTime? GetNextExpiry(int productId)
    {
        if (productId <= 0)
            return null;

        if (StoreNetworkMode.IsClient)
            return ProductService.GetById(productId)?.NextExpiry;

        using var conn = DatabaseService.OpenConnection();
        return GetNextExpiry(conn, tx: null, productId);
    }

    /// <summary>MIN(expiry) dos lotes ativos já carregados — mesma regra do SQL.</summary>
    public static DateTime? NextFromLots(IEnumerable<ProductLot> lots)
    {
        DateTime? min = null;
        foreach (var lot in lots)
        {
            if (lot.Quantity <= 0.0001)
                continue;
            if (lot.ExpiryDate is not DateTime d)
                continue;
            var date = d.Date;
            if (min is null || date < min)
                min = date;
        }
        return min;
    }

    /// <summary>Dias até o vencimento na data local operacional (sem hora).</summary>
    public static int? DaysRemaining(DateTime? expiry, DateTime? today = null)
    {
        if (expiry is not DateTime raw)
            return null;
        var day = DateOnly.FromDateTime(raw);
        var now = DateOnly.FromDateTime((today ?? DateTime.Today).Date);
        return day.DayNumber - now.DayNumber;
    }

    public static ProductExpiryStatus Classify(DateTime? expiry, DateTime? today = null)
    {
        var days = DaysRemaining(expiry, today);
        if (days is not int d)
            return ProductExpiryStatus.Uninformed;

        var kind = d switch
        {
            < 0 => ProductExpiryStatusKind.Expired,
            0 => ProductExpiryStatusKind.Today,
            <= 7 => ProductExpiryStatusKind.Within7,
            <= 15 => ProductExpiryStatusKind.Within15,
            <= 30 => ProductExpiryStatusKind.Within30,
            <= 60 => ProductExpiryStatusKind.Within60,
            <= 90 => ProductExpiryStatusKind.Within90,
            _ => ProductExpiryStatusKind.Ok,
        };

        var label = kind switch
        {
            ProductExpiryStatusKind.Expired => "VENCIDO",
            ProductExpiryStatusKind.Today => "VENCE HOJE",
            ProductExpiryStatusKind.Within7 => "ATÉ 7 DIAS",
            ProductExpiryStatusKind.Within15 => "ATÉ 15 DIAS",
            ProductExpiryStatusKind.Within30 => "ATÉ 30 DIAS",
            ProductExpiryStatusKind.Within60 => "ATÉ 60 DIAS",
            ProductExpiryStatusKind.Within90 => "ATÉ 90 DIAS",
            _ => "OK",
        };
        return new ProductExpiryStatus(kind, d, label);
    }

    public static string FormatDays(int? days) =>
        days is int d ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) : "—";

    public static bool CanOpenLotsWindow(int? productId) => productId is > 0;

    public static DateTime? GetNextExpiry(SqliteConnection conn, SqliteTransaction? tx, int productId)
    {
        if (productId <= 0)
            return null;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT MIN(expiry_date)
            FROM product_lots
            WHERE product_id = $pid
              AND quantity > 0.0001
              AND expiry_date IS NOT NULL
              AND TRIM(expiry_date) <> '';
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        var raw = cmd.ExecuteScalar();
        if (raw is null or DBNull)
            return null;
        var text = Convert.ToString(raw);
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return DateTime.TryParse(text, out var dt) ? dt.Date : null;
    }

    public static string FormatDisplay(DateTime? expiry) =>
        expiry is DateTime d ? d.ToString("dd/MM/yyyy") : UninformedDisplay;

    /// <summary>
    /// extra_json.controle_validade explícito; senão inferência por categoria/nome.
    /// </summary>
    public static bool RequiresExpiryControl(Product? product, string? fallbackName = null)
    {
        if (product is not null)
        {
            var extra = ProductExtra.Parse(product.ExtraJson);
            if (extra.ControleValidade is bool explicitFlag)
                return explicitFlag;
            return ProductClassificationHelper.SuggestsExpiryControl(product.Name, product.GroupName);
        }

        return ProductClassificationHelper.SuggestsExpiryControl(fallbackName);
    }

    public static bool PurchaseShouldPromptExpiry(IEnumerable<NfeImportItem> items) =>
        items.Any(i => RequiresExpiryControl(
            i.MatchedProductId is int id ? ProductService.GetById(id) : null,
            i.Name));
}
