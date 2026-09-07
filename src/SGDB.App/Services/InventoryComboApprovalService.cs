using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// Aprovação explícita: cadastra Kit/Combo oficial e ativa no PDV.
/// </summary>
public static class InventoryComboApprovalService
{
    public static InventoryComboApprovalDraft BuildDraft(
        InventoryComboSuggestion suggestion,
        string targetName,
        string anchorName,
        DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        var key = InventoryComboLifecycleRules.SuggestionKey(
            suggestion.TargetProductId, suggestion.AnchorProductId);
        var cost = suggestion.PairCost;
        var separate = suggestion.NormalPairPrice;
        var suggestedPrice = PickSafePrice(suggestion, cost);
        var hasSafe = suggestedPrice is not null;
        var price = suggestedPrice ?? 0;
        var margin = hasSafe ? InventoryComboLifecycleRules.MarginPercent(price, cost) : double.NaN;
        var discount = separate - price;
        var discountPct = separate > 0 && hasSafe ? (discount / separate) * 100.0 : 0;
        var duration = suggestion.SuggestedDurationDays > 0 ? suggestion.SuggestedDurationDays : 7;
        var limitations = string.Join(" · ", suggestion.Limitations.Select(InventoryComboPresentation.LimitationText));
        var signature = InventoryComboLifecycleRules.Signature(
            suggestion.TargetProductId,
            suggestion.AnchorProductId,
            price,
            cost,
            suggestion.PairEvidence,
            suggestion.MaxSafeQuantity);

        string? block = null;
        if (!hasSafe || !InventoryComboLifecycleRules.HasKnownCost(cost))
            block = InventoryComboLifecycleRules.NoSafePriceMessage;
        else
            block = InventoryComboLifecycleRules.ValidatePrice(price, cost);

        return new InventoryComboApprovalDraft
        {
            SuggestionKey = key,
            CommercialName = InventoryComboLifecycleRules.DefaultCommercialName(targetName, anchorName),
            TargetProductId = suggestion.TargetProductId,
            AnchorProductId = suggestion.AnchorProductId,
            TargetName = targetName,
            AnchorName = anchorName,
            TargetQty = 1,
            AnchorQty = 1,
            TargetStock = suggestion.TargetStock,
            AnchorStock = suggestion.AnchorStock,
            SeparatePrice = separate,
            SuggestedPrice = price,
            DiscountAmount = hasSafe ? Math.Max(0, discount) : 0,
            DiscountPercent = hasSafe ? Math.Max(0, discountPct) : 0,
            Cost = cost,
            GrossProfit = hasSafe ? price - cost : 0,
            MarginPercent = hasSafe ? margin : 0,
            MaxSafeQuantity = suggestion.MaxSafeQuantity,
            StartDate = day,
            EndDate = day.AddDays(duration),
            Reason = suggestion.TargetReason.ToString(),
            Confidence = suggestion.Confidence.ToString(),
            Limitations = limitations,
            SuggestionJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                suggestion.TargetProductId,
                suggestion.AnchorProductId,
                suggestion.PairEvidence,
                suggestion.NormalPairPrice,
                suggestion.PairCost,
                suggestion.PairFloorPrice,
                suggestion.MaxSafeQuantity,
                suggestion.Limitations,
            }),
            Signature = signature,
            HasSafePrice = hasSafe && block is null,
            BlockReason = block,
        };
    }

    public static InventoryComboApprovalResult Confirm(
        InventoryComboApprovalDraft draft,
        DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        if (InventoryComboLifecycleService.HasBlockingCampaign(draft.SuggestionKey))
            return Fail("Aprovação duplicada. Este combo já foi cadastrado.");

        var target = ProductService.GetById(draft.TargetProductId);
        var anchor = ProductService.GetById(draft.AnchorProductId);
        if (target is null || anchor is null)
            return Fail("Componente não encontrado.");
        if (!target.Active || !anchor.Active)
            return Fail("Componente inativo.");

        var targetExtra = ProductExtra.Parse(target.ExtraJson);
        var anchorExtra = ProductExtra.Parse(anchor.ExtraJson);
        if (InventoryComboLifecycleRules.IsReturnable(targetExtra)
            || InventoryComboLifecycleRules.IsReturnable(anchorExtra))
            return Fail(InventoryComboLifecycleRules.ReturnableBlockedMessage);

        var cost = RoundMoney(target.CostPrice * draft.TargetQty + anchor.CostPrice * draft.AnchorQty);
        if (!InventoryComboLifecycleRules.HasKnownCost(target.CostPrice)
            || !InventoryComboLifecycleRules.HasKnownCost(anchor.CostPrice)
            || !InventoryComboLifecycleRules.HasKnownCost(cost))
            return Fail(InventoryComboLifecycleRules.NoSafePriceMessage);

        var priceErr = InventoryComboLifecycleRules.ValidatePrice(draft.SuggestedPrice, cost);
        if (priceErr is not null)
            return Fail(priceErr);
        var periodErr = InventoryComboLifecycleRules.ValidatePeriod(draft.StartDate, draft.EndDate, day);
        if (periodErr is not null)
            return Fail(periodErr);
        var qtyErr = InventoryComboLifecycleRules.ValidateQuantity(draft.MaxSafeQuantity);
        if (qtyErr is not null)
            return Fail(qtyErr);
        if (target.TotalStock + 0.0001 < draft.TargetQty || anchor.TotalStock + 0.0001 < draft.AnchorQty)
            return Fail("Estoque insuficiente nos componentes para aprovar o combo.");

        var name = string.IsNullOrWhiteSpace(draft.CommercialName)
            ? InventoryComboLifecycleRules.DefaultCommercialName(target.Name, anchor.Name)
            : draft.CommercialName.Trim();
        if (name.Length > 180)
            name = name[..180];

        var extra = new ProductExtra
        {
            PermiteVenda = true,
            Composicao = true,
            ComposicaoItens =
            [
                new ProductCompositionItem
                {
                    ProductId = target.Id,
                    Quantity = draft.TargetQty,
                    Code = target.Code ?? "",
                    Name = target.Name,
                    Unit = target.Unit,
                    Cost = target.CostPrice,
                },
                new ProductCompositionItem
                {
                    ProductId = anchor.Id,
                    Quantity = draft.AnchorQty,
                    Code = anchor.Code ?? "",
                    Name = anchor.Name,
                    Unit = anchor.Unit,
                    Cost = anchor.CostPrice,
                },
            ],
            ComboOrigem = InventoryComboLifecycleRules.OriginEstoqueInteligente,
            ComboSuggestionKey = draft.SuggestionKey,
        };
        ProductCompositionService.Validate(extra);

        var margin = InventoryComboLifecycleRules.MarginPercent(draft.SuggestedPrice, cost);
        var code = InventoryComboLifecycleRules.DefaultInternalCode(target.Id, anchor.Id);
        var product = ProductService.Create(new ProductInput
        {
            Code = code,
            Name = name,
            GroupName = "Combos",
            Unit = "UN",
            CostPrice = cost,
            SalePrice = draft.SuggestedPrice,
            Extra = extra,
            Active = true,
        });

        extra.ComboSuggestionKey = draft.SuggestionKey;
        extra.ComboOrigem = InventoryComboLifecycleRules.OriginEstoqueInteligente;
        ProductService.Update(product.Id, new ProductInput
        {
            Code = product.Code,
            Barcode = product.Barcode,
            Name = product.Name,
            GroupName = product.GroupName,
            Unit = product.Unit,
            CostPrice = cost,
            SalePrice = draft.SuggestedPrice,
            Extra = extra,
            Active = true,
        });

        var campaign = new InventoryComboCampaign
        {
            ProductId = product.Id,
            SuggestionKey = draft.SuggestionKey,
            Status = InventoryComboLifecycleStatus.Active,
            EffectiveStatus = InventoryComboLifecycleStatus.Active,
            CommercialName = name,
            InternalCode = product.Code ?? code,
            TargetProductId = target.Id,
            AnchorProductId = anchor.Id,
            TargetName = target.Name,
            AnchorName = anchor.Name,
            TargetQty = draft.TargetQty,
            AnchorQty = draft.AnchorQty,
            Price = draft.SuggestedPrice,
            Cost = cost,
            MarginPercent = margin,
            StartDate = draft.StartDate,
            EndDate = draft.EndDate,
            MaxQty = draft.MaxSafeQuantity,
            Origin = InventoryComboLifecycleRules.OriginEstoqueInteligente,
            Reason = draft.Reason,
            Limitations = draft.Limitations,
            Confidence = draft.Confidence,
            SuggestionJson = draft.SuggestionJson,
            ApprovedBy = AppSession.UserLogin ?? "",
        };

        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        var existing = InventoryComboLifecycleService.GetBySuggestionKey(draft.SuggestionKey);
        int campaignId;
        if (existing is null)
        {
            campaignId = InventoryComboLifecycleService.InsertApproved(conn, tx, campaign);
        }
        else if (existing.ProductId is not null
                 && existing.Status is not InventoryComboLifecycleStatus.Rejected)
        {
            tx.Rollback();
            return Fail("Aprovação duplicada. Este combo já foi cadastrado.");
        }
        else
        {
            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = """
                UPDATE inventory_combo_campaigns
                    SET product_id = $pid, status = 'active', commercial_name = $name,
                    internal_code = $code, target_product_id = $tid, anchor_product_id = $aid,
                    target_name = $tname, anchor_name = $aname,
                    target_qty = $tqty, anchor_qty = $aqty, price = $price, cost = $cost,
                    margin_percent = $margin, start_date = $start, end_date = $end,
                    max_qty = $max, origin = $origin, suggestion_json = $json,
                    reason = $reason, limitations = $lim, confidence = $conf,
                    approved_by = $by, approved_at = datetime('now','localtime'),
                    rejected_at = NULL, rejected_by = '',
                    updated_at = datetime('now','localtime')
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$pid", product.Id);
            upd.Parameters.AddWithValue("$name", name);
            upd.Parameters.AddWithValue("$code", product.Code ?? code);
            upd.Parameters.AddWithValue("$tid", target.Id);
            upd.Parameters.AddWithValue("$aid", anchor.Id);
            upd.Parameters.AddWithValue("$tname", target.Name);
            upd.Parameters.AddWithValue("$aname", anchor.Name);
            upd.Parameters.AddWithValue("$tqty", draft.TargetQty);
            upd.Parameters.AddWithValue("$aqty", draft.AnchorQty);
            upd.Parameters.AddWithValue("$price", draft.SuggestedPrice);
            upd.Parameters.AddWithValue("$cost", cost);
            upd.Parameters.AddWithValue("$margin", margin);
            upd.Parameters.AddWithValue("$start", draft.StartDate.ToString("yyyy-MM-dd"));
            upd.Parameters.AddWithValue("$end", draft.EndDate.ToString("yyyy-MM-dd"));
            upd.Parameters.AddWithValue("$max", draft.MaxSafeQuantity);
            upd.Parameters.AddWithValue("$origin", InventoryComboLifecycleRules.OriginEstoqueInteligente);
            upd.Parameters.AddWithValue("$json", draft.SuggestionJson);
            upd.Parameters.AddWithValue("$reason", draft.Reason);
            upd.Parameters.AddWithValue("$lim", draft.Limitations);
            upd.Parameters.AddWithValue("$conf", draft.Confidence);
            upd.Parameters.AddWithValue("$by", AppSession.UserLogin ?? "");
            upd.Parameters.AddWithValue("$id", existing.Id);
            upd.ExecuteNonQuery();
            campaignId = existing.Id;
        }

        AuditService.LogJson(
            conn, tx, "aprovar", "combo", campaignId.ToString(),
            new
            {
                productId = product.Id,
                draft.SuggestionKey,
                name,
                price = draft.SuggestedPrice,
                cost,
                margin,
            },
            $"Combo aprovado: {name}");
        tx.Commit();

        return new InventoryComboApprovalResult
        {
            Ok = true,
            Campaign = InventoryComboLifecycleService.GetBySuggestionKey(draft.SuggestionKey),
        };
    }

    static double? PickSafePrice(InventoryComboSuggestion suggestion, double cost)
    {
        foreach (var scenario in suggestion.Scenarios)
        {
            if (InventoryComboLifecycleRules.ValidatePrice(scenario.PairPrice, cost) is null)
                return ProductPriceHelper.RoundPrice(scenario.PairPrice);
        }

        if (InventoryComboLifecycleRules.ValidatePrice(suggestion.PairFloorPrice, cost) is null)
            return ProductPriceHelper.RoundPrice(suggestion.PairFloorPrice);
        return null;
    }

    public static InventoryComboApprovalDraft ReloadDraft(
        int targetProductId,
        int anchorProductId,
        DateOnly? today = null)
    {
        var target = ProductService.GetById(targetProductId)
            ?? throw new InvalidOperationException("Produto-alvo não encontrado.");
        var anchor = ProductService.GetById(anchorProductId)
            ?? throw new InvalidOperationException("Acompanhante não encontrado.");
        var financial = InventoryComboPairFinancialEngine.Evaluate(new InventoryComboPairFinancialInput
        {
            TargetFacts = InventoryCommercialFactsEngine.Classify(FactsInput(target)),
            AnchorFacts = InventoryCommercialFactsEngine.Classify(FactsInput(anchor)),
            MinGrossMarginPolicy = InventorySmartMargin.RecommendationPolicy(),
        });
        var suggestion = new InventoryComboSuggestion
        {
            TargetProductId = target.Id,
            AnchorProductId = anchor.Id,
            NormalPairPrice = financial.NormalPairPrice ?? 0,
            PairCost = financial.PairCost ?? 0,
            PairFloorPrice = financial.PairFloorPrice ?? 0,
            Scenarios = financial.Scenarios,
            TargetStock = target.TotalStock,
            AnchorStock = anchor.TotalStock,
            MaxSafeQuantity = MaxSafe(target, anchor),
            SuggestedDurationDays = 7,
            PairEvidence = InventoryComboPairEvidence.Weak,
        };
        var draft = BuildDraft(suggestion, target.Name, anchor.Name, today);
        if (InventoryComboLifecycleRules.IsReturnable(ProductExtra.Parse(target.ExtraJson))
            || InventoryComboLifecycleRules.IsReturnable(ProductExtra.Parse(anchor.ExtraJson)))
        {
            return new InventoryComboApprovalDraft
            {
                SuggestionKey = draft.SuggestionKey,
                CommercialName = draft.CommercialName,
                TargetProductId = draft.TargetProductId,
                AnchorProductId = draft.AnchorProductId,
                TargetName = draft.TargetName,
                AnchorName = draft.AnchorName,
                TargetQty = draft.TargetQty,
                AnchorQty = draft.AnchorQty,
                TargetStock = draft.TargetStock,
                AnchorStock = draft.AnchorStock,
                SeparatePrice = draft.SeparatePrice,
                SuggestedPrice = draft.SuggestedPrice,
                DiscountAmount = draft.DiscountAmount,
                DiscountPercent = draft.DiscountPercent,
                Cost = draft.Cost,
                GrossProfit = draft.GrossProfit,
                MarginPercent = draft.MarginPercent,
                MaxSafeQuantity = draft.MaxSafeQuantity,
                StartDate = draft.StartDate,
                EndDate = draft.EndDate,
                Reason = draft.Reason,
                Confidence = draft.Confidence,
                Limitations = draft.Limitations,
                SuggestionJson = draft.SuggestionJson,
                Signature = draft.Signature,
                HasSafePrice = false,
                BlockReason = InventoryComboLifecycleRules.ReturnableBlockedMessage,
            };
        }

        return draft;
    }

    static InventoryCommercialFactsInput FactsInput(Product product)
    {
        var extra = ProductExtra.Parse(product.ExtraJson);
        return new InventoryCommercialFactsInput
        {
            ProductId = product.Id,
            ProductFound = true,
            CatalogSalePrice = product.SalePrice,
            CurrentAverageCost = product.CostPrice,
            AllowsSale = extra.PermiteVenda,
            IsCompositionProduct = extra.Composicao,
            HasReturnableContainer = extra.VasilhameTipoId is > 0,
            WholesalePrice = extra.PrecoAtacado,
            WholesaleMinimumQuantity = extra.QtdAtacado,
            UnitSalePrice = extra.PrecoAvulso,
        };
    }

    static double MaxSafe(Product target, Product anchor)
    {
        var t = Math.Max(0, Math.Floor(target.TotalStock));
        var a = Math.Max(0, Math.Floor(anchor.TotalStock));
        return Math.Min(t, a);
    }

    static double RoundMoney(double value) => ProductPriceHelper.RoundPrice(value);

    static InventoryComboApprovalResult Fail(string error) =>
        new() { Ok = false, Error = error };
}
