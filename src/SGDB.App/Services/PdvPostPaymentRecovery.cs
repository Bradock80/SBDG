using System.IO;
using System.Windows;

namespace SGDB.Services;

/// <summary>
/// Recuperação após o operador confirmar pagamento e a venda/comanda não gravar.
/// Não decide validade, estoque nem o valor devido — só trata o dinheiro já confirmado.
/// </summary>
public static class PdvPostPaymentRecovery
{
    public const double PixPaidTolerance = 0.009;

    public const string PixTitle = "PDV — PIX recebido, venda não registrada";

    public const string OutcomeNone = "none";
    public const string OutcomeRefunded = "refunded";
    public const string OutcomeRefundPending = "refund_pending";
    public const string OutcomeNoPaymentId = "no_payment_id";

    public sealed class Display
    {
        public string Title { get; init; } = "";
        public string Body { get; init; } = "";
        public bool HasPixPaid { get; init; }
        public string RefundOutcome { get; init; } = OutcomeNone;
        public bool RefundAttempted { get; init; }
        public bool IsUnexpected { get; init; }
        public long? PixPaymentId { get; init; }
    }

    /// <summary>
    /// Tenta o estorno PIX (se houver) e monta o diálogo. Não decide a falha de negócio.
    /// </summary>
    public static Display Recover(
        string businessMessage,
        double pixPaidAmount,
        long? pixPaymentId,
        string? businessTitle = null,
        IMercadoPagoPixGateway? gateway = null)
    {
        var message = string.IsNullOrWhiteSpace(businessMessage) ? "A operação não foi registrada." : businessMessage.Trim();
        var hasPix = pixPaidAmount > PixPaidTolerance;
        if (!hasPix)
        {
            return new Display
            {
                Title = string.IsNullOrWhiteSpace(businessTitle) ? "PDV" : businessTitle.Trim(),
                Body = message,
                HasPixPaid = false,
                RefundOutcome = OutcomeNone,
            };
        }

        var (outcome, attempted) = TryRefundApprovedPix(pixPaymentId, gateway);
        var status = RefundStatusText(outcome, pixPaymentId);
        var pid = pixPaymentId is long id && id > 0 ? $"\nPagamento Mercado Pago #{id}" : "";

        var body =
            $"{message}\n\n" +
            $"ATENÇÃO: o cliente JÁ PAGOU R$ {pixPaidAmount:N2} via PIX e a venda não foi registrada." +
            pid +
            $"\n\n{status}\n\n" +
            "A venda não foi registrada. Ajuste a operação antes de tentar cobrar novamente.";

        return new Display
        {
            Title = PixTitle,
            Body = body,
            HasPixPaid = true,
            RefundOutcome = outcome,
            RefundAttempted = attempted,
            PixPaymentId = pixPaymentId is long pidValue && pidValue > 0 ? pidValue : null,
        };
    }

    /// <summary>
    /// Exception inesperada depois do pagamento: recupera PIX, grava crash.log, não engole o diagnóstico.
    /// </summary>
    public static Display RecoverUnexpected(
        Exception ex,
        double pixPaidAmount,
        long? pixPaymentId,
        IMercadoPagoPixGateway? gateway = null)
    {
        ArgumentNullException.ThrowIfNull(ex);
        TryWriteCrashLog(ex);
        var diagnostic =
            $"Erro inesperado:\n\n{ex.Message}\n\n{ex.GetType().Name}\n\nLog: {CrashLogPath()}";
        var display = Recover(diagnostic, pixPaidAmount, pixPaymentId, "SGDB — Erro", gateway);
        return new Display
        {
            Title = display.HasPixPaid ? PixTitle : "SGDB — Erro",
            Body = display.Body,
            HasPixPaid = display.HasPixPaid,
            RefundOutcome = display.RefundOutcome,
            RefundAttempted = display.RefundAttempted,
            IsUnexpected = true,
            PixPaymentId = display.PixPaymentId,
        };
    }

