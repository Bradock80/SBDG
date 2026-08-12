using System.Globalization;
using System.Text.Json;
using SGDB.Domain.Finance;
using SGDB.Models;

namespace SGDB.Utils;

public static class PurchaseFinanceHelper
{
    public static readonly string[] TiposCobranca = ["Boleto", "Pix", "Dinheiro", "Cheque", "Transferencia"];

    public static string NormalizeTipoCobranca(string? tipo)
    {
        var t = (tipo ?? "").Trim().ToLowerInvariant();
        return t switch
        {
            "boleto" or "duplicata" => "Boleto",
            "pix" => "Pix",
            "dinheiro" => "Dinheiro",
            "cheque" => "Cheque",
            "transferencia" or "transferência" => "Transferencia",
            _ => TiposCobranca.FirstOrDefault(x => x.Equals(tipo, StringComparison.OrdinalIgnoreCase)) ?? "Boleto",
        };
    }

    public static string AppendFinanceiroToNotes(string? notes, PurchaseFinanceiroMeta? meta)
    {
        if (meta is null || meta.Parcelas.Count == 0)
            return notes?.Trim() ?? "";

        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["financeiro"] = meta,
        });

        return string.IsNullOrWhiteSpace(notes) ? json : $"{notes.Trim()}\n{json}";
    }

    public static PurchaseFinanceiroMeta? ExtractFinanceiroFromNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;

        foreach (var line in notes.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('{'))
                continue;
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.TryGetProperty("financeiro", out var fin))
                    return JsonSerializer.Deserialize<PurchaseFinanceiroMeta>(fin.GetRawText());
            }
            catch
            {
                // ignore invalid json line
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(notes.Trim());
            if (doc.RootElement.TryGetProperty("financeiro", out var fin))
                return JsonSerializer.Deserialize<PurchaseFinanceiroMeta>(fin.GetRawText());
        }
        catch
        {
            // ignore
        }

        return null;
    }

    /// <summary>
    /// Facade: valida/parseia datas BR, obtém data-base atual e delega ao Domain.
    /// </summary>
    public static List<PurchaseParcelaDraft> GenerateParcelas(
        double total,
        double entrada,
        int qtdParcelas,
        string primeiroVencimentoBr,
        int intervalo,
        bool intervaloEmMeses)
    {
        if (string.IsNullOrEmpty(DateBrHelper.ToIso(primeiroVencimentoBr)))
            throw new InvalidOperationException("Informe o 1º vencimento (DD/MM/AAAA).");

        if (!DateBrHelper.TryParseBr(primeiroVencimentoBr, out var firstDue))
            throw new InvalidOperationException("Informe o 1º vencimento (DD/MM/AAAA).");

        var items = InstallmentCalculator.Generate(
            total,
            entrada,
            qtdParcelas,
            firstDue,
            intervalo,
            intervaloEmMeses,
            DateBrHelper.TodayBrDate());

        return items.Select(ToDraft).ToList();
    }

    private static PurchaseParcelaDraft ToDraft(InstallmentPlanItem item) =>
        new()
        {
            Vencimento = item.DueDate.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR")),
            Tipo = item.ChargeType,
            Valor = item.Amount,
        };
}
