using SGDB.Utils;

namespace SGDB.Models;

public sealed class PayableTitleRow
{
    public int Id { get; init; }
    public int? PurchaseId { get; init; }
    public required string Number { get; init; }
    public string EmissionDate { get; init; } = "";
    public int SupplierId { get; init; }
    public string SupplierName { get; init; } = "";
    public string DocRef { get; init; } = "";
    public double TotalAmount { get; init; }
    public double Discount { get; init; }
    public double Interest { get; init; }
    public double PaidAmount { get; init; }
    public string? PaidDate { get; init; }
    public string Situacao { get; init; } = "pendente";
    public int InstallmentCount { get; init; }

    public string EmissionDateDisplay => DateBrHelper.FormatIso(EmissionDate);
    public string PaidDateDisplay => DateBrHelper.FormatIso(PaidDate);
    public string DocRefDisplay => Truncate(DocRef, 16);
    public string TotalDisplay => ProductPriceHelper.MoneyBr(TotalAmount);
    public string DiscountDisplay => ProductPriceHelper.MoneyBr(Discount);
    public string InterestDisplay => ProductPriceHelper.MoneyBr(Interest);
    public string PaidAmountDisplay => ProductPriceHelper.MoneyBr(PaidAmount);
    public string SituacaoDisplay => Capitalize(Situacao);

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s[1..];
}

public sealed class PayableInstallmentRow
{
    public int Id { get; init; }
    public int TitleId { get; init; }
    public int? PurchaseId { get; init; }
    public required string Number { get; init; }
    public int Seq { get; init; }
    public string EmissionDate { get; init; } = "";
    public string DueDate { get; init; } = "";
    public int SupplierId { get; init; }
    public string SupplierName { get; init; } = "";
    public string DocRef { get; init; } = "";
    public double Amount { get; init; }
    public double Discount { get; init; }
    public double Interest { get; init; }
    public double PaidAmount { get; init; }
    public string? PaidDate { get; init; }
    public string PaymentType { get; init; } = "Boleto";
    public string Situacao { get; init; } = "pendente";
    public string Status { get; init; } = "pendente";

    public string DisplayNumber => $"{Number}/{Seq}";
    public string EmissionDateDisplay => DateBrHelper.FormatIso(EmissionDate);
    public string DueDateDisplay => DateBrHelper.FormatIso(DueDate);
    public string PaidDateDisplay => DateBrHelper.FormatIso(PaidDate);
    public string DocRefDisplay => Truncate(DocRef, 12);
    public string AmountDisplay => ProductPriceHelper.MoneyBr(Amount);
    public string DiscountDisplay => ProductPriceHelper.MoneyBr(Discount);
    public string InterestDisplay => ProductPriceHelper.MoneyBr(Interest);
    public string PaidAmountDisplay => ProductPriceHelper.MoneyBr(PaidAmount);
    public string SituacaoDisplay => Capitalize(Situacao);

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s[1..];
}

public sealed class PayableInstallmentDetail
{
    public int Id { get; init; }
    public int TitleId { get; init; }
    public int Seq { get; init; }
    public string DueDate { get; init; } = "";
    public double Amount { get; init; }
    public double Discount { get; init; }
    public double Interest { get; init; }
    public double Multa { get; init; }
    public double PaidAmount { get; init; }
    public string? PaidDate { get; init; }
    public string PaymentType { get; init; } = "Boleto";
    public string Status { get; init; } = "pendente";
    public string SupplierName { get; init; } = "";
    public string Number { get; init; } = "";
    public int? PurchaseId { get; init; }
    public string Situacao { get; init; } = "pendente";
    public string? Notes { get; init; }
    public string? FinancialAccount { get; init; }

    public string DisplayNumber => $"{Number}/{Seq}";
}

public sealed class PayableTitleCreateInput
{
    public int SupplierId { get; init; }
    public required string Number { get; init; }
    public required string EmissionDate { get; init; }
    public required string DueDate { get; init; }
    public double TotalAmount { get; init; }
    public string PaymentType { get; init; } = "Boleto";
    public string? ExpenseCategory { get; init; }
}

public sealed class PayablePayInput
{
    public double PaidAmount { get; init; }
    public required string PaidDate { get; init; }
    public double Discount { get; init; }
    public double Interest { get; init; }
    public double Multa { get; init; }
    public string PaymentType { get; init; } = "Boleto";
    public string? Notes { get; init; }
    public string? FinancialAccount { get; init; }
}

public sealed class PayableInstallmentUpdateInput
{
    public required string DueDate { get; init; }
    public double Amount { get; init; }
    public double Discount { get; init; }
    public double Interest { get; init; }
    public string PaymentType { get; init; } = "Boleto";
}

public static class PayablePaymentTypes
{
    public static readonly string[] All = ["Boleto", "Pix", "Dinheiro", "Cheque", "Transferencia"];
}