    public static void Present(Display display)
    {
        ArgumentNullException.ThrowIfNull(display);
        var image = display.HasPixPaid || display.IsUnexpected
            ? MessageBoxImage.Error
            : MessageBoxImage.Warning;
        MessageBox.Show(display.Body, display.Title, MessageBoxButton.OK, image);
    }

    public static Display Show(
        string businessMessage,
        double pixPaidAmount,
        long? pixPaymentId,
        string? businessTitle = null,
        IMercadoPagoPixGateway? gateway = null)
    {
        var display = Recover(businessMessage, pixPaidAmount, pixPaymentId, businessTitle, gateway);
        Present(display);
        return display;
    }

    public const string PostCommitTitle = "PDV — venda registrada";

    /// <summary>Somente testes: simula falha de vínculo após o COMMIT.</summary>
    public static Exception? TestThrowOnAttachSale { get; set; }

    /// <summary>
    /// Liga o intent PIX à venda já commitada. Não chamar antes do COMMIT.
    /// </summary>
    public static void AttachSaleAfterSuccess(long? pixPaymentId, int saleId)
    {
        if (TestThrowOnAttachSale is not null)
        {
            var ex = TestThrowOnAttachSale;
            TestThrowOnAttachSale = null;
            throw ex;
        }

        if (pixPaymentId is long id && id > 0 && saleId > 0)
            PixIntentService.AttachSale(id, saleId);
    }

    /// <summary>
    /// Falha auxiliar depois da venda já commitada. Nunca estorna PIX.
    /// </summary>
    public static Display FormatPostCommitFailure(Exception ex, int saleId)
    {
        ArgumentNullException.ThrowIfNull(ex);
        TryWriteCrashLog(ex);
        return new Display
        {
            Title = PostCommitTitle,
            Body =
                $"A venda #{saleId} já foi registrada.\n\n" +
                "Ocorreu uma falha depois da gravação (vínculo PIX, cupom ou registro auxiliar).\n" +
                "Não foi feito estorno automático. O pagamento recebido permanece.\n\n" +
                $"{ex.GetType().Name}: {ex.Message}",
            HasPixPaid = false,
            RefundOutcome = OutcomeNone,
        };
    }

    public static void PresentPostCommitFailure(Exception ex, int saleId) =>
        Present(FormatPostCommitFailure(ex, saleId));

    public static bool HasApprovedPix(double pixPaidAmount, long? pixPaymentId) =>
        pixPaidAmount > PixPaidTolerance && pixPaymentId is long id && id > 0;

    private static (string Outcome, bool Attempted) TryRefundApprovedPix(
        long? pixPaymentId,
        IMercadoPagoPixGateway? gateway)
    {
        if (pixPaymentId is not long id || id <= 0)
            return (OutcomeNoPaymentId, false);

        PixCheckoutCoordinator.RefundApprovedWithoutSaleAsync(id, gateway)
            .GetAwaiter()
            .GetResult();

        var intent = PixIntentService.GetByMpPaymentId(id);
        if (string.Equals(intent?.Status, "refunded", StringComparison.OrdinalIgnoreCase))
            return (OutcomeRefunded, true);
        return (OutcomeRefundPending, true);
    }

    private static string RefundStatusText(string outcome, long? pixPaymentId) =>
        outcome switch
        {
            OutcomeRefunded =>
                "Foi solicitado o estorno no Mercado Pago. Confira se o valor voltou ao cliente.",
            OutcomeRefundPending =>
                "Não foi possível confirmar o estorno no Mercado Pago. O reembolso ficou pendente.\n" +
                $"Pagamento: {pixPaymentId?.ToString() ?? "—"}.\n" +
                "Não considere o caso encerrado. Confira no Mercado Pago.",
            _ =>
                "Não há identificador Mercado Pago para estornar pela API (PIX chave/manual ou cobrança sem QR).\n" +
                "Trate o valor manualmente. Não considere o caso encerrado.",
        };

    internal static string CrashLogPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SGDB",
            "crash.log");

    private static void TryWriteCrashLog(Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(CrashLogPath());
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(
                CrashLogPath(),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{ex}\n\n");
        }
        catch
        {
            // ignore logging failures
        }
    }
}
