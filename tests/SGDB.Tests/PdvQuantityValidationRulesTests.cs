using SGDB.Domain.Sales;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69G — barreiras de quantidade (EAN/GTIN, teto, decimal, foco).
/// Antes da correção, EvaluateRaw("7896588700608") era aceito como quantidade.
/// </summary>
public class PdvQuantityValidationRulesTests
{
    private const string IncidentEan13 = "7896588700608";
    private const string IncidentCode = "00178";

    [Fact]
    public void Ean13Real_NoQtyBox_ERejeitado()
    {
        var r = PdvQuantityValidationRules.EvaluateRaw(IncidentEan13, IncidentEan13, IncidentCode);
        Assert.False(r.Allowed);
        Assert.Equal(PdvQuantityRejectReason.LooksLikeGtin, r.Reason);
        Assert.Equal(PdvQuantityValidationRules.MessageGtinInQuantity, r.Message);
    }

    [Fact]
    public void Ean8_ERejeitado()
    {
        var r = PdvQuantityValidationRules.EvaluateRaw("12345670");
        Assert.False(r.Allowed);
        Assert.Equal(PdvQuantityRejectReason.LooksLikeGtin, r.Reason);
    }

    [Fact]
    public void Gtin14_ERejeitado()
    {
        var r = PdvQuantityValidationRules.EvaluateRaw("12345678901234");
        Assert.False(r.Allowed);
        Assert.Equal(PdvQuantityRejectReason.LooksLikeGtin, r.Reason);
    }

    [Fact]
    public void BarcodeIgualAoProduto_ERejeitado()
    {
        var r = PdvQuantityValidationRules.EvaluateQuantity(7896588700608d, IncidentEan13, IncidentCode);
        Assert.False(r.Allowed);
        Assert.Equal(PdvQuantityRejectReason.LooksLikeGtin, r.Reason);
    }

    [Fact]
    public void CodigoInternoCurto_NaoGeraFalsaRejeicao()
    {
        Assert.True(PdvQuantityValidationRules.EvaluateRaw("00178", IncidentEan13, "00178").Allowed);
        Assert.True(PdvQuantityValidationRules.EvaluateQuantity(12, IncidentEan13, "12").Allowed);
    }

    [Fact]
    public void DecimalUmEMeio_ContinuaValido()
    {
        Assert.True(PdvQuantityValidationRules.EvaluateRaw("1,5").Allowed);
        Assert.True(PdvQuantityValidationRules.EvaluateQuantity(1.5).Allowed);
    }

    [Fact]
    public void DecimalZeroVirgula250_ContinuaValido()
    {
        Assert.True(PdvQuantityValidationRules.EvaluateRaw("0,250").Allowed);
        Assert.True(PdvQuantityValidationRules.EvaluateQuantity(0.250).Allowed);
        Assert.True(PdvQuantityValidationRules.EvaluateRaw("12,5").Allowed);
    }

    [Fact]
    public void QtyNormal_Aceita()
    {
        Assert.True(PdvQuantityValidationRules.EvaluateQuantity(1).Allowed);
        Assert.True(PdvQuantityValidationRules.EvaluateQuantity(24).Allowed);
    }

    [Fact]
    public void QtyExatamenteNoLimite_Aceita()
    {
        Assert.True(PdvQuantityValidationRules.EvaluateQuantity(PdvQuantityValidationRules.MaxQuantityPerLine).Allowed);
    }

    [Fact]
    public void QtyAcimaDoLimite_Bloqueia()
    {
        var r = PdvQuantityValidationRules.EvaluateQuantity(PdvQuantityValidationRules.MaxQuantityPerLine + 1);
        Assert.False(r.Allowed);
        Assert.Equal(PdvQuantityRejectReason.AboveLineLimit, r.Reason);
        Assert.Equal(PdvQuantityValidationRules.MessageQuantityLimit, r.Message);
    }

    [Fact]
    public void Infinity_Bloqueia()
    {
        Assert.False(PdvQuantityValidationRules.EvaluateQuantity(double.PositiveInfinity).Allowed);
        Assert.False(PdvQuantityValidationRules.EvaluateLine(1, double.PositiveInfinity).Allowed);
    }

    [Fact]
    public void NaN_Bloqueia()
    {
        Assert.False(PdvQuantityValidationRules.EvaluateQuantity(double.NaN).Allowed);
        Assert.False(PdvQuantityValidationRules.EvaluateCartTotal(double.NaN).Allowed);
    }

    [Fact]
    public void ScanDeProduto_NaoDeixaProximaLeituraVirarQuantidade()
    {
        Assert.True(PdvScanFocusPolicy.ShouldAutoInclude(true));
        Assert.False(PdvScanFocusPolicy.SelectAllQuantityAfterScan);
        Assert.False(PdvScanFocusPolicy.ShouldAutoInclude(false));
    }

    [Fact]
    public void QtyResetPara1_AposRejeicao()
    {
        var g = PdvQtyBoxGuard.Evaluate(IncidentEan13, IncidentEan13, IncidentCode);
        Assert.False(g.Accepted);
        Assert.Equal(PdvQtyBoxGuard.ResetQtyText, g.QtyTextAfter);
    }

    [Fact]
    public void FocoRetornaSearchBox_AposEanIndevido()
    {
        var g = PdvQtyBoxGuard.Evaluate(IncidentEan13, IncidentEan13, IncidentCode);
        Assert.True(g.FocusSearchBox);
        Assert.False(string.IsNullOrWhiteSpace(g.Message));
    }

    [Fact]
    public void IncludeOrMerge_RejeitaEanComoQuantidade()
    {
        var product = new Product
        {
            Id = 188,
            Name = "CHOPP DE VINHO BODEGÃO 600ML",
            Code = IncidentCode,
            Barcode = IncidentEan13,
            SalePrice = 7.50,
        };
        var cart = new List<PdvCartLine>();
        var counter = 0;
        var ex = Assert.Throws<PdvException>(() =>
            PdvCartHelper.IncludeOrMerge(cart, product, 7896588700608d, 7.50, 1, ref counter));
        Assert.Equal(PdvQuantityValidationRules.MessageGtinInQuantity, ex.Message);
        Assert.Empty(cart);
    }
}
