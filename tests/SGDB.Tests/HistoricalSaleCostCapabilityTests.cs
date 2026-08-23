using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>ETAPA 69E-B2 — host calcula CMV; client não recalcula; campos extras backward-compat.</summary>
public class HistoricalSaleCostCapabilityTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void HostCalcula_ClientNaoUsaCustoLocal()
    {
        var app = AppSourceRoot();
        var dre = File.ReadAllText(Path.Combine(app, "Services", "DreService.cs"));
        var dash = File.ReadAllText(Path.Combine(app, "Services", "BusinessDashboardService.cs"));
        var reports = File.ReadAllText(Path.Combine(app, "Services", "ReportsService.cs"));
        var pdv = File.ReadAllText(Path.Combine(app, "Services", "PdvQueryService.cs"));
        var stock = File.ReadAllText(Path.Combine(app, "Services", "StockService.cs"));
        var mov = File.ReadAllText(Path.Combine(app, "Services", "MovimentacaoService.cs"));
        var client = File.ReadAllText(Path.Combine(app, "Services", "StoreNetworkClient.cs"));
        var host = File.ReadAllText(Path.Combine(app, "Services", "StoreNetworkHost.cs"));

        Assert.Contains("HistoricalSaleCostRules", dre);
        Assert.DoesNotContain("UnitCostForSoldLine", dre);
        Assert.Contains("HistoricalSaleCostRules", dash);
        Assert.Contains("HistoricalSaleCostRules", reports);
        Assert.Contains("HistoricalSaleCostRules", pdv);
        Assert.Contains("HistoricalSaleCostRules", stock);
        Assert.Contains("HistoricalSaleCostRules", mov);

        var dashboardSlice = Slice(dash, "public static NegocioDashboard GetDashboardLocal", "private sealed class SaleRow");
        Assert.DoesNotContain("GetProductCost", dashboardSlice);
        Assert.Contains("item.Cmv", dashboardSlice);

        var getDash = Slice(client, "public static NegocioDashboard GetDashboard", "public static StockReportResult StockReport");
        Assert.DoesNotContain("cost_price", getDash);
        Assert.DoesNotContain("CostPrice", getDash);
        Assert.Contains("api/dashboard", getDash);

        Assert.Contains("BusinessDashboardService.GetDashboardLocal", host);
        Assert.Contains("PdvQueryService.GetResumoDiaLocal", host);
        Assert.Contains("apiVersion = 2", host);
        Assert.DoesNotContain("historical_sale_cost_reports", host);
        Assert.DoesNotContain("historical_sale_cost_reports", string.Join(",", StoreNetworkHost.AdvertisedFeatures));
    }

    [Fact]
    public void HelperNaoFazBackfill()
    {
        var helper = File.ReadAllText(Path.Combine(AppSourceRoot(), "Services", "HistoricalSaleCostRules.cs"));
        Assert.DoesNotContain("UPDATE", helper, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cost_at_sale =", helper);
    }

    [Fact]
    public void CamposNovos_ClientAntigoIgnora()
    {
        var payload = new NegocioDashboard
        {
            Cmv = 10,
            CmvHistorico = 7,
            CmvEstimado = 3,
            HasEstimatedLegacyCost = true,
            CmvUsesHistoricalSnapshot = true,
            Faturamento = 20,
            LucroBruto = 10,
            CmvReliabilityNote = HistoricalSaleCostRules.EstimatedLegacyPeriodNote,
        };
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        Assert.Contains("cmvHistorico", json);
        Assert.Contains("hasEstimatedLegacyCost", json);

        var legacy = JsonSerializer.Deserialize<LegacyDashboardDto>(json, JsonOpts);
        Assert.NotNull(legacy);
        Assert.Equal(10, legacy!.Cmv);
        Assert.Equal(20, legacy.Faturamento);
    }

    [Fact]
    public void HostAntigo_NaoPresumeHistorico()
    {
        const string oldJson = """{"cmv":10,"faturamento":20,"lucroBruto":10}""";
        var dash = JsonSerializer.Deserialize<NegocioDashboard>(oldJson, JsonOpts);
        Assert.NotNull(dash);
        Assert.Equal(10, dash!.Cmv);
        Assert.False(dash.CmvUsesHistoricalSnapshot);
        Assert.False(dash.HasEstimatedLegacyCost);
        Assert.Null(dash.CmvReliabilityNote);
    }

    [Fact]
    public void DreCamposNovos_Opcionais()
    {
        const string oldJson = """{"cmv":5,"receitaLiquida":8,"lucroBruto":3}""";
        var dre = JsonSerializer.Deserialize<DreSimplificadoResult>(oldJson, JsonOpts);
        Assert.NotNull(dre);
        Assert.Equal(5, dre!.Cmv);
        Assert.False(dre.HasEstimatedLegacyCost);
        Assert.False(dre.CmvUsesHistoricalSnapshot);
    }

    private sealed class LegacyDashboardDto
    {
        public double Cmv { get; set; }
        public double Faturamento { get; set; }
        public double LucroBruto { get; set; }
    }

    private static string Slice(string src, string start, string end)
    {
        var i = src.IndexOf(start, StringComparison.Ordinal);
        Assert.True(i >= 0, start);
        var j = src.IndexOf(end, i, StringComparison.Ordinal);
        Assert.True(j > i, end);
        return src[i..j];
    }

    private static string AppSourceRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "SGDB.App"));
}
