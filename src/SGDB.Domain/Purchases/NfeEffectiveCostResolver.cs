using SGDB.Domain.Common;

namespace SGDB.Domain.Purchases;

/// <summary>
/// Motor único de custo efetivo da linha da NF-e.
/// Estratégias estruturais primeiro; CNPJ só como contexto na explicação.
/// ST destacado (vICMSST/vFCPST) entra no landed; ST retido não é somado de novo.
/// </summary>
public static class NfeEffectiveCostResolver
{
    public static NfeEffectiveCostDecision Resolve(NfeEffectiveCostInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.IndTot == 0)
            return NonPayable(
                input,
                NfeEffectiveCostStatus.Revisar,
                NfeEffectiveCostSources.IndTotZero,
                0.4,
                "indTot=0: item fora do total da NF. Não entra como custo pago.");

        var cfopKind = NfeCfopCostClassifier.Classify(input.Cfop);
        if (cfopKind == NfeCfopCostKind.Bonificacao)
            return NonPayable(
                input,
                NfeEffectiveCostStatus.Bonificacao,
                NfeEffectiveCostSources.Bonificacao,
                0.9,
                $"CFOP {Digits(input.Cfop)} classificado como bonificação. Custo pago = 0.");
        if (cfopKind == NfeCfopCostKind.Remessa)
            return NonPayable(
                input,
                NfeEffectiveCostStatus.Remessa,
                NfeEffectiveCostSources.Remessa,
                0.85,
                $"CFOP {Digits(input.Cfop)} classificado como remessa/amostra. Custo pago = 0.");
        if (cfopKind == NfeCfopCostKind.UnknownOutbound)
        {
            return Review(
                input,
                ComputeLanded(input),
                $"CFOP {Digits(input.Cfop)} de remessa/transferência não classificado com segurança. Revisar.");
        }

        var landed = ComputeLanded(input);
        if (landed.Line <= 0)
        {
            return Review(
                input,
                landed,
                "Custo efetivo da linha ficou ≤ 0 após descontos/encargos. Não voltar ao vProd bruto.")
                with { EffectiveLineCost = 0, EffectiveCommercialUnitCost = 0, EffectivePhysicalUnitCost = 0 };
        }

        if (NfeInfAdProdFinalPriceParser.TryParse(input.InfAdProd, out var finalUnit))
            return ResolveExplicitFinal(input, landed, finalUnit);

        var notes = new List<string> { landed.Explanation };
        var status = NfeEffectiveCostStatus.Calculado;
        var confidence = 0.8;
        var review = false;

        if (input.HeaderFreightUnallocated)
        {
            notes.Add("Frete só no total da NF — não rateado automaticamente.");
            status = NfeEffectiveCostStatus.Revisar;
            review = true;
            confidence = 0.45;
        }
        if (input.HeaderOtherUnallocated)
        {
            notes.Add("Outras despesas só no total da NF — não rateadas.");
            status = NfeEffectiveCostStatus.Revisar;
            review = true;
            confidence = Math.Min(confidence, 0.45);
        }
        if (input.HeaderDiscountUnallocated)
        {
            notes.Add("Desconto só no total/fatura — não rateado automaticamente.");
            status = NfeEffectiveCostStatus.Revisar;
            review = true;
            confidence = Math.Min(confidence, 0.5);
        }
        if (input.HeaderStUnallocated)
        {
            notes.Add("ICMS-ST só no total da NF — não rateado nesta versão.");
            status = NfeEffectiveCostStatus.Revisar;
            review = true;
            confidence = Math.Min(confidence, 0.5);
        }
        if (input.VIcmsStRet > 0.009 || input.VFcpStRet > 0.009)
            notes.Add("ST/FCP retido informado e não somado (evita duplicar tributo já recolhido).");

        if (input.VItem is > 0 && NfeCostTolerance.NearlyEqual(input.VItem.Value, landed.Line, landed.Line))
            notes.Add($"vItem {input.VItem.Value:N2} confere com o landed.");
        else if (input.VItem is > 0)
            notes.Add($"vItem {input.VItem.Value:N2} difere do landed {landed.Line:N2} (não promovido a fonte).");

