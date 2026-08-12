using SGDB.Application.Sales;

namespace SGDB.Tests;

public class SwapSaleItemUseCaseTests
{
    [Fact]
    public void Execute_Success_CallsGatewayOnce_AndReturnsResult()
    {
        var expected = new SwapSaleItemResult
        {
            SaleId = 55,
            NewTotal = 120,
            Message = "ok",
            RefundHint = null,
        };
        var gateway = new FakeGateway { Result = expected };
        var useCase = new SwapSaleItemUseCase(gateway);

        var command = ValidCommand();
        var result = useCase.Execute(command);

        Assert.Equal(1, gateway.CallCount);
        Assert.Same(command, gateway.LastCommand);
        Assert.Equal(55, result.SaleId);
        Assert.Equal(120, result.NewTotal);
        Assert.Equal("ok", result.Message);
    }

    [Fact]
    public void Execute_GatewayThrows_Propagates()
    {
        var gateway = new FakeGateway
        {
            Exception = new InvalidOperationException("sem permissão"),
        };
        var useCase = new SwapSaleItemUseCase(gateway);

        var ex = Assert.Throws<InvalidOperationException>(() => useCase.Execute(ValidCommand()));
        Assert.Equal("sem permissão", ex.Message);
        Assert.Equal(1, gateway.CallCount);
    }

    [Fact]
    public void Execute_NullCommand_Throws()
    {
        var useCase = new SwapSaleItemUseCase(new FakeGateway());
        Assert.Throws<ArgumentNullException>(() => useCase.Execute(null!));
    }

    [Fact]
    public void Execute_InvalidSaleId_Throws_WithoutCallingGateway()
    {
        var gateway = new FakeGateway();
        var useCase = new SwapSaleItemUseCase(gateway);

        var ex = Assert.Throws<ArgumentException>(() =>
            useCase.Execute(new SwapSaleItemCommand
            {
                SaleId = 0,
                ItemId = 1,
                NewProductId = 2,
            }));

        Assert.Contains("SaleId", ex.Message);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Execute_InvalidItemId_Throws_WithoutCallingGateway()
    {
        var gateway = new FakeGateway();
        var useCase = new SwapSaleItemUseCase(gateway);

        var ex = Assert.Throws<ArgumentException>(() =>
            useCase.Execute(new SwapSaleItemCommand
            {
                SaleId = 1,
                ItemId = -5,
                NewProductId = 2,
            }));

        Assert.Contains("ItemId", ex.Message);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Execute_InvalidNewProductId_Throws_WithoutCallingGateway()
    {
        var gateway = new FakeGateway();
        var useCase = new SwapSaleItemUseCase(gateway);

        var ex = Assert.Throws<ArgumentException>(() =>
            useCase.Execute(new SwapSaleItemCommand
            {
                SaleId = 1,
                ItemId = 2,
                NewProductId = 0,
            }));

        Assert.Contains("NewProductId", ex.Message);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Constructor_NullGateway_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SwapSaleItemUseCase(null!));
    }

    private static SwapSaleItemCommand ValidCommand() =>
        new()
        {
            SaleId = 9,
            ItemId = 3,
            NewProductId = 7,
            KeepLinePrice = false,
            NewQuantity = 2,
            ConfirmedPayments =
            [
                new SalePayment { PaymentType = "Dinheiro", Amount = 40 },
            ],
            CashReceived = 40,
            CustomerPersonId = 5,
        };

    private sealed class FakeGateway : ISwapSaleItemGateway
    {
        public SwapSaleItemResult Result { get; set; } = new() { SaleId = 1 };
        public Exception? Exception { get; set; }
        public int CallCount { get; private set; }
        public SwapSaleItemCommand? LastCommand { get; private set; }

        public SwapSaleItemResult Swap(SwapSaleItemCommand command)
        {
            CallCount++;
            LastCommand = command;
            if (Exception is not null)
                throw Exception;
            return Result;
        }
    }
}
