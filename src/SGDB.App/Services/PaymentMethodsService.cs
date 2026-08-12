using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using SGDB.Domain.Finance;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public static class PaymentMethodsService
{
    private static readonly object CacheLock = new();
    private static IReadOnlyList<PaymentMethodRow>? _cache;
    private static bool _seedDone;

    public static readonly IReadOnlyList<PaymentMethodRow> Catalog =
    [
        new() { Id = "dinheiro", Name = "DINHEIRO", ApiLabel = "Dinheiro", MovementType = "Entrada", Active = true, PdvKey = "a", Notes = "Tecla A no PDV — fica no caixa físico", FeeEditable = false, SettlementDays = 0, IsSystem = true, SortOrder = 10, DestinationKind = "caixa" },
        new() { Id = "debito", Name = "CARTÃO DE DÉBITO", ApiLabel = "Cartão Débito", MovementType = "Entrada", Active = true, PdvKey = "b", Notes = "Tecla B no PDV", FeeEditable = true, SettlementDays = 1, IsSystem = true, SortOrder = 20, DestinationKind = "banco" },
        new() { Id = "credito", Name = "CARTÃO DE CRÉDITO", ApiLabel = "Cartão Crédito", MovementType = "Entrada", Active = true, PdvKey = "c", Notes = "Tecla C no PDV — taxa = crédito à vista (1x)", FeeEditable = true, SettlementDays = 30, IsSystem = true, SortOrder = 30, DestinationKind = "banco" },
        new() { Id = "pix", Name = "PIX QR CODE", ApiLabel = "Pix", MovementType = "Entrada", Active = true, PdvKey = "d", Notes = "Tecla D — gera QR Mercado Pago", FeeEditable = true, SettlementDays = 0, IsSystem = true, SortOrder = 40, DestinationKind = "banco" },
        new() { Id = "pix_chave", Name = "PIX CHAVE", ApiLabel = "Pix Chave", MovementType = "Entrada", Active = true, PdvKey = "f", Notes = "PIX pela chave — sem QR; confirme o pagamento manualmente", FeeEditable = true, SettlementDays = 0, IsSystem = true, SortOrder = 45, DestinationKind = "banco" },
        new() { Id = "fiado", Name = "À PRAZO", ApiLabel = "Fiado", MovementType = "A receber", Active = true, PdvKey = "e", Notes = "Tecla E no PDV — gera conta a receber", FeeEditable = false, SettlementDays = 0, IsSystem = true, SortOrder = 50, DestinationKind = "receber" },
        new() { Id = "cheque", Name = "CHEQUE", ApiLabel = "Cheque", MovementType = "Entrada", Active = false, Notes = "Disponível em compras e contas", FeeEditable = true, SettlementDays = 0, IsSystem = true, SortOrder = 60, DestinationKind = "banco" },
        new() { Id = "deposito", Name = "DEPÓSITO", ApiLabel = "Depósito", MovementType = "Entrada", Active = false, FeeEditable = true, SettlementDays = 0, IsSystem = true, SortOrder = 70, DestinationKind = "banco" },
        new() { Id = "nota", Name = "NOTA", ApiLabel = "Nota", MovementType = "Entrada", Active = false, FeeEditable = true, SettlementDays = 0, IsSystem = true, SortOrder = 80, DestinationKind = "banco" },
        new() { Id = "troca", Name = "TROCA", ApiLabel = "Troca", MovementType = "Entrada", Active = false, FeeEditable = false, SettlementDays = 0, IsSystem = true, SortOrder = 90, DestinationKind = "banco" },
    ];

    /// <summary>Forma da família PIX (QR, chave, custom com “pix” no nome).</summary>
    public static bool IsPixFamily(string? methodId, string? nameOrLabel = null)
    {
        var (id, name) = ResolveIdName(methodId, nameOrLabel);
        return id.Contains("pix", StringComparison.Ordinal)
               || name.Contains("pix", StringComparison.Ordinal);
    }

    /// <summary>
    /// True só para PIX que abre QR Mercado Pago.
    /// PIX Chave / manual / cópia-e-cola NÃO gera QR.
    /// </summary>
    public static bool RequiresMercadoPagoQr(string? methodId, string? nameOrLabel = null)
    {
        var (id, name) = ResolveIdName(methodId, nameOrLabel);
        if (string.IsNullOrEmpty(id) && string.IsNullOrEmpty(name))
            return false;

        var blob = id + " " + name;
        if (IsManualPixText(blob) || id is "pix_chave" or "pixchave" or "pix_manual")
            return false;

        if (id is "pix" or "pix_qr" or "pixqr")
            return true;
        if (name.Contains("qr", StringComparison.Ordinal))
            return true;

        // Legado: qualquer outro “pix…” ainda abre QR (exceto chave, já filtrado)
        return id.Contains("pix", StringComparison.Ordinal)
               || name.Contains("pix", StringComparison.Ordinal);
    }

    private static bool IsManualPixText(string blob) =>
        blob.Contains("chave", StringComparison.Ordinal)
        || blob.Contains("manual", StringComparison.Ordinal)
        || blob.Contains("copia", StringComparison.Ordinal)
        || blob.Contains("cópia", StringComparison.Ordinal)
        || blob.Contains("sem qr", StringComparison.Ordinal);

    private static (string Id, string Name) ResolveIdName(string? methodId, string? nameOrLabel)
    {
        var id = (methodId ?? "").Trim().ToLowerInvariant();
        var name = (nameOrLabel ?? "").Trim().ToLowerInvariant();

        if (!string.IsNullOrEmpty(id) && string.IsNullOrEmpty(name))
        {
            var m = GetById(id);
            if (m is not null)
                name = ((m.Name ?? "") + " " + (m.ApiLabel ?? "")).Trim().ToLowerInvariant();
        }
        else if (string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(nameOrLabel))
        {
            var raw = nameOrLabel.Trim();
            var m = List().FirstOrDefault(x =>
                x.Name.Equals(raw, StringComparison.OrdinalIgnoreCase)
                || x.ApiLabel.Equals(raw, StringComparison.OrdinalIgnoreCase));
            if (m is not null)
            {
                id = m.Id.ToLowerInvariant();
                name = ((m.Name ?? "") + " " + (m.ApiLabel ?? "")).Trim().ToLowerInvariant();
            }
        }

        return (id, name);
    }

    /// <summary>Garante linhas do catálogo no banco (idempotente, leve).</summary>
    public static void EnsureSeeded(SqliteConnection conn)
    {
        if (_seedDone)
            return;

        foreach (var baseRow in Catalog)
            UpsertCatalogRow(conn, baseRow);

        _seedDone = true;
        InvalidateCache();
    }

    private static void UpsertCatalogRow(SqliteConnection conn, PaymentMethodRow baseRow)
    {
        using var exists = conn.CreateCommand();
        exists.CommandText = "SELECT IFNULL(name,'') FROM payment_method_fees WHERE method_id = $id LIMIT 1;";
        exists.Parameters.AddWithValue("$id", baseRow.Id);
        var existingName = exists.ExecuteScalar() as string;
        if (existingName is null)
        {
            InsertCatalogRow(conn, baseRow);
            return;
        }

        // Só preenche metadados se a linha ainda estiver “crua” (só taxa, sem nome).
        if (!string.IsNullOrWhiteSpace(existingName))
            return;

        using var fill = conn.CreateCommand();
        fill.CommandText = """
            UPDATE payment_method_fees SET
                name = $name,
                api_label = COALESCE(NULLIF(TRIM(IFNULL(api_label,'')), ''), $api),
                movement_type = COALESCE(NULLIF(TRIM(IFNULL(movement_type,'')), ''), $mov),
                pdv_key = CASE
                    WHEN pdv_key IS NULL OR TRIM(IFNULL(pdv_key,'')) = '' THEN $pdv
                    ELSE pdv_key END,
                notes = CASE
                    WHEN notes IS NULL OR TRIM(IFNULL(notes,'')) = '' THEN $notes
                    ELSE notes END,
                fee_editable = COALESCE(fee_editable, $feeEd),
                is_system = 1,
                destination_kind = COALESCE(NULLIF(TRIM(IFNULL(destination_kind,'')), ''), $dest),
                sort_order = CASE WHEN IFNULL(sort_order, 100) = 100 THEN $sort ELSE sort_order END,
                active = $active,
                updated_at = datetime('now')
            WHERE method_id = $id;
            """;
        fill.Parameters.AddWithValue("$id", baseRow.Id);
        fill.Parameters.AddWithValue("$name", baseRow.Name);
        fill.Parameters.AddWithValue("$api", baseRow.ApiLabel);
        fill.Parameters.AddWithValue("$mov", baseRow.MovementType);
        fill.Parameters.AddWithValue("$pdv", (object?)NullIfEmpty(baseRow.PdvKey) ?? DBNull.Value);
        fill.Parameters.AddWithValue("$notes", (object?)NullIfEmpty(baseRow.Notes) ?? DBNull.Value);
        fill.Parameters.AddWithValue("$feeEd", baseRow.FeeEditable ? 1 : 0);
        fill.Parameters.AddWithValue("$dest", baseRow.DestinationKind);
        fill.Parameters.AddWithValue("$sort", baseRow.SortOrder);
        fill.Parameters.AddWithValue("$active", baseRow.Active ? 1 : 0);
        fill.ExecuteNonQuery();
    }

    private static void InsertCatalogRow(SqliteConnection conn, PaymentMethodRow baseRow)
    {
        using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO payment_method_fees (
                method_id, fee_percent, settlement_days, fee_fixed, bank_account_id,
                name, api_label, movement_type, active, pdv_key, sort_order, notes,
                fee_editable, is_system, destination_kind, created_at, updated_at
            ) VALUES (
                $id, 0, $days, 0, NULL,
                $name, $api, $mov, $active, $pdv, $sort, $notes,
                $feeEd, 1, $dest, datetime('now'), datetime('now')
            );
            """;
        ins.Parameters.AddWithValue("$id", baseRow.Id);
        ins.Parameters.AddWithValue("$days", baseRow.SettlementDays);
        ins.Parameters.AddWithValue("$name", baseRow.Name);
        ins.Parameters.AddWithValue("$api", baseRow.ApiLabel);
        ins.Parameters.AddWithValue("$mov", baseRow.MovementType);
        ins.Parameters.AddWithValue("$active", baseRow.Active ? 1 : 0);
        ins.Parameters.AddWithValue("$pdv", (object?)NullIfEmpty(baseRow.PdvKey) ?? DBNull.Value);
        ins.Parameters.AddWithValue("$sort", baseRow.SortOrder);
        ins.Parameters.AddWithValue("$notes", (object?)NullIfEmpty(baseRow.Notes) ?? DBNull.Value);
        ins.Parameters.AddWithValue("$feeEd", baseRow.FeeEditable ? 1 : 0);
        ins.Parameters.AddWithValue("$dest", baseRow.DestinationKind);
        ins.ExecuteNonQuery();
    }

    private static void InvalidateCache()
    {
        lock (CacheLock)
            _cache = null;
    }

    public static IReadOnlyList<PaymentMethodRow> List(bool? onlyActive = null)
    {
        IReadOnlyList<PaymentMethodRow>? cached;
        lock (CacheLock)
            cached = _cache;

        var all = cached ?? LoadAllFromDb();
        if (cached is null)
        {
            lock (CacheLock)
                _cache ??= all;
        }

        if (onlyActive == true)
            return all.Where(m => m.Active).ToList();
        if (onlyActive == false)
            return all.Where(m => !m.Active).ToList();
        return all;
    }

    private static IReadOnlyList<PaymentMethodRow> LoadAllFromDb()
    {
        using var conn = DatabaseService.OpenConnection();
        EnsureSeeded(conn);
        var accountNames = LoadAccountNames(conn);
        var list = new List<PaymentMethodRow>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT method_id,
                   IFNULL(name, method_id),
                   IFNULL(api_label, method_id),
                   IFNULL(movement_type, 'Entrada'),
                   IFNULL(fee_percent, 0),
                   IFNULL(fee_fixed, 0),
                   COALESCE(settlement_days, 0),
                   bank_account_id,
                   IFNULL(active, 1),
                   IFNULL(pdv_key, ''),
                   IFNULL(notes, ''),
                   IFNULL(fee_editable, 1),
                   IFNULL(is_system, 0),
                   IFNULL(sort_order, 100),
                   IFNULL(destination_kind, 'banco')
            FROM payment_method_fees
            ORDER BY sort_order ASC, name COLLATE NOCASE ASC;
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0).Trim().ToLowerInvariant();
            var bankId = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7);
            var dest = (reader.IsDBNull(14) ? "banco" : reader.GetString(14)).Trim().ToLowerInvariant();
            if (id is "dinheiro")
            {
                dest = "caixa";
                bankId = null;
            }
            else if (id is "fiado")
            {
                dest = "receber";
                bankId = null;
            }

            var bankName = bankId is int bid && accountNames.TryGetValue(bid, out var n) ? n : "";
            list.Add(new PaymentMethodRow
            {
                Id = id,
                Name = reader.GetString(1),
                ApiLabel = reader.GetString(2),
                MovementType = reader.GetString(3),
                FeePercent = Math.Round(reader.GetDouble(4), 4),
                FeeFixed = Math.Round(reader.GetDouble(5), 4),
                SettlementDays = reader.GetInt32(6),
                BankAccountId = bankId,
                BankAccountName = bankName,
                Active = reader.GetInt32(8) != 0,
                PdvKey = reader.IsDBNull(9) ? "" : reader.GetString(9).Trim().ToLowerInvariant(),
                Notes = reader.IsDBNull(10) ? "" : reader.GetString(10),
                FeeEditable = reader.GetInt32(11) != 0,
                IsSystem = reader.GetInt32(12) != 0,
                SortOrder = reader.GetInt32(13),
                DestinationKind = dest is "caixa" or "receber" or "banco" ? dest : "banco",
            });
        }
        return list;
    }

    public static IReadOnlyList<PaymentMethodRow> ListForPdv() =>
        List(onlyActive: true)
            .Where(m => !string.IsNullOrWhiteSpace(m.PdvKey) || m.Id is "dinheiro" or "debito" or "credito" or "pix" or "fiado")
            .ToList();

    public static PaymentMethodRow? GetById(string methodId)
    {
        var key = (methodId ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(key))
            return null;
        return List().FirstOrDefault(m => m.Id == key);
    }

    /// <summary>Mapa rótulo API → % (compatível com código legado).</summary>
    public static Dictionary<string, double> FeeMapByApiLabel()
    {
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in List())
            map[m.ApiLabel] = m.FeePercent;
        return map;
    }

    /// <summary>Mapa rótulo API → taxa % + fixa + prazo + conta destino.</summary>
    public static Dictionary<string, PaymentFeeInfo> FeeInfoByApiLabel()
    {
        var map = new Dictionary<string, PaymentFeeInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in List())
        {
            map[m.ApiLabel] = new PaymentFeeInfo
            {
                FeePercent = m.FeePercent,
                FeeFixed = m.FeeFixed,
                SettlementDays = m.SettlementDays,
                BankAccountId = m.BankAccountId,
                MethodId = m.Id,
            };
        }
        return map;
    }

    public static double CalcFeeAmount(double gross, double feePercent, double feeFixed = 0) =>
        FinancialCalculator.CalculateFeeAmount(gross, feePercent, feeFixed);

    public static PaymentMethodRow Create(PaymentMethodInput input)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("formas de pagamento");
        var name = (input.Name ?? "").Trim().ToUpperInvariant();
        var api = (input.ApiLabel ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Informe o nome da forma de pagamento.");
        if (string.IsNullOrWhiteSpace(api))
            api = ToTitleCaseLabel(name);
        if (api.Length > 40)
            api = api[..40];

        var id = BuildCustomId(name, api);
        if (List().Any(m => m.Id == id))
            throw new InvalidOperationException("Já existe uma forma com este identificador. Escolha outro nome.");
        if (List().Any(m => m.ApiLabel.Equals(api, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Já existe uma forma com este rótulo.");

        var pdvKey = NormalizePdvKey(input.PdvKey);
        EnsurePdvKeyUnique(pdvKey, exceptId: null);

        var dest = NormalizeDestination(input.DestinationKind, id);
        int? bankId = dest == "banco" ? NormalizeBankId(input.BankAccountId) : null;
        if (bankId is int accId)
            EnsureBankAccountExists(accId);

        var fee = Math.Round(Math.Clamp(input.FeePercent, 0, 100), 4);
        var fixedFee = Math.Round(Math.Clamp(input.FeeFixed, 0, 9999.99), 4);
        var days = ClampDays(input.SettlementDays);
        var sort = input.SortOrder ?? NextSortOrder();

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO payment_method_fees (
                method_id, fee_percent, settlement_days, fee_fixed, bank_account_id,
                name, api_label, movement_type, active, pdv_key, sort_order, notes,
                fee_editable, is_system, destination_kind, created_at, updated_at
            ) VALUES (
                $id, $fee, $days, $fixed, $bank,
                $name, $api, $mov, $active, $pdv, $sort, $notes,
                $feeEd, 0, $dest, datetime('now'), datetime('now')
            );
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$fee", fee);
        cmd.Parameters.AddWithValue("$days", days);
        cmd.Parameters.AddWithValue("$fixed", fixedFee);
        cmd.Parameters.AddWithValue("$bank", bankId is int b ? b : DBNull.Value);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$api", api);
        cmd.Parameters.AddWithValue("$mov", string.IsNullOrWhiteSpace(input.MovementType) ? "Entrada" : input.MovementType.Trim());
        cmd.Parameters.AddWithValue("$active", input.Active ? 1 : 0);
        cmd.Parameters.AddWithValue("$pdv", (object?)NullIfEmpty(pdvKey) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sort", sort);
        cmd.Parameters.AddWithValue("$notes", (object?)NullIfEmpty(input.Notes?.Trim()) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$feeEd", input.FeeEditable ? 1 : 0);
        cmd.Parameters.AddWithValue("$dest", dest);
        cmd.ExecuteNonQuery();

        InvalidateCache();
        return GetById(id)!;
    }

    public static PaymentMethodRow Save(PaymentMethodInput input)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("formas de pagamento");
        var key = (input.Id ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(key))
            return Create(input);

        var current = GetById(key)
            ?? throw new InvalidOperationException("Forma de pagamento não encontrada.");

        var name = (input.Name ?? "").Trim().ToUpperInvariant();
        var api = (input.ApiLabel ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Informe o nome da forma de pagamento.");
        if (string.IsNullOrWhiteSpace(api))
            api = current.ApiLabel;
        if (api.Length > 40)
            api = api[..40];

        if (List().Any(m => m.Id != key && m.ApiLabel.Equals(api, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Já existe outra forma com este rótulo.");

        var pdvKey = NormalizePdvKey(input.PdvKey);
        EnsurePdvKeyUnique(pdvKey, exceptId: key);

        var dest = current.DestinationLocked
            ? current.DestinationKind
            : NormalizeDestination(input.DestinationKind, key);
        int? bankId = dest == "banco" ? NormalizeBankId(input.BankAccountId) : null;
        if (bankId is int accId)
            EnsureBankAccountExists(accId);

        var feeEditable = current.IsSystem ? current.FeeEditable : input.FeeEditable;
        var fee = feeEditable
            ? Math.Round(Math.Clamp(input.FeePercent, 0, 100), 4)
            : 0;
        var fixedFee = feeEditable
            ? Math.Round(Math.Clamp(input.FeeFixed, 0, 9999.99), 4)
            : 0;
        var days = ClampDays(input.SettlementDays);

        // Sistema: nome/rótulo/tipo podem ser editados, mas id permanece.
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE payment_method_fees SET
                name = $name,
                api_label = $api,
                movement_type = $mov,
                fee_percent = $fee,
                fee_fixed = $fixed,
                settlement_days = $days,
                bank_account_id = $bank,
                active = $active,
                pdv_key = $pdv,
                notes = $notes,
                fee_editable = $feeEd,
                destination_kind = $dest,
                updated_at = datetime('now')
            WHERE method_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", key);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$api", api);
        cmd.Parameters.AddWithValue("$mov", string.IsNullOrWhiteSpace(input.MovementType) ? current.MovementType : input.MovementType.Trim());
        cmd.Parameters.AddWithValue("$fee", fee);
        cmd.Parameters.AddWithValue("$fixed", fixedFee);
        cmd.Parameters.AddWithValue("$days", days);
        cmd.Parameters.AddWithValue("$bank", bankId is int b ? b : DBNull.Value);
        cmd.Parameters.AddWithValue("$active", input.Active ? 1 : 0);
        cmd.Parameters.AddWithValue("$pdv", (object?)NullIfEmpty(pdvKey) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)NullIfEmpty(input.Notes?.Trim()) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$feeEd", feeEditable ? 1 : 0);
        cmd.Parameters.AddWithValue("$dest", dest);
        cmd.ExecuteNonQuery();

        InvalidateCache();
        return GetById(key)!;
    }

    public static PaymentMethodRow UpdateSettings(
        string methodId,
        double feePercent,
        int settlementDays,
        double feeFixed = 0,
        int? bankAccountId = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("formas de pagamento");
        var current = GetById(methodId)
            ?? throw new InvalidOperationException("Forma de pagamento inválida.");
        return Save(new PaymentMethodInput
        {
            Id = current.Id,
            Name = current.Name,
            ApiLabel = current.ApiLabel,
            MovementType = current.MovementType,
            FeePercent = feePercent,
            FeeFixed = feeFixed,
            SettlementDays = settlementDays,
            BankAccountId = bankAccountId ?? current.BankAccountId,
            Active = current.Active,
            PdvKey = current.PdvKey,
            Notes = current.Notes,
            FeeEditable = current.FeeEditable,
            DestinationKind = current.DestinationKind,
            SortOrder = current.SortOrder,
        });
    }

    public static PaymentMethodRow UpdateFee(string methodId, double feePercent)
    {
        var current = GetById(methodId)
            ?? throw new InvalidOperationException("Forma de pagamento inválida.");
        if (!current.FeeEditable)
            throw new InvalidOperationException("Esta forma não permite editar a taxa.");
        return UpdateSettings(methodId, feePercent, current.SettlementDays, current.FeeFixed, current.BankAccountId);
    }

    public static PaymentMethodRow SetActive(string methodId, bool active)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("formas de pagamento");
        var current = GetById(methodId)
            ?? throw new InvalidOperationException("Forma de pagamento inválida.");
        if (current.Id is "dinheiro" && !active)
            throw new InvalidOperationException("Não é possível inativar Dinheiro — é a forma padrão do PDV.");

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE payment_method_fees SET active = $a, updated_at = datetime('now') WHERE method_id = $id;";
        cmd.Parameters.AddWithValue("$a", active ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", current.Id);
        cmd.ExecuteNonQuery();
        InvalidateCache();
        return GetById(current.Id)!;
    }

    public static void Delete(string methodId)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("formas de pagamento");
        var current = GetById(methodId)
            ?? throw new InvalidOperationException("Forma de pagamento inválida.");
        if (current.IsSystem)
            throw new InvalidOperationException("Formas do sistema não podem ser excluídas. Use Inativar.");

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM payment_method_fees WHERE method_id = $id AND IFNULL(is_system,0) = 0;";
        cmd.Parameters.AddWithValue("$id", current.Id);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Não foi possível excluir esta forma.");
        InvalidateCache();
    }

    public static void MoveOrder(string methodId, int direction)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("formas de pagamento");
        var list = List().ToList();
        var idx = list.FindIndex(m => m.Id.Equals(methodId, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return;
        var swap = idx + Math.Sign(direction);
        if (swap < 0 || swap >= list.Count) return;

        var a = list[idx];
        var b = list[swap];
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        SetSort(conn, tx, a.Id, b.SortOrder);
        SetSort(conn, tx, b.Id, a.SortOrder);
        tx.Commit();
        InvalidateCache();
    }

    /// <summary>Normaliza texto de pagamento para o rótulo oficial (ApiLabel).</summary>
    public static string NormalizeToApiLabel(string? paymentType)
    {
        var s = (paymentType ?? "Dinheiro").Trim();
        if (string.IsNullOrEmpty(s))
            return "Dinheiro";

        var low = s.ToLowerInvariant();
        // Atalhos de tecla e ids legados
        var byKey = List().FirstOrDefault(m =>
            (!string.IsNullOrEmpty(m.PdvKey) && m.PdvKey == low)
            || m.Id == low
            || m.ApiLabel.Equals(s, StringComparison.OrdinalIgnoreCase)
            || m.Name.Equals(s, StringComparison.OrdinalIgnoreCase));
        if (byKey is not null)
            return byKey.ApiLabel;

        return low switch
        {
            "dinheiro" or "cash" or "a" => "Dinheiro",
            "pix" or "d" => "Pix",
            "cartão débito" or "cartao debito" or "debito" or "b" => "Cartão Débito",
            "cartão crédito" or "cartao credito" or "credito" or "c" => "Cartão Crédito",
            "fiado" or "e" or "à prazo" or "a prazo" => "Fiado",
            _ => s.Length > 40 ? s[..40] : s,
        };
    }

    public static bool IsFiadoLabel(string? paymentType)
    {
        var label = NormalizeToApiLabel(paymentType);
        var m = List().FirstOrDefault(x => x.ApiLabel.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (m is not null)
            return m.Id == "fiado" || m.DestinationKind == "receber";
        return label.Contains("fiado", StringComparison.OrdinalIgnoreCase)
            || label.Contains("prazo", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDinheiroLabel(string? paymentType)
    {
        var label = NormalizeToApiLabel(paymentType);
        var m = List().FirstOrDefault(x => x.ApiLabel.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (m is not null)
            return m.Id == "dinheiro" || m.DestinationKind == "caixa";
        return label.Equals("Dinheiro", StringComparison.OrdinalIgnoreCase);
    }

    private static void SetSort(SqliteConnection conn, SqliteTransaction tx, string id, int sort)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE payment_method_fees SET sort_order = $s, updated_at = datetime('now') WHERE method_id = $id;";
        cmd.Parameters.AddWithValue("$s", sort);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static int NextSortOrder()
    {
        var max = List().Select(m => m.SortOrder).DefaultIfEmpty(0).Max();
        return max + 10;
    }

    private static string BuildCustomId(string name, string api)
    {
        var raw = Regex.Replace((api + " " + name).ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
        if (string.IsNullOrEmpty(raw))
            raw = "custom";
        if (raw.Length > 28)
            raw = raw[..28].Trim('_');
        if (!raw.StartsWith("custom_", StringComparison.Ordinal))
            raw = "custom_" + raw;
        var baseId = raw;
        var n = 2;
        while (List().Any(m => m.Id == raw))
        {
            raw = $"{baseId}_{n}";
            n++;
        }
        return raw;
    }

    private static string ToTitleCaseLabel(string upperName)
    {
        var ti = CultureInfo.GetCultureInfo("pt-BR").TextInfo;
        return ti.ToTitleCase(upperName.ToLowerInvariant());
    }

    private static string NormalizePdvKey(string? key)
    {
        var k = (key ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(k))
            return "";
        // Uma letra A–Z
        if (k.Length == 1 && k[0] is >= 'a' and <= 'z')
            return k;
        throw new InvalidOperationException("Tecla PDV deve ser uma letra de A a Z (ou vazia).");
    }

    private static void EnsurePdvKeyUnique(string pdvKey, string? exceptId)
    {
        if (string.IsNullOrEmpty(pdvKey))
            return;
        var clash = List().FirstOrDefault(m =>
            m.PdvKey == pdvKey
            && !m.Id.Equals(exceptId, StringComparison.OrdinalIgnoreCase));
        if (clash is not null)
            throw new InvalidOperationException(
                $"A tecla '{pdvKey.ToUpperInvariant()}' já está em uso por {clash.Name}.");
    }

    private static string NormalizeDestination(string? kind, string methodId)
    {
        if (methodId is "dinheiro") return "caixa";
        if (methodId is "fiado") return "receber";
        var k = (kind ?? "banco").Trim().ToLowerInvariant();
        return k is "caixa" or "receber" or "banco" ? k : "banco";
    }

    private static int? NormalizeBankId(int? id) =>
        id is null or <= 0 ? null : id;

    private static int ClampDays(int days)
    {
        if (days < 0)
            throw new InvalidOperationException("Prazo de recebimento não pode ser negativo.");
        if (days > 365)
            throw new InvalidOperationException("Prazo de recebimento máximo é 365 dias.");
        return days;
    }

    private static void EnsureBankAccountExists(int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM bank_accounts WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteScalar() is null)
            throw new InvalidOperationException("Conta bancária de destino não encontrada.");
    }

    private static Dictionary<int, string> LoadAccountNames(SqliteConnection conn)
    {
        var map = new Dictionary<int, string>();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name FROM bank_accounts ORDER BY name;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                map[reader.GetInt32(0)] = reader.GetString(1);
        }
        catch
        {
            /* tabela pode não existir ainda */
        }
        return map;
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;
}

public sealed class PaymentFeeInfo
{
    public string MethodId { get; init; } = "";
    public double FeePercent { get; init; }
    public double FeeFixed { get; init; }
    public int SettlementDays { get; init; }
    public int? BankAccountId { get; init; }
}
