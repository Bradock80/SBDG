using System.Globalization;
using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public static class CashService
{
  private const string CalcVersion = "2026-06-22-tz";

  private static readonly HashSet<CashMovementKind> OperacaoKinds =
  [
    CashMovementKind.Abertura,
    CashMovementKind.Fechamento,
    CashMovementKind.Venda,
    CashMovementKind.VendaFiado,
    CashMovementKind.RecebimentoFiado,
    CashMovementKind.Compra,
    CashMovementKind.Sangria,
    CashMovementKind.Suprimento,
    CashMovementKind.Troca,
  ];

  public static bool IsOperational(DateTime? sessionDate = null)
  {
    using var conn = DatabaseService.OpenConnection();
    return GetOperationalStatus(conn, sessionDate ?? DateTime.Today).IsOperational;
  }

  /// <summary>
  /// Período de vendas do PDV: com caixa aberto (mesmo de ontem), vai da data do turno até hoje.
  /// Sem caixa aberto, retorna só o dia informado (padrão: hoje).
  /// </summary>
  public static (DateTime From, DateTime To, bool CarriedOver, bool IsOpen) GetPdvSalesDateRange(
    DateTime? asOf = null)
  {
    var d = (asOf ?? DateTime.Today).Date;
    using var conn = DatabaseService.OpenConnection();
    var op = GetOperationalStatusFull(conn, d);
    if (op.IsOperational)
      return (op.WorkDate.Date, d, op.CarriedOver, true);
    return (d, d, false, false);
  }

  /// <summary>
  /// Data do turno operacional ainda aberto (pode ser ontem se o caixa atravessou a meia-noite).
  /// Null quando não há caixa aberto.
  /// </summary>
  public static DateTime? GetOpenWorkDate(DateTime? asOf = null)
  {
    using var conn = DatabaseService.OpenConnection();
    var op = GetOperationalStatusFull(conn, (asOf ?? DateTime.Today).Date);
    return op.IsOperational ? op.WorkDate.Date : null;
  }

  public static void RequireOperational(DateTime? sessionDate = null)
  {
    using var conn = DatabaseService.OpenConnection();
    RequireOperational(conn, sessionDate ?? DateTime.Today);
  }

  internal static void RequireOperational(SqliteConnection conn, DateTime d)
  {
    var status = GetOperationalStatus(conn, d);
    if (!status.IsOperational)
      throw new CashOperationException("Abra o caixa antes de continuar (menu Caixa).");
  }

  internal static void AddSaleMovement(
    SqliteConnection conn, SqliteTransaction tx, DateTime sessionDate, int saleId,
    CashMovementKind kind, string description, string paymentType,
    double amountIn, string? customerName, bool affectsBalance, string? notes = null)
  {
    AddSalePaymentMovement(conn, tx, sessionDate, saleId, kind, description,
      paymentType, amountIn, customerName, affectsBalance, notes);
  }

  internal static void AddSalePaymentMovement(
    SqliteConnection conn, SqliteTransaction tx, DateTime sessionDate, int saleId,
    CashMovementKind kind, string description, string paymentType,
    double amountIn, string? customerName, bool affectsBalance, string? notes = null)
  {
    AddMovement(conn, tx, sessionDate, kind, description,
      partyName: customerName, paymentType: paymentType,
      amountIn: amountIn, amountOut: 0, affectsBalance: affectsBalance,
      refType: "sale", refId: saleId, notes: notes, allowDuplicateRef: true);
  }

  internal static void AddExchangeMovement(
    SqliteConnection conn, SqliteTransaction tx, DateTime sessionDate, int exchangeId,
    string description, string paymentType, double amountIn, double amountOut,
    string? customerName = null, string? notes = null)
  {
    AddMovement(conn, tx, sessionDate, CashMovementKind.Troca, description,
      partyName: customerName, paymentType: paymentType,
      amountIn: amountIn, amountOut: amountOut, affectsBalance: true,
      refType: "sale_exchange", refId: exchangeId, notes: notes, allowDuplicateRef: true);
  }

  internal static void DeleteSaleMovements(SqliteConnection conn, SqliteTransaction tx, int saleId)
  {
    using var del = conn.CreateCommand();
    del.Transaction = tx;
    del.CommandText = "DELETE FROM cash_movements WHERE ref_type = 'sale' AND ref_id = $id;";
    del.Parameters.AddWithValue("$id", saleId);
    del.ExecuteNonQuery();
  }

  public static CashOperacaoView GetOperacaoView(DateTime? sessionDate = null)
  {
    var d = (sessionDate ?? DateTime.Today).Date;
    using var conn = DatabaseService.OpenConnection();
    SyncCashFromPayables(conn, d);
    return BuildOperacaoView(conn, d);
  }

  public static CaixaHistoricoListResult ListCaixaHistorico(
    int limit = 30,
    DateTime? dateFrom = null,
    DateTime? dateTo = null,
    string modo = "recentes")
  {
    var mode = (modo ?? "recentes").Trim().ToLowerInvariant();
    if (mode != "periodo")
      mode = "recentes";

    var lim = Math.Clamp(limit, 1, 500);
    using var conn = DatabaseService.OpenConnection();
    using var cmd = conn.CreateCommand();
    var sql = """
      SELECT id, session_date, opening_amount, status, closed_at, notes, counted_amount,
             difference_amount, opened_by_user_id, opened_by_user_name,
             closed_by_user_id, closed_by_user_name
      FROM cash_sessions
      """;
    if (mode == "periodo")
    {
      sql += " WHERE 1=1";
      if (dateFrom is DateTime df)
        sql += " AND session_date >= $from";
      if (dateTo is DateTime dt)
        sql += " AND session_date <= $to";
    }
    sql += " ORDER BY session_date DESC, id DESC;";
    cmd.CommandText = sql;
    if (mode == "periodo")
    {
      if (dateFrom is DateTime df)
        cmd.Parameters.AddWithValue("$from", df.Date.ToString("yyyy-MM-dd"));
      if (dateTo is DateTime dt)
        cmd.Parameters.AddWithValue("$to", dt.Date.ToString("yyyy-MM-dd"));
    }

    var sessions = new List<CashSessionRecord>();
    using (var reader = cmd.ExecuteReader())
    {
      while (reader.Read())
      {
        sessions.Add(MapSession(reader));
      }
    }

    var cycles = new List<(CaixaHistoricoRow Row, DateTime OpenedAt)>();
    foreach (var session in sessions)
    {
      var movs = LoadMovements(conn, session.Id);
      if (movs.Count == 0)
        continue;
      foreach (var cycle in CyclesFromMovements(movs, session))
        cycles.Add(cycle);
    }

    cycles.Sort((a, b) => b.OpenedAt.CompareTo(a.OpenedAt));
    var display = cycles.Take(lim).Select(c => c.Row).ToList();
    return new CaixaHistoricoListResult
    {
      Rows = display,
      Registros = display.Count,
      Modo = mode,
      Limit = lim,
    };
  }

  public static CaixaHistoricoDetail? GetCaixaHistoricoDetail(int aberturaId)
  {
    using var conn = DatabaseService.OpenConnection();
    using var find = conn.CreateCommand();
    find.CommandText = """
      SELECT id, session_id, kind, amount_in, notes, created_at
      FROM cash_movements WHERE id = $id LIMIT 1;
      """;
    find.Parameters.AddWithValue("$id", aberturaId);
    using var reader = find.ExecuteReader();
    if (!reader.Read())
      return null;
    if (ParseKind(reader.GetString(2)) != CashMovementKind.Abertura)
      return null;

    var abId = reader.GetInt32(0);
    var sessionId = reader.GetInt32(1);
    var abAmountIn = reader.GetDouble(3);
    var abNotes = reader.IsDBNull(4) ? null : reader.GetString(4);
    var abCreated = ParseDateTime(reader.GetString(5));
    reader.Close();

    using var sessCmd = conn.CreateCommand();
    sessCmd.CommandText = """
      SELECT id, session_date, opening_amount, status, closed_at, notes, counted_amount,
             difference_amount, opened_by_user_id, opened_by_user_name,
             closed_by_user_id, closed_by_user_name
      FROM cash_sessions WHERE id = $id LIMIT 1;
      """;
    sessCmd.Parameters.AddWithValue("$id", sessionId);
    using var sessReader = sessCmd.ExecuteReader();
    if (!sessReader.Read())
      return null;
    var session = MapSession(sessReader);
    sessReader.Close();

    var movs = LoadMovements(conn, session.Id);
    var ordered = movs.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToList();
    var startIdx = ordered.FindIndex(m => m.Id == abId);
    if (startIdx < 0)
      return null;

    var cicloMovs = new List<CashMovementRecord> { ordered[startIdx] };
    CashMovementRecord? fech = null;
    for (var i = startIdx + 1; i < ordered.Count; i++)
    {
      var m = ordered[i];
      if (m.Kind == CashMovementKind.Abertura)
        break;
      cicloMovs.Add(m);
      if (m.Kind == CashMovementKind.Fechamento)
      {
        fech = m;
        break;
      }
    }

    var cicloCalc = cicloMovs.Where(m => m.Kind != CashMovementKind.Fechamento).ToList();
    var totals = CalcSessionTotals(cicloCalc, abAmountIn);
    var activeAb = ActiveAbertura(movs);
    var isOpen = fech is null && activeAb is not null && activeAb.Id == abId;

    var rows = new List<CashMovementRow>();
    foreach (var m in cicloMovs)
    {
      double entrada = 0, saida = 0;
      if (m.Kind is not (CashMovementKind.Fechamento or CashMovementKind.VendaFiado))
      {
        entrada = m.AmountIn;
        saida = m.AmountOut;
      }
      rows.Add(new CashMovementRow
      {
        Id = m.Id,
        DateTimeDisplay = FormatBrDateTime(m.CreatedAt, "dd/MM/yy HH:mm:ss"),
        Historico = HistoricoText(m),
        EntradaDisplay = entrada > 0 ? ProductPriceHelper.MoneyBr(entrada) : "",
        SaidaDisplay = saida > 0 ? ProductPriceHelper.MoneyBr(saida) : "",
        FormaPagto = NormalizeFormaPagto(m.PaymentType),
        Kind = m.Kind.ToString().ToLowerInvariant(),
      });
    }

    var obs = (abNotes ?? "").Trim();
    if (string.IsNullOrEmpty(obs))
      obs = (session.Notes ?? "").Trim();

    double? saldoInformado = null;
    double? difference = null;
    if (fech is not null && !string.IsNullOrEmpty(session.ClosedAt) && session.CountedAmount is not null)
    {
      var closedAt = ParseDateTime(session.ClosedAt);
      if (Math.Abs((fech.CreatedAt - closedAt).TotalSeconds) < 5)
      {
        saldoInformado = session.CountedAmount;
        difference = session.DifferenceAmount
          ?? Round(session.CountedAmount.Value - totals.SaldoFinalGaveta);
      }
    }

    var operatorName = isOpen
      ? (session.OpenedByUserName ?? "")
      : (session.ClosedByUserName ?? session.OpenedByUserName ?? "");

    return new CaixaHistoricoDetail
    {
      Id = abId,
      SessionId = session.Id,
      IsOpen = isOpen,
      SaldoInicial = totals.SaldoInicial,
      EntradasCaixa = totals.EntradasCaixa,
      SaidasCaixa = totals.SaidasCaixa,
      SaldoFinal = totals.SaldoFinal,
      SaldoFinalGaveta = totals.SaldoFinalGaveta,
      SaldoInformado = saldoInformado,
      DifferenceAmount = difference,
      OperatorName = operatorName,
      EntradasPorForma = totals.EntradasPorForma,
      OpeningObs = obs,
      OpenedAtBr = FormatBrDateTime(abCreated, "dd/MM/yyyy"),
      OpenedTimeBr = FormatBrDateTime(abCreated, "HH:mm"),
      ClosedAtBr = fech is not null ? FormatBrDateTime(fech.CreatedAt, "dd/MM/yyyy HH:mm") : "",
      Rows = rows,
    };
  }

  private static List<(CaixaHistoricoRow Row, DateTime OpenedAt)> CyclesFromMovements(
    List<CashMovementRecord> movs, CashSessionRecord session)
  {
    var ordered = movs.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToList();
    var activeAb = ActiveAbertura(ordered);
    var cycles = new List<(CaixaHistoricoRow Row, DateTime OpenedAt)>();
    var i = 0;
    while (i < ordered.Count)
    {
      if (ordered[i].Kind != CashMovementKind.Abertura)
      {
        i++;
        continue;
      }
      var ab = ordered[i];
      i++;
      var cicloMovs = new List<CashMovementRecord> { ab };
      CashMovementRecord? fech = null;
      while (i < ordered.Count)
      {
        var m = ordered[i];
        if (m.Kind == CashMovementKind.Abertura)
          break;
        cicloMovs.Add(m);
        if (m.Kind == CashMovementKind.Fechamento)
        {
          fech = m;
          i++;
          break;
        }
        i++;
      }

      var cicloCalc = cicloMovs.Where(m => m.Kind != CashMovementKind.Fechamento).ToList();
      var totals = CalcSessionTotals(cicloCalc, ab.AmountIn);
      var isOpen = fech is null && activeAb is not null && activeAb.Id == ab.Id;

      double? saldoInformado = null;
      double? difference = null;
      if (fech is not null && !string.IsNullOrEmpty(session.ClosedAt) && session.CountedAmount is not null)
      {
        var closedAt = ParseDateTime(session.ClosedAt);
        if (Math.Abs((fech.CreatedAt - closedAt).TotalSeconds) < 5)
        {
          saldoInformado = session.CountedAmount;
          // Diferença = Saldo Final Informado − Saldo Final Previsto
          difference = session.DifferenceAmount
            ?? Round(session.CountedAmount.Value - totals.SaldoFinalGaveta);
        }
      }

      var obs = (ab.Notes ?? "").Trim();
      if (string.IsNullOrEmpty(obs))
        obs = (session.Notes ?? "").Trim();

      var operatorName = isOpen
        ? (session.OpenedByUserName ?? "")
        : (session.ClosedByUserName ?? session.OpenedByUserName ?? "");

      cycles.Add((new CaixaHistoricoRow
      {
        Id = ab.Id,
        SessionId = session.Id,
        OpenedAtBr = FormatBrDateTime(ab.CreatedAt, "dd/MM/yy HH:mm"),
        ClosedAtBr = fech is not null ? FormatBrDateTime(fech.CreatedAt, "dd/MM/yy HH:mm") : "",
        SaldoInicial = Round(ab.AmountIn),
        SaldoFinal = totals.SaldoFinalGaveta,
        SaldoInformado = saldoInformado,
        DifferenceAmount = difference,
        OperatorName = operatorName,
        Observacao = obs.Length > 80 ? obs[..80] : obs,
        IsOpen = isOpen,
      }, ab.CreatedAt));
    }
    return cycles;
  }

  public static void OpenSession(double openingAmount, string? notes, DateTime? sessionDate = null)
  {
    StoreNetworkMode.EnsureLocalMutationAllowed("abrir caixa");
    var d = (sessionDate ?? DateTime.Today).Date;
    int sessionId;
    var reopening = false;
    using var conn = DatabaseService.OpenConnection();
    using var tx = conn.BeginTransaction();
    (sessionId, reopening) = OpenSessionCore(conn, tx, d, openingAmount, notes);
    tx.Commit();

    var summary = reopening
        ? $"Caixa reaberto com troco de R$ {openingAmount:N2}"
        : $"Caixa aberto com troco de R$ {openingAmount:N2}";
    AuditService.LogJson("abrir", "caixa", sessionId.ToString(),
        AuditPayloadBuilder.CashOpen(openingAmount, sessionId, reopening, notes, d), summary);
  }

  public static void AddSangria(double amount, string notes, DateTime? sessionDate = null)
  {
    StoreNetworkMode.EnsureLocalMutationAllowed("sangria");
    if (string.IsNullOrWhiteSpace(notes))
      throw new CashOperationException("Informe o motivo da sangria.");

    var d = (sessionDate ?? DateTime.Today).Date;
    using var conn = DatabaseService.OpenConnection();
    RequireOperational(conn, d);
    using var tx = conn.BeginTransaction();
    var desc = notes.Trim().ToUpperInvariant();
    AddMovement(conn, tx, d, CashMovementKind.Sangria, desc,
      paymentType: "Dinheiro", amountOut: amount, notes: notes);
    tx.Commit();

    AuditService.LogJson("sangria", "caixa", null,
        AuditPayloadBuilder.CashSangria(amount, notes.Trim(), d),
        $"Sangria de R$ {amount:N2} — {notes.Trim()}");
  }

  public static void AddSuprimento(double amount, string? notes, DateTime? sessionDate = null)
  {
    StoreNetworkMode.EnsureLocalMutationAllowed("suprimento");
    var d = (sessionDate ?? DateTime.Today).Date;
    using var conn = DatabaseService.OpenConnection();
    RequireOperational(conn, d);
    using var tx = conn.BeginTransaction();
    var desc = (notes ?? "SUPRIMENTO").Trim().ToUpperInvariant();
    AddMovement(conn, tx, d, CashMovementKind.Suprimento, desc,
      paymentType: "Dinheiro", amountIn: amount, notes: notes);
    tx.Commit();

    AuditService.LogJson("suprimento", "caixa", null,
        AuditPayloadBuilder.CashSuprimento(amount, notes?.Trim(), d),
        $"Suprimento de R$ {amount:N2}" + (string.IsNullOrWhiteSpace(notes) ? "" : $" — {notes.Trim()}"));
  }

  public static void RegisterDespesa(
    double amount,
    string paymentType,
    string category,
    int supplierId,
    string supplierName,
    string? notes,
    DateTime? sessionDate = null)
  {
    StoreNetworkMode.EnsureLocalMutationAllowed("despesa no caixa");
    if (amount <= 0)
      throw new CashOperationException("Informe um valor maior que zero.");
    if (supplierId <= 0)
      throw new CashOperationException("Selecione o fornecedor.");

    var d = (sessionDate ?? DateTime.Today).Date;
    var iso = d.ToString("yyyy-MM-dd");
    var forma = NormalizeFormaPagto(paymentType);
    var historico = (notes ?? supplierName).Trim().ToUpperInvariant();
    if (string.IsNullOrWhiteSpace(historico))
      historico = supplierName.Trim().ToUpperInvariant();

    using var conn = DatabaseService.OpenConnection();
    RequireOperational(conn, d);
    using var tx = conn.BeginTransaction();

    int titleId;
    using (var insTitle = conn.CreateCommand())
    {
      insTitle.Transaction = tx;
      insTitle.CommandText = """
        INSERT INTO payable_titles (
          supplier_id, number, emission_date, total_amount, expense_category, notes
        ) VALUES ($supplier, $number, $emission, $total, $cat, $notes);
        SELECT last_insert_rowid();
        """;
      insTitle.Parameters.AddWithValue("$supplier", supplierId);
      insTitle.Parameters.AddWithValue("$number", $"CX-{DateTime.Now:yyyyMMddHHmmss}");
      insTitle.Parameters.AddWithValue("$emission", iso);
      insTitle.Parameters.AddWithValue("$total", amount);
      insTitle.Parameters.AddWithValue("$cat", category);
      insTitle.Parameters.AddWithValue("$notes", notes ?? "Lançamento via caixa");
      titleId = Convert.ToInt32(insTitle.ExecuteScalar());
    }

    int instId;
    using (var insInst = conn.CreateCommand())
    {
      insInst.Transaction = tx;
      insInst.CommandText = """
        INSERT INTO payable_installments (
          title_id, seq, due_date, amount, payment_type, status, paid_amount, paid_date
        ) VALUES ($title, 1, $due, $amount, $pay, 'pago', $amount, $due);
        SELECT last_insert_rowid();
        """;
      insInst.Parameters.AddWithValue("$title", titleId);
      insInst.Parameters.AddWithValue("$due", iso);
      insInst.Parameters.AddWithValue("$amount", amount);
      insInst.Parameters.AddWithValue("$pay", forma);
      instId = Convert.ToInt32(insInst.ExecuteScalar());
    }

    AddMovement(conn, tx, d, CashMovementKind.Compra, historico,
      partyName: supplierName, paymentType: forma, amountOut: amount,
      refType: "payable_installment", refId: instId, notes: notes);

    tx.Commit();
  }

  public static void CloseSession(double countedAmount, string? notes, DateTime? sessionDate = null)
  {
    StoreNetworkMode.EnsureLocalMutationAllowed("fechar caixa");
    var d = (sessionDate ?? DateTime.Today).Date;
    int sessionId;
    double expected;
    double difference;
    var operatorId = AppSession.CurrentUser?.Id;
    var operatorName = AppSession.CurrentUser?.Nome;
    using var conn = DatabaseService.OpenConnection();
    using var tx = conn.BeginTransaction();
    (sessionId, expected, difference) = CloseSessionCore(conn, tx, d, countedAmount, notes, operatorId, operatorName);
    tx.Commit();

    var diffText = difference == 0
        ? "sem diferença"
        : difference > 0
            ? $"sobra de R$ {difference:N2}"
            : $"falta de R$ {Math.Abs(difference):N2}";
    AuditService.LogJson("fechar", "caixa", sessionId.ToString(),
        AuditPayloadBuilder.CashClose(sessionId, expected, countedAmount, difference, notes, d, operatorId, operatorName),
        $"Caixa fechado — esperado R$ {expected:N2}, informado R$ {countedAmount:N2} ({diffText})");

    BackupSchedulerService.TryBackupOnCashClose();
  }

  /// <summary>Lista valores aguardando depósito (conferência simples: dia + valor).</summary>
  public static IReadOnlyList<CashDepositRow> ListDepositAwaits(bool onlyPending = true)
  {
    using var conn = DatabaseService.OpenConnection();
    using var cmd = conn.CreateCommand();
    var sql = """
      SELECT id, deposit_date, amount, IFNULL(status, 'pendente'),
             confirmed_amount, confirmed_at, notes
      FROM deposit_awaits
      WHERE 1 = 1
      """;
    if (onlyPending)
      sql += " AND IFNULL(status, 'pendente') = 'pendente'";
    sql += " ORDER BY deposit_date DESC, id DESC LIMIT 400;";
    cmd.CommandText = sql;

    var list = new List<CashDepositRow>();
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
      list.Add(new CashDepositRow
      {
        Id = reader.GetInt32(0),
        DepositDate = reader.GetString(1),
        Amount = reader.GetDouble(2),
        Status = reader.IsDBNull(3) ? "pendente" : reader.GetString(3),
        ConfirmedAmount = reader.IsDBNull(4) ? null : reader.GetDouble(4),
        ConfirmedAt = reader.IsDBNull(5) ? null : reader.GetString(5),
        Notes = reader.IsDBNull(6) ? null : reader.GetString(6),
      });
    }
    return list;
  }

  public static int AddDepositAwait(DateTime date, double amount, string? notes = null)
  {
    StoreNetworkMode.EnsureLocalMutationAllowed("depósito de caixa");
    amount = Math.Max(0, Round(amount));
    if (amount < 0.009)
      throw new CashOperationException("Informe o valor aguardando depósito.");

    using var conn = DatabaseService.OpenConnection();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
      INSERT INTO deposit_awaits (deposit_date, amount, status, notes, created_at)
      VALUES ($date, $amt, 'pendente', $notes, $created);
      SELECT last_insert_rowid();
      """;
    cmd.Parameters.AddWithValue("$date", date.Date.ToString("yyyy-MM-dd"));
    cmd.Parameters.AddWithValue("$amt", amount);
    cmd.Parameters.AddWithValue("$notes", string.IsNullOrWhiteSpace(notes) ? DBNull.Value : notes.Trim());
    cmd.Parameters.AddWithValue("$created", DateBrHelper.NowUtcIso());
    var id = Convert.ToInt32(cmd.ExecuteScalar());
    AuditService.Log("criar", "deposito_aguarda", id.ToString(),
      $"Aguardando depósito R$ {amount:N2} em {date:dd/MM/yyyy}");
    return id;
  }

  public static void ConfirmDepositAwait(int id, double confirmedAmount, string? notes = null)
  {
    StoreNetworkMode.EnsureLocalMutationAllowed("depósito de caixa");
    confirmedAmount = Math.Max(0, Round(confirmedAmount));
    using var conn = DatabaseService.OpenConnection();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
      SELECT IFNULL(amount, 0), IFNULL(status, 'pendente')
      FROM deposit_awaits
      WHERE id = $id
      LIMIT 1;
      """;
    cmd.Parameters.AddWithValue("$id", id);
    using var reader = cmd.ExecuteReader();
    if (!reader.Read())
      throw new CashOperationException("Lançamento não encontrado.");
    var expected = reader.GetDouble(0);
    reader.Close();

    var newStatus = Math.Abs(confirmedAmount - expected) < 0.02 ? "depositado" : "divergente";
    using var upd = conn.CreateCommand();
    upd.CommandText = """
      UPDATE deposit_awaits
      SET status = $st,
          confirmed_amount = $amt,
          confirmed_at = $at,
          notes = CASE WHEN $notes IS NULL OR TRIM($notes) = '' THEN notes ELSE $notes END
      WHERE id = $id;
      """;
    upd.Parameters.AddWithValue("$st", newStatus);
    upd.Parameters.AddWithValue("$amt", confirmedAmount);
    upd.Parameters.AddWithValue("$at", DateBrHelper.NowUtcIso());
    upd.Parameters.AddWithValue("$notes", string.IsNullOrWhiteSpace(notes) ? DBNull.Value : notes.Trim());
    upd.Parameters.AddWithValue("$id", id);
    upd.ExecuteNonQuery();

    AuditService.Log("conferir", "deposito_aguarda", id.ToString(),
      $"Depósito {newStatus}: esperado R$ {expected:N2}, informado R$ {confirmedAmount:N2}");
  }

  public static void DeleteDepositAwait(int id)
  {
    StoreNetworkMode.EnsureLocalMutationAllowed("depósito de caixa");
    using var conn = DatabaseService.OpenConnection();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "DELETE FROM deposit_awaits WHERE id = $id AND IFNULL(status,'pendente') = 'pendente';";
    cmd.Parameters.AddWithValue("$id", id);
    if (cmd.ExecuteNonQuery() <= 0)
      throw new CashOperationException("Só é possível excluir lançamentos pendentes.");
  }

  public static void DeleteMovement(int movementId)
  {
    StoreNetworkMode.EnsureLocalMutationAllowed("excluir movimento de caixa");
    using var conn = DatabaseService.OpenConnection();
    using var tx = conn.BeginTransaction();

    using var load = conn.CreateCommand();
    load.Transaction = tx;
    load.CommandText = """
      SELECT m.id, m.kind, m.ref_type, m.ref_id, s.status
      FROM cash_movements m
      JOIN cash_sessions s ON s.id = m.session_id
      WHERE m.id = $id LIMIT 1;
      """;
    load.Parameters.AddWithValue("$id", movementId);
    using var reader = load.ExecuteReader();
    if (!reader.Read())
      throw new CashOperationException("Movimento não encontrado.");

    var kind = ParseKind(reader.GetString(1));
    var refType = reader.IsDBNull(2) ? "" : reader.GetString(2);
    var refId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
    var status = reader.GetString(4);
    reader.Close();

    if (kind is CashMovementKind.Abertura or CashMovementKind.Fechamento)
      throw new CashOperationException("Não é possível excluir abertura/fechamento.");
    if (refType == "payable_installment")
      throw new CashOperationException("Estorne a baixa em Contas a Pagar.");
    if (refType is "fiado_payment" or "fiado_payment_part")
      throw new CashOperationException("Estorne o recebimento em Fiado.");
    if (refType == "sale" && refId > 0)
      throw new CashOperationException("Use Cancelar venda no PDV ou Relatório.");
    if (status == "fechada")
      throw new CashOperationException("Caixa do dia já encerrado.");

    using var del = conn.CreateCommand();
    del.Transaction = tx;
    del.CommandText = "DELETE FROM cash_movements WHERE id = $id;";
    del.Parameters.AddWithValue("$id", movementId);
    del.ExecuteNonQuery();
    tx.Commit();
  }

  /// <summary>Lança saída no caixa só se a baixa for em Dinheiro.</summary>
  internal static void RegisterPayableCashPayment(
    SqliteConnection conn, SqliteTransaction tx,
    int installmentId, string supplierName, double paidAmount,
    string paidDateIso, string paymentType)
  {
    if (!IsDinheiro(paymentType))
      return;
    if (MovementExists(conn, "payable_installment", installmentId, tx))
      return;

    var paidDate = ParseDate(paidDateIso);
    var historico = string.IsNullOrWhiteSpace(supplierName)
      ? "COMPRA"
      : supplierName.Trim().ToUpperInvariant();
    AddMovement(conn, tx, paidDate, CashMovementKind.Compra, historico,
      partyName: supplierName, paymentType: NormalizeFormaPagto(paymentType),
      amountOut: paidAmount, refType: "payable_installment", refId: installmentId);
  }

  internal static void RemovePayableCashPayment(
    SqliteConnection conn, SqliteTransaction tx, int installmentId)
  {
    using var load = conn.CreateCommand();
    load.Transaction = tx;
    load.CommandText = """
      SELECT m.id, s.status
      FROM cash_movements m
      JOIN cash_sessions s ON s.id = m.session_id
      WHERE m.ref_type = 'payable_installment' AND m.ref_id = $id
      LIMIT 1;
      """;
    load.Parameters.AddWithValue("$id", installmentId);
    using var reader = load.ExecuteReader();
    if (!reader.Read())
      return;
    var movId = reader.GetInt32(0);
    var status = reader.GetString(1);
    reader.Close();

    if (status == "fechada")
      throw new CashOperationException("Caixa do dia já encerrado — não é possível estornar movimento.");

    using var del = conn.CreateCommand();
    del.Transaction = tx;
    del.CommandText = "DELETE FROM cash_movements WHERE id = $id;";
    del.Parameters.AddWithValue("$id", movId);
    del.ExecuteNonQuery();
  }

  internal static void RegisterFiadoRecebimento(
    SqliteConnection conn, SqliteTransaction tx,
    DateTime paymentDate, int paymentId, string customerName,
    IReadOnlyList<(string PaymentType, double Amount)> parts,
    double interestAmount,
    double? cashReceived = null,
    double changeAmount = 0)
  {
    RequireOperational(conn, paymentDate.Date);
    var descBase = $"RECEBIMENTO FIADO — {customerName}";
    if (interestAmount > 0.009)
      descBase += $" (juros R$ {interestAmount:F2})";

    for (var idx = 0; idx < parts.Count; idx++)
    {
      var part = parts[idx];
      var desc = parts.Count > 1
        ? $"{descBase} — {part.PaymentType} R$ {part.Amount:F2}"
        : descBase;
      string? movNotes = null;
      var amountIn = part.Amount;
      var amountOut = 0.0;
      if (part.PaymentType.Equals("Dinheiro", StringComparison.OrdinalIgnoreCase)
          && cashReceived is not null && changeAmount > 0.009)
      {
        // Cliente deu a mais: entra o valor recebido e sai o troco da gaveta.
        amountIn = cashReceived.Value;
        amountOut = changeAmount;
        desc = $"{desc} (recebido R$ {cashReceived:F2}, troco R$ {changeAmount:F2})";
        movNotes =
          $"{{\"cash_received\":{cashReceived.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"change\":{changeAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
      }

      var refType = parts.Count == 1 ? "fiado_payment" : "fiado_payment_part";
      var refId = parts.Count == 1 ? paymentId : paymentId * 100 + idx;
      AddMovement(conn, tx, paymentDate.Date, CashMovementKind.RecebimentoFiado, desc,
        partyName: customerName, paymentType: part.PaymentType,
        amountIn: amountIn, amountOut: amountOut, affectsBalance: true,
        refType: refType, refId: refId, notes: movNotes);
    }
  }

  internal static void RemoveFiadoRecebimento(
    SqliteConnection conn, SqliteTransaction tx, int paymentId)
  {
    using var load = conn.CreateCommand();
    load.Transaction = tx;
    load.CommandText = """
      SELECT m.id, s.status
      FROM cash_movements m
      JOIN cash_sessions s ON s.id = m.session_id
      WHERE (m.ref_type = 'fiado_payment' AND m.ref_id = $id)
         OR (m.ref_type = 'fiado_payment_part'
             AND m.ref_id >= $lo AND m.ref_id < $hi);
      """;
    load.Parameters.AddWithValue("$id", paymentId);
    load.Parameters.AddWithValue("$lo", paymentId * 100);
    load.Parameters.AddWithValue("$hi", paymentId * 100 + 100);
    var toDelete = new List<(int Id, string Status)>();
    using (var reader = load.ExecuteReader())
    {
      while (reader.Read())
        toDelete.Add((reader.GetInt32(0), reader.GetString(1)));
    }

    if (toDelete.Any(x => x.Status == "fechada"))
      throw new CashOperationException("Caixa do dia já encerrado — não é possível estornar movimento.");

    foreach (var (id, _) in toDelete)
    {
      using var del = conn.CreateCommand();
      del.Transaction = tx;
      del.CommandText = "DELETE FROM cash_movements WHERE id = $id;";
      del.Parameters.AddWithValue("$id", id);
      del.ExecuteNonQuery();
    }
  }

  public static void SyncCashFromPayables(SqliteConnection conn, DateTime sessionDate)
  {
    if (StoreNetworkMode.IsClient)
      return;
    var iso = sessionDate.ToString("yyyy-MM-dd");
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
      SELECT pi.id, pi.paid_amount, pi.amount, pi.payment_type, pi.paid_date,
             pt.number, p.name
      FROM payable_installments pi
      JOIN payable_titles pt ON pt.id = pi.title_id
      JOIN people p ON p.id = pt.supplier_id
      WHERE lower(pi.status) = 'pago'
        AND pi.paid_date = $date
        AND LOWER(TRIM(pi.payment_type)) IN ('dinheiro', 'cash');
      """;
    cmd.Parameters.AddWithValue("$date", iso);
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
      var instId = reader.GetInt32(0);
      if (MovementExists(conn, "payable_installment", instId))
        continue;

      var paid = reader.IsDBNull(1) ? reader.GetDouble(2) : reader.GetDouble(1);
      var paymentType = reader.IsDBNull(3) ? "Dinheiro" : reader.GetString(3);
      var paidDate = reader.IsDBNull(4) ? sessionDate : ParseDate(reader.GetString(4));
      var supplier = reader.IsDBNull(6) ? "" : reader.GetString(6);
      var historico = string.IsNullOrWhiteSpace(supplier) ? "COMPRA" : supplier.Trim().ToUpperInvariant();

      AddMovement(conn, null, paidDate, CashMovementKind.Compra, historico,
        partyName: supplier, paymentType: paymentType, amountOut: paid,
        refType: "payable_installment", refId: instId);
    }
  }

  private static CashOperacaoView BuildOperacaoView(SqliteConnection conn, DateTime d)
  {
    var op = GetOperationalStatusFull(conn, d);
    var workDate = op.WorkDate;
    var view = new CashOperacaoView
    {
      IsOperational = op.IsOperational,
      IsClosed = op.IsClosed,
      NeedsOpening = op.NeedsOpening,
      CarriedOver = op.CarriedOver,
      StatusMessage = op.Message,
      SessionDateBr = workDate.ToString("dd/MM/yyyy"),
    };

    var session = op.Session ?? GetSession(conn, d);
    if (session is null)
      return view;

    view.SessionId = session.Id;
    var movs = LoadMovements(conn, session.Id);
    var abertura = op.IsOperational ? op.Abertura : null;
    if (abertura is null)
    {
      for (var i = movs.Count - 1; i >= 0; i--)
      {
        if (movs[i].Kind == CashMovementKind.Abertura)
        {
          abertura = movs[i];
          break;
        }
      }
    }

    var openingAmount = Round(abertura?.AmountIn ?? session.OpeningAmount);
    var movsDiaCalc = movs.Where(m => m.Kind is not (CashMovementKind.Abertura or CashMovementKind.Fechamento)).ToList();
    var totals = TotalsExcludingAbertura(movsDiaCalc, openingAmount);

    var ciclo = MovsCicloAtual(movs);
    var movsTurno = ciclo.Where(m => m.Kind != CashMovementKind.Abertura).ToList();
    var totalsTurno = TotalsExcludingAbertura(movsTurno, openingAmount);
    totals.SaldoFinalGaveta = totalsTurno.SaldoFinalGaveta;

    if (abertura is not null)
    {
      view.OpenedAtBr = FormatBrDateTime(abertura.CreatedAt, "dd/MM/yyyy");
      view.OpenedTimeBr = FormatBrDateTime(abertura.CreatedAt, "HH:mm");
      view.OpeningObs = (abertura.Notes ?? session.Notes ?? "").Trim();
    }

    view.SaldoInicial = totals.SaldoInicial;
    view.EntradasCaixa = totals.EntradasCaixa;
    view.SaidasCaixa = totals.SaidasCaixa;
    view.SaldoFinal = totals.SaldoFinal;
    view.SaldoFinalGaveta = totals.SaldoFinalGaveta;
    view.EntradasPorForma = totals.EntradasPorForma;
    view.VendasDiaPdv = Round(movs
      .Where(m => m.Kind == CashMovementKind.Venda && m.AffectsBalance)
      .Sum(m => m.AmountIn));

    if (!string.IsNullOrEmpty(session.ClosedAt))
      view.ClosedAtBr = FormatBrDateTime(ParseDateTime(session.ClosedAt), "dd/MM/yyyy HH:mm");

    foreach (var m in movs)
    {
      if (m.Kind == CashMovementKind.Fechamento)
        continue;

      var entrada = m.Kind == CashMovementKind.VendaFiado ? 0.0 : m.AmountIn;
      var saida = m.Kind == CashMovementKind.VendaFiado ? 0.0 : m.AmountOut;
      var deletable = m.Kind is CashMovementKind.Sangria or CashMovementKind.Suprimento
        || (m.Kind is CashMovementKind.Venda or CashMovementKind.VendaFiado
            && m.RefType == "sale" && m.RefId > 0);

      view.Rows.Add(new CashMovementRow
      {
        Id = m.Id,
        DateTimeDisplay = FormatBrDateTime(m.CreatedAt, "dd/MM/yy HH:mm:ss"),
        Historico = HistoricoText(m),
        EntradaDisplay = entrada > 0 ? ProductPriceHelper.MoneyBr(entrada) : "",
        SaidaDisplay = saida > 0 ? ProductPriceHelper.MoneyBr(saida) : "",
        FormaPagto = NormalizeFormaPagto(m.PaymentType),
        Kind = m.Kind.ToString().ToLowerInvariant(),
        Deletable = deletable,
        RefType = m.RefType ?? "",
        RefId = m.RefId ?? 0,
      });
    }

    return view;
  }

  private static (int SessionId, bool Reopening) OpenSessionCore(SqliteConnection conn, SqliteTransaction tx, DateTime d,
    double openingAmount, string? notes)
  {
    var openElsewhere = FindOpenOperationalSession(conn, tx);
    if (openElsewhere is not null)
    {
      var openDate = ParseDate(openElsewhere.Value.Session.SessionDate);
      if (openDate.Date == d.Date)
        throw new CashOperationException("Caixa já está aberto. Feche (F3) antes de abrir de novo.");
      throw new CashOperationException(
        $"Caixa continua aberto desde {openDate:dd/MM/yyyy}. " +
        "Feche o turno (F3) antes de abrir saldo inicial de hoje.");
    }

    var session = GetOrCreateSession(conn, tx, d);
    var movs = LoadMovements(conn, session.Id, tx);
    if (session.Status == CashSessionStatus.Aberta && ActiveAbertura(movs) is not null)
      throw new CashOperationException("Caixa já está aberto. Feche (F3) antes de abrir de novo.");

    var reabrindo = session.Status == CashSessionStatus.Fechada
      || movs.Any(m => m.Kind == CashMovementKind.Fechamento);

    var opener = AppSession.CurrentUser;
    using (var upd = conn.CreateCommand())
    {
      upd.Transaction = tx;
      upd.CommandText = """
        UPDATE cash_sessions
        SET status = 'aberta', closed_at = NULL, counted_amount = NULL,
            difference_amount = NULL, expected_amount = NULL,
            opening_amount = $opening, notes = $notes,
            opened_by_user_id = $opId, opened_by_user_name = $opName,
            closed_by_user_id = NULL, closed_by_user_name = NULL
        WHERE id = $id;
        """;
      upd.Parameters.AddWithValue("$opening", openingAmount);
      upd.Parameters.AddWithValue("$notes", (object?)(notes?.Trim()[..Math.Min(300, notes.Trim().Length)]) ?? DBNull.Value);
      upd.Parameters.AddWithValue("$opId", (object?)opener?.Id ?? DBNull.Value);
      upd.Parameters.AddWithValue("$opName", (object?)opener?.Nome ?? DBNull.Value);
      upd.Parameters.AddWithValue("$id", session.Id);
      upd.ExecuteNonQuery();
    }

    var refId = (int)(DateTime.UtcNow.Ticks % 2_000_000_000);
    var desc = reabrindo ? "REABERTURA DE CAIXA" : "SALDO INICIAL";
    AddMovement(conn, tx, d, CashMovementKind.Abertura, desc,
      paymentType: "Dinheiro", amountIn: openingAmount,
      refType: "abertura", refId: refId, notes: notes);
    return (session.Id, reabrindo);
  }

  private static (int SessionId, double Expected, double Difference) CloseSessionCore(
    SqliteConnection conn, SqliteTransaction tx, DateTime d,
    double countedAmount, string? notes, int? operatorId = null, string? operatorName = null)
  {
    var today = GetSession(conn, d, tx);
    var todayMovs = today is null ? new List<CashMovementRecord>() : LoadMovements(conn, today.Id, tx);
    var todayOpen = today is not null
      && today.Status == CashSessionStatus.Aberta
      && ActiveAbertura(todayMovs) is not null;

    var session = todayOpen ? today : FindOpenOperationalSession(conn, tx)?.Session;
    if (session is null)
      throw new CashOperationException("Abra o caixa antes de encerrar o dia.");
    if (session.Status == CashSessionStatus.Fechada)
      throw new CashOperationException("Caixa deste dia já encerrado.");

    var sessionDate = ParseDate(session.SessionDate);
    var movs = LoadMovements(conn, session.Id, tx);
    var expected = CalcExpectedBalance(session, movs);
    var closedAt = DateBrHelper.NowUtcIso();
    var fechamentoTxt = $"FECHAMENTO DO CX: CAIXA-{sessionDate:dd/MM/yyyy} {DateBrHelper.UtcNowAsBrazil():HH:mm}";
    var vendasDia = movs
      .Where(m => m.Kind == CashMovementKind.Venda && m.AffectsBalance)
      .Sum(m => m.AmountIn);

    using (var upd = conn.CreateCommand())
    {
      upd.Transaction = tx;
      upd.CommandText = """
        UPDATE cash_sessions
        SET status = 'fechada', closed_at = $closed, expected_amount = $expected,
            counted_amount = $counted, difference_amount = $diff, notes = $notes,
            closed_by_user_id = $opId, closed_by_user_name = $opName
        WHERE id = $id;
        """;
      upd.Parameters.AddWithValue("$closed", closedAt);
      upd.Parameters.AddWithValue("$expected", expected);
      upd.Parameters.AddWithValue("$counted", countedAmount);
      upd.Parameters.AddWithValue("$diff", Round(countedAmount - expected));
      upd.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
      upd.Parameters.AddWithValue("$opId", (object?)operatorId ?? DBNull.Value);
      upd.Parameters.AddWithValue("$opName", (object?)(string.IsNullOrWhiteSpace(operatorName) ? null : operatorName.Trim()) ?? DBNull.Value);
      upd.Parameters.AddWithValue("$id", session.Id);
      upd.ExecuteNonQuery();
    }

    if (!MovementExists(conn, "fechamento", session.Id, tx))
    {
      using var ins = conn.CreateCommand();
      ins.Transaction = tx;
      ins.CommandText = """
        INSERT INTO cash_movements (
          session_id, movement_date, kind, description, payment_type,
          amount_in, amount_out, affects_balance, ref_type, ref_id, notes, created_at
        ) VALUES (
          $sid, $date, 'fechamento', $desc, NULL,
          $vendas, 0, 0, 'fechamento', $sid, $notes, $created
        );
        """;
      ins.Parameters.AddWithValue("$sid", session.Id);
      ins.Parameters.AddWithValue("$date", sessionDate.ToString("yyyy-MM-dd"));
      ins.Parameters.AddWithValue("$desc", fechamentoTxt);
      ins.Parameters.AddWithValue("$vendas", vendasDia);
      ins.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
      ins.Parameters.AddWithValue("$created", closedAt);
      ins.ExecuteNonQuery();
    }

    var difference = Round(countedAmount - expected);
    return (session.Id, expected, difference);
  }

  private static void AddMovement(
    SqliteConnection conn, SqliteTransaction? tx, DateTime sessionDate,
    CashMovementKind kind, string description,
    string? partyName = null, string? paymentType = null,
    double amountIn = 0, double amountOut = 0, bool affectsBalance = true,
    string? refType = null, int? refId = null, string? notes = null,
    bool allowDuplicateRef = false)
  {
    if (!allowDuplicateRef && refType is not null && refId is not null
        && MovementExists(conn, refType, refId.Value, tx))
      return;

    var resolved = ResolveOperationalSession(conn, sessionDate, tx);
    var session = resolved.Session is not null && resolved.Abertura is not null
      ? resolved.Session
      : GetOrCreateSession(conn, tx, sessionDate);
    if (session.Status == CashSessionStatus.Fechada && kind != CashMovementKind.Fechamento)
      throw new CashOperationException("Caixa do dia já encerrado — não é possível lançar movimentos.");

    var created = DateBrHelper.NowUtcIso();
    using var ins = conn.CreateCommand();
    if (tx is not null) ins.Transaction = tx;
    ins.CommandText = """
      INSERT INTO cash_movements (
        session_id, movement_date, kind, description, party_name, payment_type,
        amount_in, amount_out, affects_balance, ref_type, ref_id, notes, created_at
      ) VALUES (
        $sid, $date, $kind, $desc, $party, $pay,
        $in, $out, $aff, $refType, $refId, $notes, $created
      );
      """;
    ins.Parameters.AddWithValue("$sid", session.Id);
    ins.Parameters.AddWithValue("$date", sessionDate.ToString("yyyy-MM-dd"));
    ins.Parameters.AddWithValue("$kind", KindToDb(kind));
    ins.Parameters.AddWithValue("$desc", description[..Math.Min(200, description.Length)]);
    ins.Parameters.AddWithValue("$party", (object?)partyName ?? DBNull.Value);
    ins.Parameters.AddWithValue("$pay", (object?)paymentType ?? DBNull.Value);
    ins.Parameters.AddWithValue("$in", amountIn);
    ins.Parameters.AddWithValue("$out", amountOut);
    ins.Parameters.AddWithValue("$aff", affectsBalance ? 1 : 0);
    ins.Parameters.AddWithValue("$refType", (object?)refType ?? DBNull.Value);
    ins.Parameters.AddWithValue("$refId", (object?)refId ?? DBNull.Value);
    ins.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
    ins.Parameters.AddWithValue("$created", created);
    ins.ExecuteNonQuery();
  }

  private static (bool IsOperational, bool IsClosed, bool NeedsOpening) GetOperationalStatus(
    SqliteConnection conn, DateTime d)
  {
    var op = GetOperationalStatusFull(conn, d);
    return (op.IsOperational, op.IsClosed, op.NeedsOpening);
  }

  private sealed record OperationalStatusFull(
    bool IsOperational,
    bool IsClosed,
    bool NeedsOpening,
    bool CarriedOver,
    DateTime WorkDate,
    string Message,
    CashSessionRecord? Session,
    CashMovementRecord? Abertura);

  private static OperationalStatusFull GetOperationalStatusFull(
    SqliteConnection conn, DateTime d, SqliteTransaction? tx = null)
  {
    var resolved = ResolveOperationalSession(conn, d, tx);
    var isOperational = resolved.Session is not null && resolved.Abertura is not null;
    var today = GetSession(conn, d, tx);
    var isClosed = !isOperational
      && today is not null
      && today.Status == CashSessionStatus.Fechada;
    var workDate = isOperational && resolved.Session is not null
      ? ParseDate(resolved.Session.SessionDate)
      : d;
    var message = !isOperational
      ? "Abra o caixa para vender ou receber fiado."
      : resolved.CarriedOver
        ? $"Caixa aberto desde {workDate:dd/MM/yyyy} — permanece até você fechar (F3)."
        : "";

    return new OperationalStatusFull(
      isOperational,
      isClosed,
      !isOperational,
      resolved.CarriedOver,
      workDate,
      message,
      resolved.Session,
      resolved.Abertura);
  }

  private static (CashSessionRecord? Session, CashMovementRecord? Abertura, bool CarriedOver)
    ResolveOperationalSession(SqliteConnection conn, DateTime d, SqliteTransaction? tx = null)
  {
    var today = GetSession(conn, d, tx);
    if (today is not null)
    {
      var movs = LoadMovements(conn, today.Id, tx);
      var abertura = ActiveAbertura(movs);
      if (today.Status == CashSessionStatus.Aberta && abertura is not null)
        return (today, abertura, false);
    }

    var carried = FindOpenOperationalSession(conn, tx);
    if (carried is not null)
    {
      var carriedOver = !string.Equals(
        carried.Value.Session.SessionDate,
        d.ToString("yyyy-MM-dd"),
        StringComparison.Ordinal);
      return (carried.Value.Session, carried.Value.Abertura, carriedOver);
    }

    if (today is null)
      return (null, null, false);

    return (today, ActiveAbertura(LoadMovements(conn, today.Id, tx)), false);
  }

  private static (CashSessionRecord Session, CashMovementRecord Abertura)? FindOpenOperationalSession(
    SqliteConnection conn, SqliteTransaction? tx = null)
  {
    using var cmd = conn.CreateCommand();
    if (tx is not null) cmd.Transaction = tx;
    cmd.CommandText = """
      SELECT id, session_date, opening_amount, status, closed_at, notes, counted_amount,
             difference_amount, opened_by_user_id, opened_by_user_name,
             closed_by_user_id, closed_by_user_name
      FROM cash_sessions
      WHERE status = 'aberta'
      ORDER BY session_date DESC, id DESC;
      """;
    var sessions = new List<CashSessionRecord>();
    using (var reader = cmd.ExecuteReader())
    {
      while (reader.Read())
        sessions.Add(MapSession(reader));
    }

    foreach (var session in sessions)
    {
      var abertura = ActiveAbertura(LoadMovements(conn, session.Id, tx));
      if (abertura is not null)
        return (session, abertura);
    }

    return null;
  }

  private static CashSessionRecord? GetSession(SqliteConnection conn, DateTime d, SqliteTransaction? tx = null)
  {
    using var cmd = conn.CreateCommand();
    if (tx is not null) cmd.Transaction = tx;
    cmd.CommandText = """
      SELECT id, session_date, opening_amount, status, closed_at, notes, counted_amount,
             difference_amount, opened_by_user_id, opened_by_user_name,
             closed_by_user_id, closed_by_user_name
      FROM cash_sessions WHERE session_date = $date LIMIT 1;
      """;
    cmd.Parameters.AddWithValue("$date", d.ToString("yyyy-MM-dd"));
    using var reader = cmd.ExecuteReader();
    if (!reader.Read())
      return null;
    return MapSession(reader);
  }

  private static CashSessionRecord GetOrCreateSession(SqliteConnection conn, SqliteTransaction? tx, DateTime d)
  {
    var existing = GetSession(conn, d, tx);
    if (existing is not null)
      return existing;

    using var ins = conn.CreateCommand();
    if (tx is not null) ins.Transaction = tx;
    ins.CommandText = """
      INSERT INTO cash_sessions (session_date, opening_amount, status, created_at)
      VALUES ($date, 0, 'aberta', $created);
      SELECT last_insert_rowid();
      """;
    ins.Parameters.AddWithValue("$date", d.ToString("yyyy-MM-dd"));
    ins.Parameters.AddWithValue("$created", DateBrHelper.NowUtcIso());
    var id = Convert.ToInt32(ins.ExecuteScalar());
    return new CashSessionRecord
    {
      Id = id,
      SessionDate = d.ToString("yyyy-MM-dd"),
      OpeningAmount = 0,
      Status = CashSessionStatus.Aberta,
    };
  }

  private static List<CashMovementRecord> LoadMovements(SqliteConnection conn, int sessionId, SqliteTransaction? tx = null)
  {
    var list = new List<CashMovementRecord>();
    using var cmd = conn.CreateCommand();
    if (tx is not null) cmd.Transaction = tx;
    cmd.CommandText = """
      SELECT id, kind, description, party_name, payment_type, amount_in, amount_out,
             affects_balance, ref_type, ref_id, notes, created_at
      FROM cash_movements WHERE session_id = $sid ORDER BY created_at ASC, id ASC;
      """;
    cmd.Parameters.AddWithValue("$sid", sessionId);
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
      list.Add(new CashMovementRecord
      {
        Id = reader.GetInt32(0),
        Kind = ParseKind(reader.GetString(1)),
        Description = reader.GetString(2),
        PartyName = reader.IsDBNull(3) ? null : reader.GetString(3),
        PaymentType = reader.IsDBNull(4) ? null : reader.GetString(4),
        AmountIn = reader.GetDouble(5),
        AmountOut = reader.GetDouble(6),
        AffectsBalance = reader.GetInt32(7) != 0,
        RefType = reader.IsDBNull(8) ? null : reader.GetString(8),
        RefId = reader.IsDBNull(9) ? null : reader.GetInt32(9),
        Notes = reader.IsDBNull(10) ? null : reader.GetString(10),
        CreatedAt = ParseDateTime(reader.GetString(11)),
      });
    }
    return list;
  }

  private static bool MovementExists(SqliteConnection conn, string refType, int refId, SqliteTransaction? tx = null)
  {
    using var cmd = conn.CreateCommand();
    if (tx is not null) cmd.Transaction = tx;
    cmd.CommandText = """
      SELECT 1 FROM cash_movements WHERE ref_type = $rt AND ref_id = $rid LIMIT 1;
      """;
    cmd.Parameters.AddWithValue("$rt", refType);
    cmd.Parameters.AddWithValue("$rid", refId);
    return cmd.ExecuteScalar() is not null;
  }

  private static CashMovementRecord? ActiveAbertura(List<CashMovementRecord> movs)
  {
    var lastClose = movs
      .Where(m => m.Kind == CashMovementKind.Fechamento)
      .Select(m => m.CreatedAt)
      .DefaultIfEmpty(DateTime.MinValue)
      .Max();
    var aberturas = movs.Where(m => m.Kind == CashMovementKind.Abertura).OrderBy(m => m.CreatedAt).ToList();
    for (var i = aberturas.Count - 1; i >= 0; i--)
    {
      if (lastClose == DateTime.MinValue || aberturas[i].CreatedAt > lastClose)
        return aberturas[i];
    }
    return null;
  }

  private static List<CashMovementRecord> MovsCicloAtual(List<CashMovementRecord> movs)
  {
    var ab = ActiveAbertura(movs);
    if (ab is null)
      return [];
    return movs.Where(m => m.CreatedAt >= ab.CreatedAt && m.Kind != CashMovementKind.Fechamento).ToList();
  }

  private static double CalcExpectedBalance(CashSessionRecord session, List<CashMovementRecord> movs)
  {
    var ciclo = MovsCicloAtual(movs);
    return CalcSessionTotals(ciclo, session.OpeningAmount).SaldoFinalGaveta;
  }

  private static SessionTotals TotalsExcludingAbertura(List<CashMovementRecord> movs, double openingAmount)
  {
    var raw = CalcSessionTotals(movs, openingAmount);
    return raw;
  }

  private static SessionTotals CalcSessionTotals(List<CashMovementRecord> movs, double openingAmount)
  {
    var saldoInicial = openingAmount;
    var entradasTudo = 0.0;
    var saidasTudo = 0.0;
    var entradasGaveta = 0.0;
    var saidasGaveta = 0.0;
    var entradasPorForma = new Dictionary<string, double>();

    foreach (var m in movs)
    {
      if (m.Kind == CashMovementKind.Fechamento)
        continue;
      if (m.Kind == CashMovementKind.Abertura)
      {
        saldoInicial = m.AmountIn;
        continue;
      }

      if (m.Kind is CashMovementKind.Venda or CashMovementKind.RecebimentoFiado or CashMovementKind.Troca
          && (m.AmountIn > 0.009 || m.AmountOut > 0.009))
      {
        var forma = NormalizeFormaPagto(m.PaymentType);
        // Com troco: amount_in = recebido, amount_out = troco → líquido é o que vale na forma.
        var liquido = Round(m.AmountIn - m.AmountOut);
        if (liquido > 0.009)
          entradasPorForma[forma] = Round(entradasPorForma.GetValueOrDefault(forma) + liquido);
      }

      if (m.AffectsBalance || m.Kind is CashMovementKind.Venda or CashMovementKind.RecebimentoFiado)
      {
        entradasTudo += m.AmountIn;
        saidasTudo += m.AmountOut;
      }

      if (MovementAffectsGaveta(m))
      {
        entradasGaveta += m.AmountIn;
        saidasGaveta += m.AmountOut;
      }
    }

    return new SessionTotals
    {
      SaldoInicial = Round(saldoInicial),
      EntradasCaixa = Round(entradasTudo),
      SaidasCaixa = Round(saidasTudo),
      SaldoFinal = Round(saldoInicial + entradasTudo - saidasTudo),
      SaldoFinalGaveta = Round(saldoInicial + entradasGaveta - saidasGaveta),
      EntradasPorForma = entradasPorForma,
    };
  }

  private static bool MovementAffectsGaveta(CashMovementRecord m)
  {
    if (m.Kind is CashMovementKind.Abertura or CashMovementKind.Sangria or CashMovementKind.Suprimento)
      return true;
    if (m.Kind == CashMovementKind.Compra)
      return IsDinheiro(m.PaymentType);
    if (m.Kind is CashMovementKind.Venda or CashMovementKind.RecebimentoFiado or CashMovementKind.Troca)
      return IsDinheiro(m.PaymentType);
    return false;
  }

  private static string HistoricoText(CashMovementRecord m)
  {
    if (m.Kind == CashMovementKind.Abertura)
      return (m.Description ?? "ABERTURA DE CAIXA - CAIXA").Trim();
    if (m.Kind == CashMovementKind.Fechamento)
      return m.Description ?? "FECHAMENTO DO CX: CAIXA";
    if (!string.IsNullOrWhiteSpace(m.PartyName) && m.Kind == CashMovementKind.Compra)
      return m.PartyName.Trim().ToUpperInvariant();
    return (m.Description ?? "").Trim();
  }

  private static string NormalizeFormaPagto(string? paymentType)
  {
    var s = (paymentType ?? "").Trim();
    if (string.IsNullOrEmpty(s))
      return "—";
    var low = s.ToLowerInvariant();
    if (low is "dinheiro" or "cash") return "Dinheiro";
    if (low == "pix") return "Pix";
    if (low.Contains("debito") || low is "cartao debito" or "cartão débito") return "Cartão Débito";
    if (low.Contains("credito") || low.Contains("crédito")) return "Cartão Crédito";
    if (low == "cheque") return "Cheque";
    if (low == "boleto") return "Boleto";
    if (low is "transferencia" or "transferência") return "Transferência";
    if (low is "fiado" or "a prazo" or "prazo") return "Fiado";
    return s.Length > 30 ? s[..30] : s;
  }

  private static bool IsDinheiro(string? paymentType) =>
    NormalizeFormaPagto(paymentType) == "Dinheiro";

  private static CashMovementKind ParseKind(string value) => value.ToLowerInvariant() switch
  {
    "abertura" => CashMovementKind.Abertura,
    "fechamento" => CashMovementKind.Fechamento,
    "venda" => CashMovementKind.Venda,
    "venda_fiado" => CashMovementKind.VendaFiado,
    "recebimento_fiado" => CashMovementKind.RecebimentoFiado,
    "compra" => CashMovementKind.Compra,
    "sangria" => CashMovementKind.Sangria,
    "suprimento" => CashMovementKind.Suprimento,
    "troca" => CashMovementKind.Troca,
    _ => CashMovementKind.Sangria,
  };

  private static string KindToDb(CashMovementKind kind) => kind switch
  {
    CashMovementKind.VendaFiado => "venda_fiado",
    CashMovementKind.RecebimentoFiado => "recebimento_fiado",
    _ => kind.ToString().ToLowerInvariant(),
  };

  private static double Round(double v) => ProductPriceHelper.RoundPrice(v);

  private static DateTime ParseDateTime(string? s)
  {
    if (string.IsNullOrWhiteSpace(s))
      return DateTime.UtcNow;

    var cleaned = s.Trim();
    if (cleaned.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
      cleaned = cleaned[..^1];
    cleaned = cleaned.Replace('T', ' ');

    if (DateTime.TryParse(cleaned, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
        || DateTime.TryParse(cleaned, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
      return DateTime.SpecifyKind(dt, DateTimeKind.Utc);

    return DateTime.UtcNow;
  }

  private static DateTime ParseDate(string s) =>
    DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
      ? dt : DateTime.Today;

  private static string FormatBrDateTime(DateTime dt, string format) =>
    DateBrHelper.FormatUtcToBrazil(dt, format);

  private static CashSessionRecord MapSession(SqliteDataReader reader) => new()
  {
    Id = reader.GetInt32(0),
    SessionDate = reader.GetString(1),
    OpeningAmount = reader.GetDouble(2),
    Status = reader.GetString(3) == "fechada" ? CashSessionStatus.Fechada : CashSessionStatus.Aberta,
    ClosedAt = reader.IsDBNull(4) ? null : reader.GetString(4),
    Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
    CountedAmount = reader.IsDBNull(6) ? null : reader.GetDouble(6),
    DifferenceAmount = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetDouble(7) : null,
    OpenedByUserId = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetInt32(8) : null,
    OpenedByUserName = reader.FieldCount > 9 && !reader.IsDBNull(9) ? reader.GetString(9) : null,
    ClosedByUserId = reader.FieldCount > 10 && !reader.IsDBNull(10) ? reader.GetInt32(10) : null,
    ClosedByUserName = reader.FieldCount > 11 && !reader.IsDBNull(11) ? reader.GetString(11) : null,
  };

  private sealed class CashSessionRecord
  {
    public int Id { get; init; }
    public string SessionDate { get; init; } = "";
    public double OpeningAmount { get; init; }
    public CashSessionStatus Status { get; init; }
    public string? ClosedAt { get; init; }
    public string? Notes { get; init; }
    public double? CountedAmount { get; init; }
    public double? DifferenceAmount { get; init; }
    public int? OpenedByUserId { get; init; }
    public string? OpenedByUserName { get; init; }
    public int? ClosedByUserId { get; init; }
    public string? ClosedByUserName { get; init; }
  }

  private sealed class CashMovementRecord
  {
    public int Id { get; init; }
    public CashMovementKind Kind { get; init; }
    public string Description { get; init; } = "";
    public string? PartyName { get; init; }
    public string? PaymentType { get; init; }
    public double AmountIn { get; init; }
    public double AmountOut { get; init; }
    public bool AffectsBalance { get; init; }
    public string? RefType { get; init; }
    public int? RefId { get; init; }
    public string? Notes { get; init; }
    public DateTime CreatedAt { get; init; }
  }

  private sealed class SessionTotals
  {
    public double SaldoInicial { get; set; }
    public double EntradasCaixa { get; set; }
    public double SaidasCaixa { get; set; }
    public double SaldoFinal { get; set; }
    public double SaldoFinalGaveta { get; set; }
    public Dictionary<string, double> EntradasPorForma { get; set; } = new();
  }
}
