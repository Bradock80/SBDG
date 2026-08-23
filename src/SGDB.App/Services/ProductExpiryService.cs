using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// Validade operacional: fonte de verdade em product_lots.
/// extra_json.data_validade é legado e não entra nesta regra.
/// </summary>
public static class ProductExpiryService
{
    public const string UninformedDisplay = "Não informada";

    /// <summary>
    /// Próxima validade = MIN(expiry_date) dos lotes do produto com quantity &gt; 0
    /// e expiry_date preenchida. Não grava em products. Não usa extra_json.
    /// </summary>
    public static DateTime? GetNextExpiry(int productId)
    {
        if (productId <= 0)
            return null;

        using var conn = DatabaseService.OpenConnection();
        return GetNextExpiry(conn, tx: null, productId);
    }

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
