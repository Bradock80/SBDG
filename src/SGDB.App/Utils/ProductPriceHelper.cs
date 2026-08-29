using System.Globalization;
using SGDB.Domain.Products;
using SGDB.Models;

namespace SGDB.Utils;

public static class ProductPriceHelper
{
    /// <summary>Cultura da UI brasileira (dinheiro/quantidade). Independente do Windows/CI.</summary>
    public static CultureInfo Br { get; } = CultureInfo.GetCultureInfo("pt-BR");

    public static string FormatBr(double value) =>
        value.ToString("N2", Br);

    /// <summary>Dois decimais pt-BR sem agrupamento (40,00). Textos de caixa/cupom.</summary>
    public static string FormatFixed2(double value) =>
        value.ToString("F2", Br);

    /// <summary>Valor monetário com prefixo R$ (ex.: R$ 1.234,56).</summary>
    public static string MoneyBr(double value) =>
        $"R$ {FormatBr(value)}";

    public static string MoneyBrOrDash(double? value) =>
        value is double v ? MoneyBr(v) : "—";

    /// <summary>
    /// Custo médio ponderado na entrada de estoque:
    /// (estoque_antes × custo_antes + qtd_entrada × custo_entrada) / (estoque_antes + qtd_entrada).
    /// Estoque zerado/negativo → usa o custo da entrada (pode ser 0 = brinde).
    /// </summary>
    public static double WeightedAverageCost(
        double stockBefore, double costBefore, double qtyIn, double costIn) =>
        ProductPriceCalculator.WeightedAverageCost(stockBefore, costBefore, qtyIn, costIn);

    /// <summary>
    /// Remove uma entrada do custo médio (estorno de compra/entrada):
    /// (estoque×custo − qtd×custoEntrada) / (estoque−qtd).
    /// Se sobrar estoque ≤ 0, zera o custo.
    /// </summary>
    public static double RemoveFromWeightedAverage(
        double stockNow, double costNow, double qtyOut, double costOut) =>
        ProductPriceCalculator.RemoveFromWeightedAverage(stockNow, costNow, qtyOut, costOut);

