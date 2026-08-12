using SGDB.Application.Sales;

namespace SGDB.Tests;

public class ChangeSalePaymentUseCaseTests
{
    [Fact]
    public void Execute_Success_CallsGatewayOnce_AndReturnsResult()
    {
        var expected = new ChangeSalePaymentResult { SaleId = 55 };
        var gateway = new FakeGateway { Result = expected };
        var useCase = new ChangeSalePaymentUseCase(gateway);

        var command = ValidCommand();
        var result = useCase.Execute(command);

        Assert.Equal(1, gateway.CallCount);
        Assert.Same(command, gateway.LastCommand);
        Assert.Equal(55, result.SaleId);
    }

    [Fact]
    public void Execute_GatewayThrows_Propagates()
    {
        var gateway = new FakeGateway
        {
            Exception = new InvalidOperationException("já cancelada"),
        };
        var useCase = new ChangeSalePaymentUseCase(gateway);

        var ex = Assert.Throws<InvalidOperationException>(() => useCase.Execute(ValidCommand()));
        Assert.Equal("já cancelada", ex.Message);
        Assert.Equal(1, gateway.CallCount);
    }

    [Fact]
    public void Execute_NullCommand_Throws()
    {
        var useCase = new ChangeSalePaymentUseCase(new FakeGateway());
        Assert.Throws<ArgumentNullException>(() => useCase.Execute(null!));
    }

    [Fact]
    public void Execute_InvalidSaleId_Throws_WithoutCallingGateway()
    {
        var gateway = new FakeGateway();
        var useCase = new ChangeSalePaymentUseCase(gateway);

        var ex = Assert.Throws<ArgumentException>(() =>
            useCase.Execute(new ChangeSalePaymentCommand
            {
                SaleId = 0,
                Payments = [],
            }));

        Assert.Contains("SaleId", ex.Message);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Execute_NullPayments_Throws_WithoutCallingGateway()
    {
        var gateway = new FakeGateway();
        var useCase = new ChangeSalePaymentUseCase(gateway);

        Assert.Throws<ArgumentException>(() =>
            useCase.Execute(new ChangeSalePaymentCommand
            {
                SaleId = 1,
                Payments = null!,
            }));

        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Constructor_NullGateway_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ChangeSalePaymentUseCase(null!));
    }

    private static ChangeSalePaymentCommand ValidCommand() =>
        new()
        {
            SaleId = 9,
            Payments =
            [
                new SalePayment { PaymentType = "Pix", Amount = 20 },
            ],
            CashReceived = 0,
            CustomerPersonId = 3,
        };

    private sealed class FakeGateway : IChangeSalePaymentGateway
    {
        public ChangeSalePaymentResult Result { get; set; } = new() { SaleId = 1 };
        public Exception? Exception { get; set; }
        public int CallCount { get; private set; }
        public ChangeSalePaymentCommand? LastCommand { get; private set; }

        public ChangeSalePaymentResult Change(ChangeSalePaymentCommand command)
        {
            CallCount++;
            LastCommand = command;
            if (Exception is not null)
                throw Exception;
            return Result;
        }
    }
}
