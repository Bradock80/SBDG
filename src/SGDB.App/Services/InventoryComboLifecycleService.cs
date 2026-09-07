using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// Persistência e disponibilidade PDV do ciclo de combo. Reusa Kit/Combo existente.
/// </summary>
public static class InventoryComboLifecycleService
{
    public const string UnavailableInPdvMessage =
        "Este combo não está disponível para novas vendas.";

    public static InventoryComboCampaign? GetBySuggestionKey(string suggestionKey)
    {
        if (string.IsNullOrWhiteSpace(suggestionKey))
            return null;
        using var conn = DatabaseService.OpenConnection();
        return ReadOne(conn, null, "suggestion_key = $k", "$k", suggestionKey.Trim());
    }

    public static InventoryComboCampaign? GetByProductId(int productId)
    {
        if (productId <= 0)
            return null;
        using var conn = DatabaseService.OpenConnection();
        return ReadOne(conn, null, "product_id = $id", "$id", productId);
    }

    public static IReadOnlyList<InventoryComboCampaign> ListByStatus(
        InventoryComboLifecycleStatus status,
        DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        using var conn = DatabaseService.OpenConnection();
        var rows = ReadMany(conn, null, "1=1");
        var list = new List<InventoryComboCampaign>();
        foreach (var row in rows)
        {
            var effective = WithEffective(row, day);
            if (effective.EffectiveStatus == status)
                list.Add(effective);
        }

        return list;
    }

    public static IReadOnlyList<InventoryComboCampaign> ListAll(DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        using var conn = DatabaseService.OpenConnection();
        return ReadMany(conn, null, "1=1").Select(r => WithEffective(r, day)).ToList();
    }

    public static double PossibleStock(InventoryComboCampaign campaign)
    {
        if (campaign.ProductId is not int productId)
            return 0;
        var product = ProductService.GetById(productId);
        return product is null ? 0 : ProductCompositionService.AvailableSaleQty(product) ?? 0;
    }

    public static string SalesSummary(int productId)
    {
        if (productId <= 0)
            return "Combo ainda sem produto cadastrado.";
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*), IFNULL(SUM(si.quantity),0), IFNULL(SUM(si.subtotal),0) "
            + "FROM sale_items si JOIN sales s ON s.id = si.sale_id "
            + "WHERE si.product_id = $id AND IFNULL(s.cancelled,0) = 0;";
        cmd.Parameters.AddWithValue("$id", productId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return "Nenhuma venda encontrada.";
        return $"{reader.GetInt32(0)} venda(s) · {reader.GetDouble(1):0.##} un. · R$ {reader.GetDouble(2):N2}";
    }

    public static void ReviewUnsafeMargins(DateOnly? today = null)
    {
        foreach (var campaign in ListAll(today))
        {
            if (campaign.EffectiveStatus != InventoryComboLifecycleStatus.Active
                || campaign.ProductId is not int productId)
                continue;
            var product = ProductService.GetById(productId);
            if (product is null)
                continue;
            if (InventoryComboLifecycleRules.ValidatePrice(product.SalePrice, product.CostPrice) is not null)
                Pause(campaign.Id);
        }
    }