    /// <summary>
    /// Interpreta valor digitado na UI brasileira.
    /// Não usa CurrentCulture: em en-US, NumberStyles.Any trata vírgula como milhar
    /// e "5,50" vira 550. Separador decimal = o último ',' ou '.'.
    /// </summary>
    public static double ParseBr(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        text = text.Trim()
            .Replace("R$", "", StringComparison.OrdinalIgnoreCase)
            .Replace("\u00A0", "")
            .Replace(" ", "");

        var lastComma = text.LastIndexOf(',');
        var lastDot = text.LastIndexOf('.');
        string normalized;
        if (lastComma >= 0 && lastDot >= 0)
        {
            normalized = lastComma > lastDot
                ? text.Replace(".", "").Replace(',', '.')
                : text.Replace(",", "");
        }
        else if (lastComma >= 0)
        {
            normalized = text.Replace(',', '.');
        }
        else
        {
            normalized = text;
        }

        return double.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    public static double RoundPrice(double value) =>
        ProductPriceCalculator.RoundPrice(value);

    /// <summary>
    /// Fator fardo/cartela → unidade de venda (maço/lata).
    /// Cigarro: usa fator_embalagem ou qtd_atacado; se &gt; 30 (pacote 200), resolve maços.
    /// </summary>
    public static double ResolveSalePackFactor(
        ProductExtra? extra, string? name = null, string? group = null)
    {
        var factor = 1.0;
        if (extra is not null)
        {
            if (extra.FatorEmbalagem >= 2)
                factor = extra.FatorEmbalagem;
            else if (extra.QtdAtacado >= 2)
                factor = extra.QtdAtacado;
        }

        var isCig = ProductClassificationHelper.UsesPackPurchasePrice(name, group);
        if (factor < 2 && isCig)
            factor = ResolveCigarettesPerPack(name, 20);
        else if (isCig && factor > 30)
            // Cartela BOX 200s: custo costuma ser da cartela → 10 maços (não 200 cigarros).
            factor = Math.Max(2, Math.Round(factor / ResolveCigarettesPerPack(name, factor), 0));

        return factor >= 2 ? factor : 1;
    }

    /// <summary>
    /// Custo da unidade vendida no PDV. Se o custo cadastrado for de fardo/cartela/maço
    /// (muito acima do preço unitário da venda), divide pelo fator.
    /// Ex.: cartela R$ 142,60 ÷ 20 maços = R$ 7,13; venda maço R$ 8,50.
    /// Cigarro avulso: custo maço R$ 24 ÷ 20 = R$ 1,20 quando soldUnitPrice ≈ preço avulso.
    /// </summary>
    public static double UnitCostForSoldLine(
        double catalogCost,
        double soldUnitPrice,
        ProductExtra? extra = null,
        string? name = null,
        string? group = null)
    {
        if (catalogCost <= 0.009)
            return 0;

        var fator = ResolveSalePackFactor(extra, name, group);
        if (fator < 2 || soldUnitPrice <= 0.009)
            return RoundPrice(catalogCost);

        // Pode ter cartela (ou cartela×cigarro) gravada no lugar do maço — divide até
        // o custo ficar compatível com o preço de venda da unidade (inclui avulso).
        var cost = catalogCost;
        for (var i = 0; i < 3 && cost > soldUnitPrice * 1.5; i++)
            cost = RoundPrice(cost / fator);

        return RoundPrice(cost);
    }

    public static double MarginOnSale(double cost, double sale) =>
        ProductPriceCalculator.MarginOnSale(cost, sale);

    public static double SaleFromCostAndMargin(double cost, double marginPercent) =>
        ProductPriceCalculator.SaleFromCostAndMargin(cost, marginPercent);

    public static double CostFromPurchaseAndPercent(double purchase, double costsPercent) =>
        ProductPriceCalculator.CostFromPurchaseAndPercent(purchase, costsPercent);

    /// <summary>Custo do maço/fardo a partir do custo unitário da NF.</summary>
    public static double PackCostFromUnit(double unitCost, double packFactor) =>
        ProductPriceCalculator.PackCostFromUnit(unitCost, packFactor);

    /// <summary>
    /// Cigarro: Preço Compra do maço = total ÷ qtd de maços.
    /// Ex.: 4 pacotes = 40 maços, R$ 533,60 ÷ 40 = R$ 13,34.
    /// </summary>
    public static double CigarettePackCostFromTotal(
        double lineTotal, double cigaretteQty, double cigsPerPack = 20)
    {
        if (lineTotal <= 0 || cigaretteQty <= 0)
            return 0;
        var perPack = ResolveCigarettesPerPack(null, cigsPerPack);
        var macos = cigaretteQty / perPack;
        if (macos <= 0)
            return 0;
        return RoundPrice(lineTotal / macos);
    }

    /// <summary>
    /// Cigarros por maço (HW25=25, HW20/BOX=20). Fator 200 (pacote) vira 20.
    /// </summary>
    public static double ResolveCigarettesPerPack(string? name, double storedFactor)
    {
        var n = (name ?? "").ToUpperInvariant();
        var hw = System.Text.RegularExpressions.Regex.Match(n, @"\bHW\s*(\d{2})\b");
        if (hw.Success
            && int.TryParse(hw.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hwQty)
            && hwQty is >= 10 and <= 30)
            return hwQty;

        if (storedFactor is >= 10 and <= 30)
            return storedFactor;

        // Pacote/cartela (ex.: BOX 200s = 10 maços × 20)
        if (storedFactor > 30)
            return 20;

        return 20;
    }

    /// <summary>
    /// Preço Compra/Custo no cadastro: cigarro = valor do maço; demais = unitário.
    /// Aceita grade em maços (Souza Cruz) ou em cigarros (custo unitário baixo).
    /// </summary>
    public static double ResolveCatalogCost(
        double unitCost,
        double packFactor,
        string? name,
        string? group,
        double? lineTotal = null,
        double? quantity = null)
    {
        if (!ProductClassificationHelper.UsesPackPurchasePrice(name, group))
            return Math.Round(unitCost, 4);

        var cigsPerPack = ResolveCigarettesPerPack(name, packFactor);
        // Qtd já em cigarros (ex.: 0,4000 MIL → 400 cig) mesmo quando o unitário físico ≥ 4.
        var looksPhysicalCigarettes = cigsPerPack >= 2
            && quantity is > 0
            && quantity.Value + 0.0001 >= cigsPerPack * 2;

        // Já veio custo de maço (NF em maços / edição na grade).
        if (unitCost >= 4.0 && !looksPhysicalCigarettes)
            return RoundPrice(unitCost);

        if (lineTotal is > 0 && quantity is > 0)
        {
            var perQty = lineTotal.Value / quantity.Value;
            // Quantidade em maços → custo do maço = total ÷ qtd
            if (perQty >= 4.0 && !looksPhysicalCigarettes)
                return RoundPrice(perQty);
            // Quantidade em cigarros → total ÷ (cig ÷ fator)
            return CigarettePackCostFromTotal(lineTotal.Value, quantity.Value, cigsPerPack);
        }

        if (unitCost > 0 && cigsPerPack >= 2)
            return PackCostFromUnit(unitCost, cigsPerPack);

        return Math.Round(unitCost, 4);
    }

    /// <summary>
    /// Preço Venda no cadastro a partir da grade da NF.
    /// Cigarro: valor do maço (se vier &lt; 5, trata como avulso e multiplica pelo fator).
    /// </summary>
    public static double ResolveCatalogSale(
        double saleFromGrid, double unitCost, double packFactor, string? name, string? group,
        double? marginPercent = null)
    {
        var usePack = ProductClassificationHelper.UsesPackPurchasePrice(name, group);
        var cigsPerPack = ResolveCigarettesPerPack(name, packFactor);

        if (saleFromGrid > 0)
        {
            if (usePack && saleFromGrid < 5)
                return RoundPrice(saleFromGrid * cigsPerPack);
            return RoundPrice(saleFromGrid);
        }

        if (marginPercent is > 0 && unitCost > 0)
        {
            var cost = ResolveCatalogCost(unitCost, cigsPerPack, name, group);
            return SaleFromCostAndMargin(cost, marginPercent.Value);
        }

        return 0;
    }
}