        var commercial = UnitFromLine(landed.Line, input.QCom, input.VUnCom);
        return new NfeEffectiveCostDecision
        {
            GrossProductValue = landed.Gross,
            DiscountValue = landed.Discount,
            IncludedCharges = landed.Charges,
            StCharges = landed.St,
            EffectiveLineCost = landed.Line,
            DanfeLineCostWithoutSt = landed.WithoutSt,
            EffectiveCommercialUnitCost = commercial,
            EffectivePhysicalUnitCost = commercial,
            Source = NfeEffectiveCostSources.Landed,
            Status = status,
            Confidence = confidence,
            NeedsManualReview = review,
            IncludeInPayable = true,
            Explanation = string.Join(" ", notes),
        };
    }

    static NfeEffectiveCostDecision ResolveExplicitFinal(
        NfeEffectiveCostInput input, LandedParts landed, double finalUnit)
    {
        var qCom = input.QCom > 0 ? input.QCom : 1;
        var explicitLine = MonetaryRounding.Round(finalUnit * qCom);
        var reconciled = ReconcilesExplicit(input, landed, explicitLine);
        var commercial = Math.Round(finalUnit, 4);

        if (reconciled)
        {
            return new NfeEffectiveCostDecision
            {
                GrossProductValue = landed.Gross,
                DiscountValue = landed.Discount,
                IncludedCharges = landed.Charges,
                StCharges = landed.St,
                EffectiveLineCost = explicitLine,
                DanfeLineCostWithoutSt = landed.WithoutSt,
                EffectiveCommercialUnitCost = commercial,
                EffectivePhysicalUnitCost = commercial,
                Source = NfeEffectiveCostSources.PrecoUnitarioFinal,
                Status = NfeEffectiveCostStatus.Conferido,
                Confidence = 0.95,
                NeedsManualReview = false,
                IncludeInPayable = true,
                Explanation =
                    $"Preco Unitario Final {commercial:N4} validado contra composição/total da NF.",
            };
        }

        var fallbackCommercial = UnitFromLine(landed.Line, input.QCom, input.VUnCom);
        return new NfeEffectiveCostDecision
        {
            GrossProductValue = landed.Gross,
            DiscountValue = landed.Discount,
            IncludedCharges = landed.Charges,
            StCharges = landed.St,
            EffectiveLineCost = landed.Line,
            DanfeLineCostWithoutSt = landed.WithoutSt,
            EffectiveCommercialUnitCost = fallbackCommercial,
            EffectivePhysicalUnitCost = fallbackCommercial,
            Source = NfeEffectiveCostSources.Landed,
            Status = NfeEffectiveCostStatus.Revisar,
            Confidence = 0.4,
            NeedsManualReview = true,
            IncludeInPayable = true,
            Explanation =
                $"Preco Unitario Final {finalUnit:N4} encontrado, mas não reconciliou " +
                $"(linha {explicitLine:N2} vs landed {landed.Line:N2}). Usar landed e revisar.",
        };
    }

    static bool ReconcilesExplicit(NfeEffectiveCostInput input, LandedParts landed, double explicitLine)
    {
        if (NfeCostTolerance.NearlyEqual(explicitLine, landed.Line, landed.Line))
            return true;

        var share = input.HeaderVProd > 0.05
            ? input.VProd / input.HeaderVProd
            : 1;
        foreach (var total in DocumentReferences(input))
        {
            var expected = MonetaryRounding.Round(total * share);
            if (expected > 0 && NfeCostTolerance.NearlyEqual(explicitLine, expected, expected))
                return true;
        }

        return false;
    }

    static IEnumerable<double> DocumentReferences(NfeEffectiveCostInput input)
    {
        if (input.FatLiq > 0.05) yield return input.FatLiq;
        if (input.DupSum > 0.05) yield return input.DupSum;
        if (input.PagSum > 0.05) yield return input.PagSum;
        if (input.HeaderVNf > 0.05) yield return input.HeaderVNf;
    }

    static LandedParts ComputeLanded(NfeEffectiveCostInput input)
    {
        var gross = Math.Max(0, input.VProd);
        var discount = Math.Max(0, input.VDesc);
        var st = Math.Max(0, input.VIcmsSt) + Math.Max(0, input.VFcpSt);
        var otherCharges = Math.Max(0, input.VIpi)
            + Math.Max(0, input.VFrete)
            + Math.Max(0, input.VSeg)
            + Math.Max(0, input.VOutro);
        var charges = st + otherCharges;
        var withoutSt = Math.Round(gross + otherCharges - discount, 4);
        var line = Math.Round(gross + charges - discount, 4);
        return new LandedParts(
            gross,
            discount,
            charges,
            st,
            line,
            withoutSt,
            $"Landed = vProd {gross:N2} + encargos {charges:N2} − desc {discount:N2}.");
    }

    static double UnitFromLine(double line, double qCom, double vUnCom) =>
        qCom > 0.0000001 ? Math.Round(line / qCom, 6) : Math.Round(vUnCom, 6);

    static NfeEffectiveCostDecision NonPayable(
        NfeEffectiveCostInput input,
        NfeEffectiveCostStatus status,
        string source,
        double confidence,
        string explanation)
    {
        var landed = ComputeLanded(input);
        return new NfeEffectiveCostDecision
        {
            GrossProductValue = landed.Gross,
            DiscountValue = landed.Discount,
            IncludedCharges = landed.Charges,
            StCharges = landed.St,
            EffectiveLineCost = 0,
            DanfeLineCostWithoutSt = 0,
            EffectiveCommercialUnitCost = 0,
            EffectivePhysicalUnitCost = 0,
            Source = source,
            Status = status,
            Confidence = confidence,
            NeedsManualReview = status == NfeEffectiveCostStatus.Revisar,
            IncludeInPayable = false,
            Explanation = explanation,
        };
    }

    static NfeEffectiveCostDecision Review(NfeEffectiveCostInput input, LandedParts landed, string why)
    {
        var commercial = UnitFromLine(landed.Line, input.QCom, input.VUnCom);
        return new NfeEffectiveCostDecision
        {
            GrossProductValue = landed.Gross,
            DiscountValue = landed.Discount,
            IncludedCharges = landed.Charges,
            StCharges = landed.St,
            EffectiveLineCost = Math.Max(0, landed.Line),
            DanfeLineCostWithoutSt = Math.Max(0, landed.WithoutSt),
            EffectiveCommercialUnitCost = Math.Max(0, commercial),
            EffectivePhysicalUnitCost = Math.Max(0, commercial),
            Source = NfeEffectiveCostSources.Landed,
            Status = NfeEffectiveCostStatus.Revisar,
            Confidence = 0.35,
            NeedsManualReview = true,
            IncludeInPayable = true,
            Explanation = why,
        };
    }

    static string Digits(string? cfop) =>
        new((cfop ?? "").Where(char.IsDigit).ToArray());

    readonly record struct LandedParts(
        double Gross, double Discount, double Charges, double St,
        double Line, double WithoutSt, string Explanation);
}
