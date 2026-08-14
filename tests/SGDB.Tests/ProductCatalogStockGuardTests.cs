using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 63B — Cadastro não cria/apaga stock nem stock_fridge.
/// Quantidade só muda por compra, ajuste, inventário, transferência ou venda.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class ProductCatalogStockGuardTests
{
    [Fact]
    public void Update_IgnoraStockEFridge_AlteraNomeEPreco()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedStock(100);
        TestDataHelper.SetProductFridge(id, 20);
        var movBefore = TestDataHelper.CountMovements(id);

        var updated = ProductService.Update(id, TestDataHelper.CatalogInputFrom(
            ProductService.GetById(id)!,
            i =>
            {
                i.Name = "PRODUTO 63B RENOMEADO";
                i.SalePrice = 12.5;
                i.CostPrice = 6;
                i.Stock = 999;
                i.StockFridge = 888;
            }));

        Assert.Equal("PRODUTO 63B RENOMEADO", updated.Name);
        Assert.Equal(12.5, updated.SalePrice);
        Assert.Equal(6, updated.CostPrice);
        Assert.Equal(100, updated.Stock);
        Assert.Equal(20, updated.StockFridge);
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
        Assert.Equal(20, TestDataHelper.GetProductFridge(id));
        Assert.Equal(movBefore, TestDataHelper.CountMovements(id));
    }

    [Fact]
    public void UpdateLocal_CaminhoPutRede_IgnoraStockEFridge()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedStock(40);
        TestDataHelper.SetProductFridge(id, 7);

        var updated = ProductService.UpdateLocal(id, TestDataHelper.CatalogInputFrom(
            ProductService.GetById(id)!,
            i =>
            {
                i.Name = "VIA PUT REDE";
                i.Stock = 1;
                i.StockFridge = 99;
            }));

        Assert.Equal("VIA PUT REDE", updated.Name);
        Assert.Equal(40, updated.Stock);
        Assert.Equal(7, updated.StockFridge);
    }

    [Fact]
    public void Create_ForcaStockEFridgeZero()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");

        var created = ProductService.Create(new ProductInput
        {
            Code = "C63B",
            Name = "NOVO 63B",
            Unit = "UN",
            CostPrice = 2,
            SalePrice = 5,
            MinStock = 8,
            Stock = 100,
            StockFridge = 20,
            StockFridgeMin = 12,
            Active = true,
        });

        Assert.Equal(0, created.Stock);
        Assert.Equal(0, created.StockFridge);
        Assert.Equal(12, created.StockFridgeMin);
        Assert.Equal(8, created.MinStock);
        Assert.Equal(0, TestDataHelper.GetProductStock(created.Id));
        Assert.Equal(0, TestDataHelper.GetProductFridge(created.Id));
        Assert.Equal(0, TestDataHelper.CountMovements(created.Id));
    }

    [Fact]
    public void Create_DepoisAjusteSaldoInicial_GravaDepositoComMovement()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var created = ProductService.Create(new ProductInput
        {
            Code = "INI63",
            Name = "SALDO INICIAL 63B",
            Unit = "UN",
            CostPrice = 2,
            SalePrice = 5,
            Stock = 100,
            Active = true,
        });
        Assert.Equal(0, created.Stock);

        var result = StockService.Adjust(created.Id, StockAdjustMode.Saldo, newStock: 80,
            notes: "Saldo inicial");

        Assert.Equal(0, result.StockBefore);
        Assert.Equal(80, result.StockAfter);
        Assert.Equal(80, TestDataHelper.GetProductStock(created.Id));
        Assert.Equal(0, TestDataHelper.GetProductFridge(created.Id));
        Assert.Equal(1, TestDataHelper.CountMovements(created.Id));
    }

    [Fact]
    public void Update_NaoAlteraLotesNemCriaDivergencia()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedStock(100);
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = id,
            Quantity = 100,
            LotNumber = "L63B",
            ExpiryDate = DateTime.Today.AddDays(40),
        });
        Assert.Equal(100, TestDataHelper.SumLots(id));

        ProductService.Update(id, TestDataHelper.CatalogInputFrom(
            ProductService.GetById(id)!,
            i =>
            {
                i.Stock = 150;
                i.StockFridge = 10;
                i.Name = "COM LOTE 63B";
            }));

        Assert.Equal(100, TestDataHelper.GetProductStock(id));
        Assert.Equal(100, TestDataHelper.SumLots(id));
        Assert.Equal("COM LOTE 63B", ProductService.GetById(id)!.Name);
    }

    [Fact]
    public void UpdateCadastral_DepoisTransferenciaIdaEVolta()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedStock(20);

        ProductService.Update(id, TestDataHelper.CatalogInputFrom(
            ProductService.GetById(id)!,
            i =>
            {
                i.Name = "TRANSFER 63B";
                i.StockFridgeMin = 6;
                i.Stock = 999;
                i.StockFridge = 888;
            }));

        Assert.Equal(20, TestDataHelper.GetProductStock(id));
        Assert.Equal(0, TestDataHelper.GetProductFridge(id));

        StockService.TransferWarehouseToFridge(id, 10);
        Assert.Equal(10, TestDataHelper.GetProductStock(id));
        Assert.Equal(10, TestDataHelper.GetProductFridge(id));

        StockService.TransferFridgeToWarehouse(id, 10);
        Assert.Equal(20, TestDataHelper.GetProductStock(id));
        Assert.Equal(0, TestDataHelper.GetProductFridge(id));
        Assert.Equal("TRANSFER 63B", ProductService.GetById(id)!.Name);
        Assert.Equal(6, ProductService.GetById(id)!.StockFridgeMin);
    }

    [Fact]
    public void Update_PermiteMinimosEFator_SemMexerSaldo()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedStock(30);
        TestDataHelper.SetProductFridge(id, 4);

        var updated = ProductService.Update(id, TestDataHelper.CatalogInputFrom(
            ProductService.GetById(id)!,
            i =>
            {
                i.MinStock = 9;
                i.StockFridgeMin = 3;
                i.Extra.FatorEmbalagem = 12;
                i.Stock = 1;
                i.StockFridge = 1;
            }));

        Assert.Equal(9, updated.MinStock);
        Assert.Equal(3, updated.StockFridgeMin);
        Assert.Equal(12, ProductExtra.Parse(updated.ExtraJson).FatorEmbalagem);
        Assert.Equal(30, updated.Stock);
        Assert.Equal(4, updated.StockFridge);
    }

    private static int SeedStock(double stock) =>
        TestDataHelper.SeedSimpleProduct(stock, 5, 2, $"P{Guid.NewGuid():N}"[..8], "PROD 63B");
}
