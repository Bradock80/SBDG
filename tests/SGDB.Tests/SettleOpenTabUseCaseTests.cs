using SGDB.Application.OpenTabs;
using SGDB.Application.Sales;

namespace SGDB.Tests;

public class SettleOpenTabUseCaseTests
{
    [Fact]
    public void Execute_Success_CallsGatewayOnce_AndReturnsResult()
    {
        var expected = new SaleExecutionResult
        {
            SaleId = 42,
            Total = 30,
            ChangeAmount = 0,
            CashReceived = 30,
        };
        var gateway = new FakeGateway { Result = expected };
        var useCase = new SettleOpenTabUseCase(gateway);

        var command = ValidCommand();
        var result = useCase.Execute(command);

        Assert.Equal(1, gateway.CallCount);
        Assert.Same(command, gateway.LastCommand);
        Assert.Equal(42, result.SaleId);
        Assert.Equal(30, result.Total);
        Assert.Equal(0, result.ChangeAmount);
        Assert.Equal(30, result.CashReceived);
    }

    [Fact]
    public void Execute_GatewayThrows_Propagates()
    {
        var gateway = new FakeGateway
        {
            Exception = new InvalidOperationException("já fechado"),
        };
        var useCase = new SettleOpenTabUseCase(gateway);

        var ex = Assert.Throws<InvalidOperationException>(() => useCase.Execute(ValidCommand()));
        Assert.Equal("já fechado", ex.Message);
        Assert.Equal(1, gateway.CallCount);
    }

    [Fact]
    public void Execute_NullCommand_Throws()
    {
        var useCase = new SettleOpenTabUseCase(new FakeGateway());
        Assert.Throws<ArgumentNullException>(() => useCase.Execute(null!));
    }

    [Fact]
    public void Execute_InvalidTabId_Throws_WithoutCallingGateway()
    {
        var gateway = new FakeGateway();
        var useCase = new SettleOpenTabUseCase(gateway);

        var ex = Assert.Throws<ArgumentException>(() =>
            useCase.Execute(new SettleOpenTabCommand
            {
                TabId = 0,
                Items = [],
            }));

        Assert.Contains("TabId", ex.Message);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Execute_NullItems_Throws_WithoutCallingGateway()
    {
        var gateway = new FakeGateway();
        var useCase = new SettleOpenTabUseCase(gateway);

        Assert.Throws<ArgumentException>(() =>
            useCase.Execute(new SettleOpenTabCommand
            {
                TabId = 1,
                Items = null!,
            }));

        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Constructor_NullGateway_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SettleOpenTabUseCase(null!));
    }

    private static SettleOpenTabCommand ValidCommand() =>
        new()
        {
            TabId = 7,
            Items =
            [
                new SaleLine
                {
                    ProductId = 1,
                    Quantity = 2,
                    UnitPrice = 15,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = 30,
        };

    private sealed class FakeGateway : IOpenTabSettlementGateway
    {
        public SaleExecutionResult Result { get; set; } = new() { SaleId = 1 };
        public Exception? Exception { get; set; }
        public int CallCount { get; private set; }
        public SettleOpenTabCommand? LastCommand { get; private set; }

        public SaleExecutionResult Settle(SettleOpenTabCommand command)
        {
            CallCount++;
            LastCommand = command;
            if (Exception is not null)
                throw Exception;
            return Result;
        }
    }
}
