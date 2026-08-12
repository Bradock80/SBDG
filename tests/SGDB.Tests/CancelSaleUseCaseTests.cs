using SGDB.Application.Sales;

namespace SGDB.Tests;

public class CancelSaleUseCaseTests
{
    [Fact]
    public void Execute_Success_CallsGatewayOnce()
    {
        var gateway = new FakeGateway();
        var useCase = new CancelSaleUseCase(gateway);
        var command = new CancelSaleCommand { SaleId = 42 };

        useCase.Execute(command);

        Assert.Equal(1, gateway.CallCount);
        Assert.Same(command, gateway.LastCommand);
    }

    [Fact]
    public void Execute_GatewayThrows_Propagates()
    {
        var gateway = new FakeGateway
        {
            Exception = new InvalidOperationException("já cancelada"),
        };
        var useCase = new CancelSaleUseCase(gateway);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            useCase.Execute(new CancelSaleCommand { SaleId = 1 }));

        Assert.Equal("já cancelada", ex.Message);
        Assert.Equal(1, gateway.CallCount);
    }

    [Fact]
    public void Execute_NullCommand_Throws()
    {
        var useCase = new CancelSaleUseCase(new FakeGateway());
        Assert.Throws<ArgumentNullException>(() => useCase.Execute(null!));
    }

    [Fact]
    public void Execute_InvalidSaleId_Throws_WithoutCallingGateway()
    {
        var gateway = new FakeGateway();
        var useCase = new CancelSaleUseCase(gateway);

        var ex = Assert.Throws<ArgumentException>(() =>
            useCase.Execute(new CancelSaleCommand { SaleId = 0 }));

        Assert.Contains("SaleId", ex.Message);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Constructor_NullGateway_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CancelSaleUseCase(null!));
    }

    private sealed class FakeGateway : ICancelSaleGateway
    {
        public Exception? Exception { get; set; }
        public int CallCount { get; private set; }
        public CancelSaleCommand? LastCommand { get; private set; }

        public void Cancel(CancelSaleCommand command)
        {
            CallCount++;
            LastCommand = command;
            if (Exception is not null)
                throw Exception;
        }
    }
}
