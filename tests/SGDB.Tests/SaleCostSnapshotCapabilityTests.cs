using System.IO;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69E-B1 — host calcula cost_at_sale; client não envia custo.
/// </summary>
public class SaleCostSnapshotCapabilityTests
{
    [Fact]
    public void HostCalcula_ClientNaoEnviaCusto()
    {
        var app = AppSourceRoot();
        var pdv = File.ReadAllText(Path.Combine(app, "Services", "PdvService.cs"));
        var client = File.ReadAllText(Path.Combine(app, "Services", "StoreNetworkClient.cs"));
        var host = File.ReadAllText(Path.Combine(app, "Services", "StoreNetworkHost.cs"));

        var finalize = Slice(pdv, "internal static PdvFinalizeResult FinalizeSaleCore", "public static void CancelSale");
        Assert.Contains("SaleCostSnapshotRules.ComputeForProduct", finalize);
        Assert.Contains("cost_at_sale", finalize);
        Assert.DoesNotContain("request.CostAtSale", finalize);

        Assert.DoesNotContain("cost_at_sale", client);
        Assert.DoesNotContain("CostAtSale", client);
        Assert.DoesNotContain("api/pdv/finalize", client);
        Assert.DoesNotContain("api/pdv/finalize", host);

        Assert.Contains("TlsRequiredMessage", client);
        Assert.Contains("https://", client);
        Assert.Contains("SessionToken", client);
        Assert.Contains("apiVersion = 2", host);
        Assert.DoesNotContain("cost_at_sale_atomic", host);
        Assert.DoesNotContain("cost_at_sale_atomic", string.Join(",", StoreNetworkHost.AdvertisedFeatures));
    }

    [Fact]
    public void SwapTambemUsaHelper()
    {
        var pdv = File.ReadAllText(Path.Combine(AppSourceRoot(), "Services", "PdvService.cs"));
        var swap = Slice(pdv, "public static PdvSwapItemResult SwapSaleItem", "private static SwapPlan PlanSwapSaleItem");
        Assert.Contains("SaleCostSnapshotRules.ComputeForProduct", swap);
        Assert.Contains("cost_at_sale = $cost", swap);
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
