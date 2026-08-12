using System.ComponentModel;
using System.Runtime.CompilerServices;
using SGDB.Utils;

namespace SGDB.Models;

public sealed class SaleExchangeException : Exception
{
    public SaleExchangeException(string message) : base(message) { }
}

public sealed class SaleExchangeReturnLine
{
    public int SaleItemId { get; init; }
    public double Qty { get; init; }
}

public sealed class SaleExchangeNewLine
{
    public int ProductId { get; init; }
    public double Qty { get; init; }
    public double? UnitPrice { get; init; }
}

public sealed class SaleExchangeRequest
{
    public int OriginalSaleId { get; init; }
    public IReadOnlyList<SaleExchangeReturnLine> Returns { get; init; } = [];
    public IReadOnlyList<SaleExchangeNewLine> NewItems { get; init; } = [];
    public string? PaymentType { get; init; }
    public string? Notes { get; init; }
}

public sealed class SaleExchangeResult
{
    public int ExchangeId { get; init; }
    public double ReturnTotal { get; init; }
    public double NewTotal { get; init; }
    public double Difference { get; init; }
    public string Message { get; init; } = "";
    public bool WarnManualPixRefund { get; init; }
}

public sealed class SaleExchangeSaleItemVm : INotifyPropertyChanged
{
    private double _returnQty;

    public int SaleItemId { get; init; }
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public double SoldQty { get; init; }
    public double AlreadyReturnedQty { get; init; }
    public double AvailableQty => ProductPriceHelper.RoundPrice(SoldQty - AlreadyReturnedQty);
    public double UnitPrice { get; init; }

    public double ReturnQty
    {
        get => _returnQty;
        set
        {
            var v = ProductPriceHelper.RoundPrice(Math.Max(0, value));
            if (v > AvailableQty) v = AvailableQty;
            if (Math.Abs(_returnQty - v) < 0.00001) return;
            _returnQty = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReturnAmount));
            OnPropertyChanged(nameof(ReturnAmountDisplay));
        }
    }

    public string SoldQtyDisplay => SoldQty.ToString("0.###");
    public string AvailableQtyDisplay => AvailableQty.ToString("0.###");
    public string UnitPriceDisplay => ProductPriceHelper.MoneyBr(UnitPrice);
    public double ReturnAmount => ProductPriceHelper.RoundPrice(ReturnQty * UnitPrice);
    public string ReturnAmountDisplay => ProductPriceHelper.MoneyBr(ReturnAmount);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class SaleExchangeNewItemVm : INotifyPropertyChanged
{
    private double _qty = 1;
    private double _unitPrice;

    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string Unit { get; init; } = "UN";

    public double Qty
    {
        get => _qty;
        set
        {
            var v = ProductPriceHelper.RoundPrice(Math.Max(0, value));
            if (Math.Abs(_qty - v) < 0.00001) return;
            _qty = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Amount));
            OnPropertyChanged(nameof(AmountDisplay));
            OnPropertyChanged(nameof(QtyDisplay));
        }
    }

    public double UnitPrice
    {
        get => _unitPrice;
        set
        {
            var v = ProductPriceHelper.RoundPrice(Math.Max(0, value));
            if (Math.Abs(_unitPrice - v) < 0.00001) return;
            _unitPrice = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Amount));
            OnPropertyChanged(nameof(AmountDisplay));
            OnPropertyChanged(nameof(UnitPriceDisplay));
        }
    }

    public double Amount => ProductPriceHelper.RoundPrice(Qty * UnitPrice);
    public string QtyDisplay => Qty.ToString("0.###");
    public string UnitPriceDisplay => ProductPriceHelper.MoneyBr(UnitPrice);
    public string AmountDisplay => ProductPriceHelper.MoneyBr(Amount);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class SaleExchangeSearchRow
{
    public int Id { get; init; }
    public string SessionDateBr { get; init; } = "";
    public string CreatedAtBr { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string PaymentLabel { get; init; } = "";
    public double Total { get; init; }
    public string TotalDisplay => ProductPriceHelper.MoneyBr(Total);
}
