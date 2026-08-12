using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public static class DemoDataService
{
    public sealed class SeedResult
    {
        public int SuppliersCreated { get; init; }
        public int SuppliersSkipped { get; init; }
        public int ProductsCreated { get; init; }
        public int ProductsSkipped { get; init; }
        public int ClientsCreated { get; init; }
        public int ClientsSkipped { get; init; }
        public int StockReplenished { get; init; }
    }

    public static SeedResult SeedComprasTestData()
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("dados de demonstração");
        var suppliersCreated = 0;
        var suppliersSkipped = 0;
        var productsCreated = 0;
        var productsSkipped = 0;
        var clientsCreated = 0;
        var clientsSkipped = 0;
        var stockReplenished = 0;

        foreach (var supplier in DefaultSuppliers())
        {
            if (PersonExistsByCnpj(supplier.CpfCnpj))
            {
                suppliersSkipped++;
                continue;
            }

            PersonService.Create(supplier, requireClienteRole: false);
            suppliersCreated++;
        }

        foreach (var client in DefaultClients())
        {
            if (PersonExistsByCnpj(client.CpfCnpj))
            {
                clientsSkipped++;
                continue;
            }

            PersonService.Create(client, requireClienteRole: false);
            clientsCreated++;
        }

        foreach (var product in DefaultProducts())
        {
            if (ProductExistsByCode(product.Code))
            {
                productsSkipped++;
                if (ReplenishStock(product.Code!, product.Stock))
                    stockReplenished++;
                continue;
            }

            ProductService.Create(product);
            productsCreated++;
        }

        return new SeedResult
        {
            SuppliersCreated = suppliersCreated,
            SuppliersSkipped = suppliersSkipped,
            ProductsCreated = productsCreated,
            ProductsSkipped = productsSkipped,
            ClientsCreated = clientsCreated,
            ClientsSkipped = clientsSkipped,
            StockReplenished = stockReplenished,
        };
    }

    private static IEnumerable<PersonInput> DefaultSuppliers() =>
    [
        new PersonInput
        {
            Name = "AMBEV S.A. — DIST. SÃO PAULO",
            TradeName = "AMBEV",
            CpfCnpj = "07526557000100",
            RgIe = "ISENTO",
            State = "SP",
            City = "SAO PAULO",
            Neighborhood = "JAGUARE",
            Address = "RUA DR ANTONIO NOVAES DE AMORIM",
            AddressNumber = "1000",
            Cep = "05317900",
            Phone = "1130001000",
            Roles = new PersonRoles { Ativo = true, Fornecedores = true },
            Notes = "FORNECEDOR TESTE — CERVEJAS",
        },
        new PersonInput
        {
            Name = "COCA-COLA FEMSA BRASIL LTDA",
            TradeName = "FEMSA",
            CpfCnpj = "45997418000153",
            RgIe = "ISENTO",
            State = "RJ",
            City = "RIO DE JANEIRO",
            Neighborhood = "BENFICA",
            Address = "AVENIDA BRASIL",
            AddressNumber = "3500",
            Cep = "20930040",
            Phone = "2125095000",
            Roles = new PersonRoles { Ativo = true, Fornecedores = true },
            Notes = "FORNECEDOR TESTE — REFRIGERANTES",
        },
        new PersonInput
        {
            Name = "INDAIÁ BRASIL ÁGUAS MINERAIS LTDA",
            TradeName = "INDAIÁ",
            CpfCnpj = "45394508000133",
            RgIe = "ISENTO",
            State = "MG",
            City = "LAMBARI",
            Neighborhood = "DISTRITO INDUSTRIAL",
            Address = "RODOVIA MG-230",
            AddressNumber = "S/N",
            Cep = "37480000",
            Phone = "3532211000",
            Roles = new PersonRoles { Ativo = true, Fornecedores = true },
            Notes = "FORNECEDOR TESTE — AGUA MINERAL",
        },
        new PersonInput
        {
            Name = "CERVEJARIA PETROPOLIS S/A",
            TradeName = "PETROPOLIS",
            CpfCnpj = "73410326000173",
            RgIe = "ISENTO",
            State = "RJ",
            City = "PETROPOLIS",
            Neighborhood = "QUITANDINHA",
            Address = "RUA DA CERVEJA",
            AddressNumber = "550",
            Cep = "25651000",
            Phone = "2422223000",
            Roles = new PersonRoles { Ativo = true, Fornecedores = true },
            Notes = "FORNECEDOR TESTE — ITAIPAVA / BLACK PRINCESS",
        },
    ];

    private static IEnumerable<PersonInput> DefaultClients() =>
    [
        new PersonInput
        {
            Name = "BAR DO ZÉ",
            TradeName = "BAR DO ZE",
            CpfCnpj = "12345678000190",
            State = "SP",
            City = "SAO PAULO",
            Neighborhood = "CENTRO",
            Address = "RUA DAS FLORES",
            AddressNumber = "100",
            Phone = "11999990001",
            Roles = PersonRoles.ForNewCliente(),
            Notes = "CLIENTE TESTE — FIADO PDV",
        },
        new PersonInput
        {
            Name = "MERCEARIA SÃO JOSÉ",
            CpfCnpj = "98765432000111",
            State = "SP",
            City = "OSASCO",
            Neighborhood = "JARDIM DAS FLORES",
            Address = "AV BRASIL",
            AddressNumber = "450",
            Phone = "11999990002",
            Roles = PersonRoles.ForNewCliente(),
            Notes = "CLIENTE TESTE",
        },
        new PersonInput
        {
            Name = "JOÃO DA SILVA",
            CpfCnpj = "52998224725",
            State = "SP",
            City = "SAO PAULO",
            Phone = "11988887777",
            Roles = PersonRoles.ForNewCliente(),
            Notes = "CLIENTE PF TESTE — FIADO",
        },
    ];

    private static IEnumerable<ProductInput> DefaultProducts() =>
    [
        new ProductInput
        {
            Code = "SKOL350",
            Barcode = "7891000100103",
            Name = "CERVEJA SKOL 350ML LATA",
            GroupName = "CERVEJAS",
            Unit = "UN",
            CostPrice = 1.80,
            SalePrice = 3.50,
            MinStock = 24,
            Stock = 120,
        },
        new ProductInput
        {
            Code = "BRAHMA350",
            Barcode = "7891149200507",
            Name = "CERVEJA BRAHMA 350ML LATA",
            GroupName = "CERVEJAS",
            Unit = "UN",
            CostPrice = 1.75,
            SalePrice = 3.50,
            MinStock = 24,
            Stock = 96,
        },
        new ProductInput
        {
            Code = "HEIN330",
            Barcode = "78905441",
            Name = "CERVEJA HEINEKEN 330ML LONG NECK",
            GroupName = "CERVEJAS",
            Unit = "UN",
            CostPrice = 3.20,
            SalePrice = 5.90,
            MinStock = 12,
            Stock = 48,
        },
        new ProductInput
        {
            Code = "ITAIPAVA350",
            Barcode = "7891149201234",
            Name = "CERVEJA ITAIPAVA 350ML LATA",
            GroupName = "CERVEJAS",
            Unit = "UN",
            CostPrice = 1.65,
            SalePrice = 3.20,
            MinStock = 24,
            Stock = 72,
        },
        new ProductInput
        {
            Code = "COCA2L",
            Barcode = "7894900011517",
            Name = "REFRIGERANTE COCA-COLA 2L",
            GroupName = "REFRIGERANTES",
            Unit = "UN",
            CostPrice = 5.50,
            SalePrice = 9.90,
            MinStock = 12,
            Stock = 36,
        },
        new ProductInput
        {
            Code = "GUARA2L",
            Barcode = "7892840810159",
            Name = "REFRIGERANTE GUARANA ANTARCTICA 2L",
            GroupName = "REFRIGERANTES",
            Unit = "UN",
            CostPrice = 4.80,
            SalePrice = 8.90,
            MinStock = 12,
            Stock = 30,
        },
        new ProductInput
        {
            Code = "AGUA20L",
            Barcode = "7898918200010",
            Name = "AGUA MINERAL 20L GALAO",
            GroupName = "AGUA",
            Unit = "UN",
            CostPrice = 4.00,
            SalePrice = 12.00,
            MinStock = 10,
            Stock = 25,
        },
        new ProductInput
        {
            Code = "AGUA500",
            Barcode = "7891010000101",
            Name = "AGUA MINERAL 500ML",
            GroupName = "AGUA",
            Unit = "UN",
            CostPrice = 0.45,
            SalePrice = 2.00,
            MinStock = 48,
            Stock = 200,
        },
        new ProductInput
        {
            Code = "RED250",
            Barcode = "7891811060010",
            Name = "ENERGETICO RED BULL 250ML",
            GroupName = "ENERGETICOS",
            Unit = "UN",
            CostPrice = 6.50,
            SalePrice = 12.00,
            MinStock = 12,
            Stock = 24,
        },
        new ProductInput
        {
            Code = "CRYSTAL5L",
            Barcode = "7894900700015",
            Name = "AGUA CRYSTAL 5L",
            GroupName = "AGUA",
            Unit = "UN",
            CostPrice = 2.80,
            SalePrice = 6.50,
            MinStock = 12,
            Stock = 40,
        },
    ];

    private static bool ReplenishStock(string code, double minStock)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE products
            SET stock = CASE WHEN stock < $stock THEN $stock ELSE stock END
            WHERE UPPER(code) = $code AND active = 1;
            """;
        cmd.Parameters.AddWithValue("$code", code.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("$stock", minStock);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static bool PersonExistsByCnpj(string? cnpj)
    {
        var digits = TextNorm.DigitsOnly(cnpj, 14);
        if (digits is null)
            return false;

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM people WHERE cpf_cnpj = $cnpj LIMIT 1;";
        cmd.Parameters.AddWithValue("$cnpj", digits);
        return cmd.ExecuteScalar() is not null;
    }

    private static bool ProductExistsByCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM products WHERE UPPER(code) = $code LIMIT 1;";
        cmd.Parameters.AddWithValue("$code", code.Trim().ToUpperInvariant());
        return cmd.ExecuteScalar() is not null;
    }
}
