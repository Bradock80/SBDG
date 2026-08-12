using SGDB.Application.Sales;

namespace SGDB.Tests;

public class FinalizeSaleUseCaseTests
{
    [Fact]
    public void Execute_Success_CallsGatewayOnce_AndReturnsResult()
    {
        var expected = new SaleExecutionResult
        {
            SaleId = 99,
            Total = 45.5,
            ChangeAmount = 4.5,
            CashReceived = 50,
        };
        var gateway = new FakeGateway { Result = expected };
        var useCase = new FinalizeSaleUseCase(gateway);

        var command = ValidCommand();
        var result = useCase.Execute(command);

        Assert.Equal(1, gateway.CallCount);
        Assert.Same(command, gateway.LastCommand);
        Assert.Equal(99, result.SaleId);
        Assert.Equal(45.5, result.Total);
        Assert.Equal(4.5, result.ChangeAmount);
        Assert.Equal(50, result.CashReceived);
    }

    [Fact]
    public void Execute_GatewayThrows_Propagates()
    {
        var gateway = new FakeGateway
        {
            Exception = new InvalidOperationException("sem permissão"),
        };
        var useCase = new FinalizeSaleUseCase(gateway);

        var ex = Assert.Throws<InvalidOperationException>(() => useCase.Execute(ValidCommand()));
        Assert.Equal("sem permissão", ex.Message);
        Assert.Equal(1, gateway.CallCount);
    }

    [Fact]
    public void Execute_NullCommand_Throws()
    {
        var useCase = new FinalizeSaleUseCase(new FakeGateway());
        Assert.Throws<ArgumentNullException>(() => useCase.Execute(null!));
    }

    [Fact]
    public void Execute_NullItems_Throws_WithoutCallingGateway()
    {
        var gateway = new FakeGateway();
        var useCase = new FinalizeSaleUseCase(gateway);

        Assert.Throws<ArgumentException>(() =>
            useCase.Execute(new FinalizeSaleCommand { Items = null! }));

        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Execute_EmptyItems_StillCallsGateway()
    {
        // Validação de carrinho vazio permanece no PdvService (PdvException).
        var gateway = new FakeGateway
        {
            Result = new SaleExecutionResult { SaleId = 1 },
        };
        var useCase = new FinalizeSaleUseCase(gateway);

        useCase.Execute(new FinalizeSaleCommand { Items = [] });

        Assert.Equal(1, gateway.CallCount);
        Assert.Empty(gateway.LastCommand!.Items);
    }

    [Fact]
    public void Constructor_NullGateway_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FinalizeSaleUseCase(null!));
    }

    private static FinalizeSaleCommand ValidCommand() =>
        new()
        {
            Items =
            [
                new SaleLine
                {
                    ProductId = 3,
                    Quantity = 2,
                    UnitPrice = 10,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = 20,
            Discount = 0,
            Surcharge = 0,
        };

    private sealed class FakeGateway : IFinalizeSaleGateway
    {
        public SaleExecutionResult Result { get; set; } = new() { SaleId = 1 };
        public Exception? Exception { get; set; }
        public int CallCount { get; private set; }
        public FinalizeSaleCommand? LastCommand { get; private set; }

        public SaleExecutionResult Finalize(FinalizeSaleCommand command)
        {
            CallCount++;
            LastCommand = command;
            if (Exception is not null)
                throw Exception;
            return Result;
        }
    }
}
