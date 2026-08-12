using SGDB.Application.Sales;

namespace SGDB.Tests;

public class PreviewSwapSaleItemUseCaseTests
{
    [Fact]
    public void Execute_Success_CallsGatewayOnce_AndReturnsResult()
    {
        var expected = new PreviewSwapSaleItemResult
        {
            SaleId = 10,
            OldTotal = 90,
            NewTotal = 110,
            Difference = 20,
            RequiresPaymentConfirmation = true,
        };
        var gateway = new FakeGateway { Result = expected };
        var useCase = new PreviewSwapSaleItemUseCase(gateway);

        var command = ValidCommand();
        var result = useCase.Execute(command);

        Assert.Equal(1, gateway.CallCount);
        Assert.Same(command, gateway.LastCommand);
        Assert.Equal(10, result.SaleId);
        Assert.Equal(20, result.Difference);
        Assert.True(result.RequiresPaymentConfirmation);
    }

    [Fact]
    public void Execute_GatewayThrows_Propagates()
    {
        var gateway = new FakeGateway
        {
            Exception = new InvalidOperationException("venda cancelada"),
        };
        var useCase = new PreviewSwapSaleItemUseCase(gateway);

        var ex = Assert.Throws<InvalidOperationException>(() => useCase.Execute(ValidCommand()));
        Assert.Equal("venda cancelada", ex.Message);
        Assert.Equal(1, gateway.CallCount);
    }

    [Fact]
    public void Execute_NullCommand_Throws()
    {
        var useCase = new PreviewSwapSaleItemUseCase(new FakeGateway());
        Assert.Throws<ArgumentNullException>(() => useCase.Execute(null!));
    }

    [Fact]
    public void Execute_InvalidSaleId_Throws_WithoutCallingGateway()
    {
        var gateway = new FakeGateway();
        var useCase = new PreviewSwapSaleItemUseCase(gateway);

        var ex = Assert.Throws<ArgumentException>(() =>
            useCase.Execute(new PreviewSwapSaleItemCommand
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
        var useCase = new PreviewSwapSaleItemUseCase(gateway);

        var ex = Assert.Throws<ArgumentException>(() =>
            useCase.Execute(new PreviewSwapSaleItemCommand
            {
                SaleId = 1,
                ItemId = 0,
                NewProductId = 2,
            }));

        Assert.Contains("ItemId", ex.Message);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Execute_InvalidNewProductId_Throws_WithoutCallingGateway()
    {
        var gateway = new FakeGateway();
        var useCase = new PreviewSwapSaleItemUseCase(gateway);

        var ex = Assert.Throws<ArgumentException>(() =>
            useCase.Execute(new PreviewSwapSaleItemCommand
            {
                SaleId = 1,
                ItemId = 2,
                NewProductId = -1,
            }));

        Assert.Contains("NewProductId", ex.Message);
        Assert.Equal(0, gateway.CallCount);
    }

    [Fact]
    public void Constructor_NullGateway_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PreviewSwapSaleItemUseCase(null!));
    }

    private static PreviewSwapSaleItemCommand ValidCommand() =>
        new()
        {
            SaleId = 9,
            ItemId = 3,
            NewProductId = 7,
            KeepLinePrice = true,
            NewQuantity = 2,
        };

    private sealed class FakeGateway : IPreviewSwapSaleItemGateway
    {
        public PreviewSwapSaleItemResult Result { get; set; } = new() { SaleId = 1 };
        public Exception? Exception { get; set; }
        public int CallCount { get; private set; }
        public PreviewSwapSaleItemCommand? LastCommand { get; private set; }

        public PreviewSwapSaleItemResult Preview(PreviewSwapSaleItemCommand command)
        {
            CallCount++;
            LastCommand = command;
            if (Exception is not null)
                throw Exception;
            return Result;
        }
    }
}