    public static InventoryComboPresentationSnapshot OverlaySuggestions(
        InventoryComboPresentationSnapshot presented,
        DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(presented);
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        var campaigns = ListAll(day);
        var byKey = campaigns
            .GroupBy(c => c.SuggestionKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var targets = new List<InventoryComboTargetPresentationGroup>(presented.Targets.Count);
        foreach (var target in presented.Targets)
        {
            var kept = new List<InventoryComboSuggestionPresentationRow>();
            foreach (var row in target.Suggestions)
            {
                var key = InventoryComboLifecycleRules.SuggestionKey(row.TargetProductId, row.AnchorProductId);
                if (!byKey.TryGetValue(key, out var campaign))
                {
                    kept.Add(row);
                    continue;
                }

                if (campaign.Status is InventoryComboLifecycleStatus.Active
                    or InventoryComboLifecycleStatus.Paused
                    or InventoryComboLifecycleStatus.Finished
                    or InventoryComboLifecycleStatus.Expired)
                    continue;

                if (campaign.Status == InventoryComboLifecycleStatus.Rejected
                    && !InventoryComboLifecycleRules.CanRejectedReappear(
                        ParseDate(campaign.RejectedAt ?? ""),
                        campaign.RejectionSignature,
                        row.Signature,
                        day))
                    continue;

                kept.Add(row);
            }

            targets.Add(new InventoryComboTargetPresentationGroup
            {
                ProductId = target.ProductId,
                Code = target.Code,
                Name = target.Name,
                TargetTitle = target.TargetTitle,
                Reason = target.Reason,
                TargetReasonText = target.TargetReasonText,
                ConfidenceText = target.ConfidenceText,
                TargetStockText = target.TargetStockText,
                SuggestionCount = kept.Count,
                SuggestionCountText = InventoryComboPresentation.SuggestionCountText(kept.Count),
                EmptyMessage = kept.Count == 0 ? target.EmptyMessage : "",
                RejectionReasons = target.RejectionReasons,
                RejectionReasonTexts = target.RejectionReasonTexts,
                Suggestions = kept,
            });
        }

        var map = targets.ToDictionary(t => t.ProductId);
        return new InventoryComboPresentationSnapshot
        {
            QueryCount = presented.QueryCount,
            EmptySnapshotMessage = presented.EmptySnapshotMessage,
            DisclaimerText = presented.DisclaimerText,
            Targets = targets,
            ByProductId = map,
        };
    }

    public static bool IsPdvSellable(Product product, DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        var extra = ProductExtra.Parse(product.ExtraJson);
        if (!IsIntelligenceCombo(extra))
            return true;
        var campaign = GetByProductId(product.Id);
        if (campaign is null)
            return false;
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        var effective = InventoryComboLifecycleRules.EffectiveStatus(
            campaign.Status, day, campaign.StartDate, campaign.EndDate,
            campaign.SoldQty, campaign.MaxQty);
        if (!InventoryComboLifecycleRules.IsPdvSellable(effective))
            return false;
        return InventoryComboLifecycleRules.ValidatePrice(product.SalePrice, product.CostPrice) is null;
    }

    public static void ThrowIfNotSellable(Product product, double qty, DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        var extra = ProductExtra.Parse(product.ExtraJson);
        if (!IsIntelligenceCombo(extra))
            return;

        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        var campaign = GetByProductId(product.Id);
        if (campaign is null)
            throw new PdvException(UnavailableInPdvMessage);

        var effective = InventoryComboLifecycleRules.EffectiveStatus(
            campaign.Status, day, campaign.StartDate, campaign.EndDate,
            campaign.SoldQty, campaign.MaxQty);
        if (!InventoryComboLifecycleRules.IsPdvSellable(effective))
            throw new PdvException(UnavailableInPdvMessage);
        var marginErr = InventoryComboLifecycleRules.ValidatePrice(product.SalePrice, product.CostPrice);
        if (marginErr is not null)
            throw new PdvException(marginErr);

        if (campaign.MaxQty > 0 && campaign.SoldQty + qty > campaign.MaxQty + 0.0001)
            throw new PdvException("Quantidade máxima do combo já foi atingida.");

        var available = ProductCompositionService.AvailableSaleQty(product);
        if (available is not null && available.Value + 0.0001 < qty)
            throw new PdvException("Falta componente para o combo.");

        foreach (var item in ProductCompositionService.GetItems(extra))
        {
            var comp = ProductService.GetById(item.ProductId);
            if (comp is null || !comp.Active)
                throw new PdvException("Componente inativo. O combo não pode ser vendido.");
            if (InventoryComboLifecycleRules.IsReturnable(ProductExtra.Parse(comp.ExtraJson)))
                throw new PdvException(InventoryComboLifecycleRules.ReturnableBlockedMessage);
        }
    }

    public static void ApplySoldDelta(
        SqliteConnection conn,
        SqliteTransaction tx,
        int productId,
        double qty)
    {
        if (productId <= 0 || Math.Abs(qty) < 0.0000001)
            return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE inventory_combo_campaigns
            SET sold_qty = MAX(0, sold_qty + $q),
                updated_at = datetime('now','localtime')
            WHERE product_id = $id;
            """;
        cmd.Parameters.AddWithValue("$q", qty);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    public static InventoryComboApprovalResult Reject(
        string suggestionKey,
        string signature,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(suggestionKey))
            return Fail("Sugestão inválida.");
        using var conn = DatabaseService.OpenConnection();
        var existing = ReadOne(conn, null, "suggestion_key = $k", "$k", suggestionKey.Trim());
        if (existing is { Status: InventoryComboLifecycleStatus.Active or InventoryComboLifecycleStatus.Paused })
            return Fail("Combo já aprovado. Pause ou encerre em vez de descartar.");

        UpsertRejected(conn, suggestionKey.Trim(), signature ?? "", reason ?? "");
        AuditService.LogJson(
            "descartar",
            "combo",
            suggestionKey,
            new { suggestionKey, reason },
            "Sugestão de combo descartada");
        return new InventoryComboApprovalResult
        {
            Ok = true,
            Campaign = GetBySuggestionKey(suggestionKey),
        };
    }

    public static bool ShouldHideRejectedSuggestion(
        string suggestionKey,
        string currentSignature,
        DateOnly today)
    {
        var row = GetBySuggestionKey(suggestionKey);
        if (row is null || row.Status != InventoryComboLifecycleStatus.Rejected)
            return false;
        if (!DateOnly.TryParse(row.RejectedAt?[..Math.Min(10, row.RejectedAt.Length)], out var rejectedOn)
            && !DateOnly.TryParse(row.RejectedAt, out rejectedOn))
        {
            rejectedOn = today;
        }

        return !InventoryComboLifecycleRules.CanRejectedReappear(
            rejectedOn, row.RejectionSignature, currentSignature, today);
    }

    public static bool HasBlockingCampaign(string suggestionKey)
    {
        var row = GetBySuggestionKey(suggestionKey);
        if (row is null || row.ProductId is null)
            return false;
        return row.Status is InventoryComboLifecycleStatus.Active
            or InventoryComboLifecycleStatus.Paused
            or InventoryComboLifecycleStatus.Finished
            or InventoryComboLifecycleStatus.Expired;
    }

    public static InventoryComboApprovalResult Pause(int campaignId) =>
        SetStatus(campaignId, InventoryComboLifecycleStatus.Paused, "pausar");

    public static InventoryComboApprovalResult Finish(int campaignId) =>
        SetStatus(campaignId, InventoryComboLifecycleStatus.Finished, "encerrar");

    public static InventoryComboApprovalResult ChangeEndDate(int campaignId, DateOnly endDate)
    {
        using var conn = DatabaseService.OpenConnection();
        var row = ReadOne(conn, null, "id = $id", "$id", campaignId);
        if (row is null)
            return Fail("Combo não encontrado.");
        var err = InventoryComboLifecycleRules.ValidatePeriod(row.StartDate, endDate, DateOnly.FromDateTime(DateTime.Today));
        if (err is not null)
            return Fail(err);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE inventory_combo_campaigns
            SET end_date = $end, updated_at = datetime('now','localtime')
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$end", endDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$id", campaignId);
        cmd.ExecuteNonQuery();
        AuditService.LogJson("alterar", "combo", campaignId.ToString(),
            new { endDate = endDate.ToString("yyyy-MM-dd") },
            "Data final do combo alterada");
        return new InventoryComboApprovalResult { Ok = true, Campaign = GetBySuggestionKey(row.SuggestionKey) };
    }

    public static InventoryComboApprovalResult Reactivate(int campaignId, DateOnly? today = null)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        using var conn = DatabaseService.OpenConnection();
        var row = ReadOne(conn, null, "id = $id", "$id", campaignId);
        if (row is null || row.ProductId is not int productId)
            return Fail("Combo não encontrado.");
        var product = ProductService.GetById(productId)
            ?? throw new InvalidOperationException("Produto do combo não encontrado.");
        var extra = ProductExtra.Parse(product.ExtraJson);
        var items = ProductCompositionService.GetItems(extra);
        if (items.Count == 0)
            return Fail("Componentes do combo não encontrados.");
        foreach (var item in items)
        {
            var comp = ProductService.GetById(item.ProductId);
            if (comp is null || !comp.Active)
                return Fail("Componente inativo. Reative os produtos antes.");
            if (InventoryComboLifecycleRules.IsReturnable(ProductExtra.Parse(comp.ExtraJson)))
                return Fail(InventoryComboLifecycleRules.ReturnableBlockedMessage);
        }

        var priceErr = InventoryComboLifecycleRules.ValidatePrice(product.SalePrice, product.CostPrice);
        if (priceErr is not null)
            return Fail(priceErr);
        var periodErr = InventoryComboLifecycleRules.ValidatePeriod(row.StartDate, row.EndDate, day);
        if (periodErr is not null)
            return Fail(periodErr);

        extra.PermiteVenda = true;
        ProductService.Update(product.Id, ToInput(product, extra));
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                UPDATE inventory_combo_campaigns
                SET status = 'active', updated_at = datetime('now','localtime')
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", campaignId);
            cmd.ExecuteNonQuery();
        }

        AuditService.LogJson("reativar", "combo", campaignId.ToString(),
            new { productId }, "Combo reativado após nova validação");
        return new InventoryComboApprovalResult { Ok = true, Campaign = GetBySuggestionKey(row.SuggestionKey) };
    }

    public static void SetProductPermiteVenda(int productId, bool permite)
    {
        var product = ProductService.GetById(productId);
        if (product is null)
            return;
        var extra = ProductExtra.Parse(product.ExtraJson);
        extra.PermiteVenda = permite;
        ProductService.Update(product.Id, ToInput(product, extra));
    }

    internal static int InsertApproved(
        SqliteConnection conn,
        SqliteTransaction tx,
        InventoryComboCampaign campaign)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO inventory_combo_campaigns (
                product_id, suggestion_key, status, commercial_name, internal_code,
                target_product_id, anchor_product_id, target_name, anchor_name, target_qty, anchor_qty,
                price, cost, margin_percent, start_date, end_date, max_qty, sold_qty,
                origin, suggestion_json, reason, limitations, confidence,
                approved_by, approved_at, created_at, updated_at
            ) VALUES (
                $pid, $key, $status, $name, $code,
                $tid, $aid, $tname, $aname, $tqty, $aqty,
                $price, $cost, $margin, $start, $end, $max, 0,
                $origin, $json, $reason, $lim, $conf,
                $by, datetime('now','localtime'), datetime('now','localtime'), datetime('now','localtime')
            );
            SELECT last_insert_rowid();
            """;
        BindCampaign(cmd, campaign);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    internal static void UpdateApprovedProduct(
        SqliteConnection conn,
        SqliteTransaction tx,
        int id,
        int productId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE inventory_combo_campaigns
            SET product_id = $pid, status = 'active',
                rejected_at = NULL, rejected_by = '',
                updated_at = datetime('now','localtime')
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    static InventoryComboApprovalResult SetStatus(
        int campaignId,
        InventoryComboLifecycleStatus status,
        string action)
    {
        using var conn = DatabaseService.OpenConnection();
        var row = ReadOne(conn, null, "id = $id", "$id", campaignId);
        if (row is null)
            return Fail("Combo não encontrado.");
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE inventory_combo_campaigns
            SET status = $st, updated_at = datetime('now','localtime')
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$st", InventoryComboLifecycleRules.ToDb(status));
        cmd.Parameters.AddWithValue("$id", campaignId);
        cmd.ExecuteNonQuery();
        if (row.ProductId is int pid)
            SetProductPermiteVenda(pid, status == InventoryComboLifecycleStatus.Active);
        AuditService.LogJson(action, "combo", campaignId.ToString(),
            new { status = InventoryComboLifecycleRules.ToDb(status) },
            $"Combo {InventoryComboLifecycleRules.StatusLabel(status).ToLowerInvariant()}");
        return new InventoryComboApprovalResult { Ok = true, Campaign = GetBySuggestionKey(row.SuggestionKey) };
    }

    static void UpsertRejected(SqliteConnection conn, string key, string signature, string reason)
    {
        var existing = ReadOne(conn, null, "suggestion_key = $k", "$k", key);
        if (existing is null)
        {
            using var ins = conn.CreateCommand();
            ins.CommandText = """
                INSERT INTO inventory_combo_campaigns (
                    suggestion_key, status, reason, rejection_signature,
                    rejected_by, rejected_at, created_at, updated_at
                ) VALUES (
                    $key, 'rejected', $reason, $sig, $by,
                    datetime('now','localtime'), datetime('now','localtime'), datetime('now','localtime')
                );
                """;
            ins.Parameters.AddWithValue("$key", key);
            ins.Parameters.AddWithValue("$reason", reason);
            ins.Parameters.AddWithValue("$sig", signature);
            ins.Parameters.AddWithValue("$by", AppSession.UserLogin ?? "");
            ins.ExecuteNonQuery();
            return;
        }

        using var upd = conn.CreateCommand();
        upd.CommandText = """
            UPDATE inventory_combo_campaigns
            SET status = 'rejected', reason = $reason, rejection_signature = $sig,
                rejected_by = $by, rejected_at = datetime('now','localtime'),
                updated_at = datetime('now','localtime')
            WHERE suggestion_key = $key;
            """;
        upd.Parameters.AddWithValue("$reason", reason);
        upd.Parameters.AddWithValue("$sig", signature);
        upd.Parameters.AddWithValue("$by", AppSession.UserLogin ?? "");
        upd.Parameters.AddWithValue("$key", key);
        upd.ExecuteNonQuery();
    }

    static InventoryComboCampaign WithEffective(InventoryComboCampaign row, DateOnly today)
    {
        var effective = InventoryComboLifecycleRules.EffectiveStatus(
            row.Status, today, row.StartDate, row.EndDate, row.SoldQty, row.MaxQty);
        return new InventoryComboCampaign
        {
            Id = row.Id,
            ProductId = row.ProductId,
            SuggestionKey = row.SuggestionKey,
            Status = row.Status,
            EffectiveStatus = effective,
            CommercialName = row.CommercialName,
            InternalCode = row.InternalCode,
            TargetProductId = row.TargetProductId,
            AnchorProductId = row.AnchorProductId,
            TargetName = row.TargetName,
            AnchorName = row.AnchorName,
            TargetQty = row.TargetQty,
            AnchorQty = row.AnchorQty,
            Price = row.Price,
            Cost = row.Cost,
            MarginPercent = row.MarginPercent,
            StartDate = row.StartDate,
            EndDate = row.EndDate,
            MaxQty = row.MaxQty,
            SoldQty = row.SoldQty,
            Origin = row.Origin,
            Reason = row.Reason,
            Limitations = row.Limitations,
            Confidence = row.Confidence,
            SuggestionJson = row.SuggestionJson,
            ApprovedBy = row.ApprovedBy,
            ApprovedAt = row.ApprovedAt,
            RejectedBy = row.RejectedBy,
            RejectedAt = row.RejectedAt,
            RejectionSignature = row.RejectionSignature,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
        };
    }

    static InventoryComboCampaign? ReadOne(
        SqliteConnection conn,
        SqliteTransaction? tx,
        string where,
        string param,
        object value)
    {
        return ReadMany(conn, tx, where, param, value).FirstOrDefault();
    }

    static List<InventoryComboCampaign> ReadMany(
        SqliteConnection conn,
        SqliteTransaction? tx,
        string where,
        string? param = null,
        object? value = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT id, product_id, suggestion_key, status, commercial_name, IFNULL(internal_code,''), "
            + "target_product_id, anchor_product_id, target_qty, anchor_qty, "
            + "price, cost, margin_percent, start_date, end_date, max_qty, sold_qty, "
            + "origin, IFNULL(suggestion_json,''), IFNULL(reason,''), IFNULL(limitations,''), "
            + "IFNULL(confidence,''), IFNULL(approved_by,''), approved_at, "
            + "IFNULL(rejected_by,''), rejected_at, IFNULL(rejection_signature,''), "
            + "created_at, updated_at, IFNULL(target_name,''), IFNULL(anchor_name,'') "
            + "FROM inventory_combo_campaigns WHERE " + where + " ORDER BY id;";
        if (param is not null)
            cmd.Parameters.AddWithValue(param, value ?? DBNull.Value);
        var list = new List<InventoryComboCampaign>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(Map(reader));
        return list;
    }

    static InventoryComboCampaign Map(SqliteDataReader reader)
    {
        var start = ParseDate(reader.IsDBNull(13) ? "" : reader.GetString(13));
        var end = ParseDate(reader.IsDBNull(14) ? "" : reader.GetString(14));
        var status = InventoryComboLifecycleRules.ToStatus(reader.GetString(3));
        return new InventoryComboCampaign
        {
            Id = reader.GetInt32(0),
            ProductId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
            SuggestionKey = reader.GetString(2),
            Status = status,
            EffectiveStatus = status,
            CommercialName = reader.IsDBNull(4) ? "" : reader.GetString(4),
            InternalCode = reader.GetString(5),
            TargetProductId = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
            AnchorProductId = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
            TargetQty = reader.IsDBNull(8) ? 1 : reader.GetDouble(8),
            AnchorQty = reader.IsDBNull(9) ? 1 : reader.GetDouble(9),
            Price = reader.IsDBNull(10) ? 0 : reader.GetDouble(10),
            Cost = reader.IsDBNull(11) ? 0 : reader.GetDouble(11),
            MarginPercent = reader.IsDBNull(12) ? 0 : reader.GetDouble(12),
            StartDate = start,
            EndDate = end,
            MaxQty = reader.IsDBNull(15) ? 0 : reader.GetDouble(15),
            SoldQty = reader.IsDBNull(16) ? 0 : reader.GetDouble(16),
            Origin = reader.IsDBNull(17) ? InventoryComboLifecycleRules.OriginEstoqueInteligente : reader.GetString(17),
            SuggestionJson = reader.GetString(18),
            Reason = reader.GetString(19),
            Limitations = reader.GetString(20),
            Confidence = reader.GetString(21),
            ApprovedBy = reader.GetString(22),
            ApprovedAt = reader.IsDBNull(23) ? null : reader.GetString(23),
            RejectedBy = reader.GetString(24),
            RejectedAt = reader.IsDBNull(25) ? null : reader.GetString(25),
            RejectionSignature = reader.GetString(26),
            CreatedAt = reader.IsDBNull(27) ? "" : reader.GetString(27),
            UpdatedAt = reader.IsDBNull(28) ? "" : reader.GetString(28),
            TargetName = reader.FieldCount > 29 && !reader.IsDBNull(29) ? reader.GetString(29) : "",
            AnchorName = reader.FieldCount > 30 && !reader.IsDBNull(30) ? reader.GetString(30) : "",
        };
    }

    static void BindCampaign(SqliteCommand cmd, InventoryComboCampaign campaign)
    {
        cmd.Parameters.AddWithValue("$pid", (object?)campaign.ProductId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$key", campaign.SuggestionKey);
        cmd.Parameters.AddWithValue("$status", InventoryComboLifecycleRules.ToDb(campaign.Status));
        cmd.Parameters.AddWithValue("$name", campaign.CommercialName);
        cmd.Parameters.AddWithValue("$code", campaign.InternalCode);
        cmd.Parameters.AddWithValue("$tid", campaign.TargetProductId);
        cmd.Parameters.AddWithValue("$aid", campaign.AnchorProductId);
        cmd.Parameters.AddWithValue("$tname", campaign.TargetName);
        cmd.Parameters.AddWithValue("$aname", campaign.AnchorName);
        cmd.Parameters.AddWithValue("$tqty", campaign.TargetQty);
        cmd.Parameters.AddWithValue("$aqty", campaign.AnchorQty);
        cmd.Parameters.AddWithValue("$price", campaign.Price);
        cmd.Parameters.AddWithValue("$cost", campaign.Cost);
        cmd.Parameters.AddWithValue("$margin", campaign.MarginPercent);
        cmd.Parameters.AddWithValue("$start", campaign.StartDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$end", campaign.EndDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$max", campaign.MaxQty);
        cmd.Parameters.AddWithValue("$origin", campaign.Origin);
        cmd.Parameters.AddWithValue("$json", campaign.SuggestionJson);
        cmd.Parameters.AddWithValue("$reason", campaign.Reason);
        cmd.Parameters.AddWithValue("$lim", campaign.Limitations);
        cmd.Parameters.AddWithValue("$conf", campaign.Confidence);
        cmd.Parameters.AddWithValue("$by", campaign.ApprovedBy);
    }

    static DateOnly ParseDate(string raw)
    {
        if (DateOnly.TryParse(raw, out var d))
            return d;
        if (raw.Length >= 10 && DateOnly.TryParse(raw[..10], out d))
            return d;
        return DateOnly.FromDateTime(DateTime.Today);
    }

    static bool IsIntelligenceCombo(ProductExtra extra) =>
        string.Equals(extra.ComboOrigem, InventoryComboLifecycleRules.OriginEstoqueInteligente, StringComparison.Ordinal)
        || !string.IsNullOrWhiteSpace(extra.ComboSuggestionKey);

    static ProductInput ToInput(Product product, ProductExtra extra) =>
        new()
        {
            Code = product.Code,
            Barcode = product.Barcode,
            Name = product.Name,
            GroupName = product.GroupName,
            Unit = product.Unit,
            CostPrice = product.CostPrice,
            SalePrice = product.SalePrice,
            MinStock = product.MinStock,
            Stock = product.Stock,
            StockFridge = product.StockFridge,
            StockFridgeMin = product.StockFridgeMin,
            Location = product.Location,
            Extra = extra,
            Active = product.Active,
        };

    static InventoryComboApprovalResult Fail(string error) =>
        new() { Ok = false, Error = error };
}
