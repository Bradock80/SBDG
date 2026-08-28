using System.IO;
using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// 70B3A-D1 — smoke funcional da UI sobre ui_test.db descartável.
/// Deixa o banco pronto em %TEMP%\SGDB.Tests\70B3A-D1\ui_test.db para inspeção visual.
/// Nunca toca AppData\SGDB\deposito.db.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class LotCoverageUiSmokeD1Tests
{
    public static string TestDbDir =>
        Path.Combine(Path.GetTempPath(), "SGDB.Tests", "70B3A-D1");

    public static string TestDbPath => Path.Combine(TestDbDir, "ui_test.db");

    private static readonly DateTime ExpSep = new(2026, 9, 30);
    private static readonly DateTime ExpNov = new(2026, 11, 30);
    private static readonly DateTime ExpOct = new(2026, 10, 15);

    [Fact]
    public void D1_SmokeCompleto_BancoIsoladoEOperacoesDaUi()
    {
        var normalDb = DatabaseService.DefaultStoreDatabasePath;
        Directory.CreateDirectory(TestDbDir);

        // Pasta exclusiva desta execução (evita lock se SGDB visual ainda estiver aberto em ui_test.db).
        var runDir = Path.Combine(TestDbDir, "run-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(runDir);
        var runDbPath = Path.Combine(runDir, "ui_test.db");

        DatabaseService.Initialize(runDbPath);
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

        Assert.True(DatabaseService.IsIsolatedDatabasePath(runDbPath));
        Assert.False(string.Equals(
            Path.GetFullPath(runDbPath),
            Path.GetFullPath(normalDb),
            StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Path.GetFullPath(runDbPath), Path.GetFullPath(DatabaseService.DatabasePath));

        if (SetupService.NeedsInitialSetup())
        {
            SetupService.CompleteInitialSetup(
                new CompanyProfile { NomeFantasia = "LOJA TESTE 70B3A-D1" },
                adminLogin: "admin_d1",
                adminNome: "Admin D1",
                password: "teste1234");
        }

        TestDataHelper.SetSessionRole("admin");

        // ---- CENÁRIO A: 100 sem rastreamento → 60 + 40 ----
        var idA = Seed("TV100", "TESTE VALIDADE CEM UN", stock: 100, fridge: 0, cost: 3);
        var snapA0 = LotCoverageService.GetSnapshot(idA);
        Assert.Equal(100, snapA0.Stock);
        Assert.Equal(0, snapA0.TrackedQuantity);
        Assert.Equal(100, snapA0.UntrackedQuantity);
        Assert.Contains("geladeira", LotCoverageUi.FridgeDisclaimer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manter", LotCoverageUi.SelectProductHint, StringComparison.OrdinalIgnoreCase);

        LotCoverageService.AddCoverage(new LotCoverageAddInput
        {
            ProductId = idA, Quantity = 60, ExpiryDate = ExpSep, LotNumber = "",
        });
        var snapA1 = LotCoverageService.GetSnapshot(idA);
        Assert.Equal(100, snapA1.Stock);
        Assert.Equal(60, snapA1.TrackedQuantity);
        Assert.Equal(40, snapA1.UntrackedQuantity);
        Assert.Equal("—", LotCoverageUi.ToRows(snapA1)[0].LotDisplay);

        LotCoverageService.AddCoverage(new LotCoverageAddInput
        {
            ProductId = idA, Quantity = 40, ExpiryDate = ExpNov, LotNumber = "",
        });
        var snapA2 = LotCoverageService.GetSnapshot(idA);
        Assert.Equal(100, snapA2.Stock);
        Assert.Equal(100, snapA2.TrackedQuantity);
        Assert.Equal(0, snapA2.UntrackedQuantity);
        Assert.Equal(2, snapA2.Lines.Count);
        Assert.Equal(100, TestDataHelper.GetProductStock(idA));
        Assert.Equal(0, TestDataHelper.GetProductFridge(idA));

        var rowsA = LotCoverageUi.ToRows(snapA2);
        Assert.Contains(rowsA, r => r.QtyDisplay == "60" && r.ExpiryDisplay == "30/09/2026" && r.LotDisplay == "—");
        Assert.Contains(rowsA, r => r.QtyDisplay == "40" && r.ExpiryDisplay == "30/11/2026" && r.LotDisplay == "—");

        var ex50 = Assert.Throws<LotCoverageException>(() =>
            LotCoverageService.AddCoverage(new LotCoverageAddInput
            {
                ProductId = idA, Quantity = 50, ExpiryDate = ExpOct, LotNumber = "X",
            }));
        Assert.Equal(LotCoverageRules.QuantityExceedsUntracked, ex50.ErrorCode);
        Assert.Contains("sem rastreamento", LotCoverageUi.MapError(ex50));
        Assert.Equal(100, TestDataHelper.GetProductStock(idA));
        Assert.Equal(100, SumLots(idA));

        // ---- CENÁRIO B: parcial 60/40 ----
        var idB = Seed("TVPAR", "TESTE VALIDADE PARCIAL", 100, 0, 2.5);
        LotCoverageService.AddCoverage(new LotCoverageAddInput
        {
            ProductId = idB, Quantity = 60, ExpiryDate = ExpSep, LotNumber = "PAR",
        });
        var snapB = LotCoverageService.GetSnapshot(idB);
        Assert.Equal(60, snapB.TrackedQuantity);
        Assert.Equal(40, snapB.UntrackedQuantity);

        // ---- CENÁRIO C: compra × manual (mesmo lote/validade) ----
        var idC = Seed("TVORG", "TESTE ORIGEM COMPRA", 0, 0, 4);
        var supplierId = SeedSupplier();
        var purchaseId = PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-D1-ORIG",
            GerarEstoque = true,
            Items =
            [
                new PurchaseItemInput
                {
                    ProductId = idC,
                    ProductName = "TESTE ORIGEM COMPRA",
                    Quantity = 20,
                    UnitPrice = 2,
                    LotNumber = "ABC",
                    ExpiryDate = ExpSep,
                },
            ],
        }, closeOnSave: true);
        SetStock(idC, 50);
        var buyLotId = PurchaseService.ListPurchaseItemLots(purchaseId).Single().ProductLotId!.Value;
        var buyBefore = GetOrigin(buyLotId);

        var manualAdd = LotCoverageService.AddCoverage(new LotCoverageAddInput
        {
            ProductId = idC, Quantity = 30, ExpiryDate = ExpSep, LotNumber = "ABC",
        });
        var snapC = LotCoverageService.GetSnapshot(idC);
        Assert.Equal(2, snapC.Lines.Count);
        var uiC = LotCoverageUi.ToRows(snapC);
        Assert.Contains(uiC, r => r.IsPurchaseOrigin && r.OriginDisplay.StartsWith("Compra #"));
        Assert.Contains(uiC, r => !r.IsPurchaseOrigin && r.OriginDisplay == "Conferência manual");

        var splitBuy = Assert.Throws<LotCoverageException>(() =>
            LotCoverageService.SplitCoverage(new LotCoverageSplitInput
            {
                ProductLotId = buyLotId, DestinationQuantity = 5, DestinationExpiryDate = ExpNov,
                DestinationLotNumber = "ABC", Reason = "tentar split compra",
            }));
        Assert.Equal(LotCoverageRules.PurchaseOriginProtected, splitBuy.ErrorCode);
        Assert.Contains("não pode ser dividida", LotCoverageUi.MapError(splitBuy, "split"));

        var qtyBuy = Assert.Throws<LotCoverageException>(() =>
            LotCoverageService.CorrectQuantity(new LotCoverageQuantityInput
            {
                ProductLotId = buyLotId, Quantity = 10, Reason = "tentar qty",
            }));
        Assert.Equal(LotCoverageRules.PurchaseOriginProtected, qtyBuy.ErrorCode);
        Assert.Contains("não pode ser alterada", LotCoverageUi.MapError(qtyBuy, "quantity"));

        var remBuy = Assert.Throws<LotCoverageException>(() =>
            LotCoverageService.RemoveCoverage(new LotCoverageRemoveInput
            {
                ProductLotId = buyLotId, Reason = "tentar remove",
            }));
        Assert.Equal(LotCoverageRules.PurchaseOriginProtected, remBuy.ErrorCode);
        Assert.Contains("não pode ser removida", LotCoverageUi.MapError(remBuy, "remove"));

        var editBuy = LotCoverageService.EditCoverage(new LotCoverageEditInput
        {
            ProductLotId = buyLotId,
            ExpiryDate = ExpSep,
            LotNumber = "ABC-FIX",
            Reason = "correção etiqueta NF",
        });
        Assert.Equal(buyLotId, editBuy.ProductLotId);
        var buyAfter = GetOrigin(buyLotId);
        Assert.Equal(buyBefore.PurchaseId, buyAfter.PurchaseId);
        Assert.Equal(buyBefore.UnitCost, buyAfter.UnitCost);
        Assert.Equal(purchaseId, buyAfter.PurchaseId);

        // ---- Edit / Split / Qty / Remove manuais (produto B) ----
        var lineB = LotCoverageService.GetSnapshot(idB).Lines.Single();
        LotCoverageService.EditCoverage(new LotCoverageEditInput
        {
            ProductLotId = lineB.Id, ExpiryDate = ExpOct, LotNumber = "PAR-EDIT", Reason = "ajuste etiqueta",
        });
        var afterEdit = LotCoverageService.GetSnapshot(idB).Lines.Single();
        Assert.Equal(ExpOct, afterEdit.ExpiryDate);
        Assert.Equal("PAR-EDIT", afterEdit.LotNumber);
        Assert.Equal(60, afterEdit.Quantity);
        Assert.Equal(100, TestDataHelper.GetProductStock(idB));

        // produto para split 100
        var idSplit = Seed("TVSPL", "TESTE SPLIT MANUAL", 100, 0, 2);
        var splitOrigin = LotCoverageService.AddCoverage(new LotCoverageAddInput
        {
            ProductId = idSplit, Quantity = 100, ExpiryDate = ExpSep, LotNumber = "",
        }).ProductLotId!.Value;
        LotCoverageService.SplitCoverage(new LotCoverageSplitInput
        {
            ProductLotId = splitOrigin,
            DestinationQuantity = 40,
            DestinationExpiryDate = ExpNov,
            DestinationLotNumber = "",
            Reason = "duas validades na pilha",
        });
        var snapSplit = LotCoverageService.GetSnapshot(idSplit);
        Assert.Equal(100, snapSplit.TrackedQuantity);
        Assert.Equal(100, snapSplit.Stock);
        Assert.Equal(2, snapSplit.Lines.Count);

        // CorrectQuantity manual em B: 60 → 40
        var lineB2 = LotCoverageService.GetSnapshot(idB).Lines.Single();
        Assert.Contains("não será alterado", LotCoverageUi.QuantityHint);
        LotCoverageService.CorrectQuantity(new LotCoverageQuantityInput
        {
            ProductLotId = lineB2.Id, Quantity = 40, Reason = "contagem da caixa",
        });
        var snapB2 = LotCoverageService.GetSnapshot(idB);
        Assert.Equal(40, snapB2.TrackedQuantity);
        Assert.Equal(60, snapB2.UntrackedQuantity);
        Assert.Equal(100, TestDataHelper.GetProductStock(idB));

        // Remove manual
        Assert.Contains("NÃO removerá o produto do estoque", LotCoverageUi.RemoveConfirmMessage);
        var remId = snapB2.Lines.Single().Id;
        LotCoverageService.RemoveCoverage(new LotCoverageRemoveInput
        {
            ProductLotId = remId, Reason = "etiqueta ilegível",
        });
        var snapB3 = LotCoverageService.GetSnapshot(idB);
        Assert.Equal(0, snapB3.TrackedQuantity);
        Assert.Equal(100, snapB3.UntrackedQuantity);
        Assert.Equal(100, TestDataHelper.GetProductStock(idB));

        // ---- CENÁRIO D: lote vazio ----
        var idD = Seed("TVLOT", "TESTE LOTE VAZIO", 30, 0, 2);
        LotCoverageService.AddCoverage(new LotCoverageAddInput
        {
            ProductId = idD, Quantity = 10, ExpiryDate = ExpSep, LotNumber = "",
        });
        Assert.Equal("—", LotCoverageUi.ToRows(LotCoverageService.GetSnapshot(idD))[0].LotDisplay);

        // ---- CENÁRIO E: validade não informada (legado Receive) ----
        var idE = Seed("TVEXP", "TESTE VALIDADE NULA", 20, 0, 2);
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = idE, Quantity = 5, LotNumber = "LEG", ExpiryDate = null,
        });
        Assert.Equal("Não informada", LotCoverageUi.ToRows(LotCoverageService.GetSnapshot(idE))[0].ExpiryDisplay);

        // ---- Validade vencida: confirmação UI (SensitiveExpiryCorrection no motor) ----
        var idExp = Seed("TVVEN", "TESTE VENCIDO EDIT", 20, 0, 2);
        var expired = DateTime.Today.AddDays(-5);
        var expLot = LotCoverageService.AddCoverage(new LotCoverageAddInput
        {
            ProductId = idExp, Quantity = 10, ExpiryDate = expired, LotNumber = "VENC",
        }).ProductLotId!.Value;
        var editedExp = LotCoverageService.EditCoverage(new LotCoverageEditInput
        {
            ProductLotId = expLot,
            ExpiryDate = DateTime.Today.AddDays(20),
            LotNumber = "VENC",
            Reason = "data da nota errada",
        });
        Assert.True(editedExp.SensitiveExpiryCorrection);
        Assert.Contains("validade vencida", LotCoverageUi.SensitiveExpiryConfirmMessage, StringComparison.OrdinalIgnoreCase);

        // ---- Inventário aberto ----
        InventoryService.CreateSession();
        var invEx = Assert.Throws<LotCoverageException>(() =>
            LotCoverageService.AddCoverage(new LotCoverageAddInput
            {
                ProductId = idD, Quantity = 1, ExpiryDate = ExpNov, LotNumber = "",
            }));
        Assert.Equal(LotCoverageRules.OpenInventory, invEx.ErrorCode);
        Assert.Contains("inventário", LotCoverageUi.MapError(invEx));
        var open = InventoryService.GetOpenSession();
        Assert.NotNull(open);
        InventoryService.Cancel(open!.Id);

        // ---- Permissões ----
        TestDataHelper.SetSessionRole("gestor");
        Assert.True(LotCoverageUi.CanMutateUi());
        TestDataHelper.SetSessionCustomPermissions("vendedor", p => p.RelatoriosAcesso = true);
        Assert.False(LotCoverageUi.CanMutateUi());
        TestDataHelper.SetSessionRole("admin");
        Assert.True(LotCoverageUi.CanMutateUi());

        // ---- Rede Loja (código) ----
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        Assert.False(LotCoverageUi.CanMutateUi());
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

        // ---- Integridade final no ui_test.db ----
        Assert.Equal(100, TestDataHelper.GetProductStock(idA));
        Assert.Equal(0, TestDataHelper.GetProductFridge(idA));
        Assert.Equal(100, SumLots(idA));
        Assert.All(ListLots(idA), l =>
        {
            Assert.Null(l.PurchaseId);
            Assert.True(l.UnitCost <= 0.009);
        });

        var buyFinal = GetOrigin(buyLotId);
        Assert.Equal(purchaseId, buyFinal.PurchaseId);
        Assert.True(buyFinal.UnitCost > 0.009);
        Assert.Null(GetOrigin(manualAdd.ProductLotId!.Value).PurchaseId);
        Assert.Equal(0, GetOrigin(manualAdd.ProductLotId!.Value).UnitCost);

        // Marcador / cópia estável para inspeção visual (melhor esforço se não houver lock).
        try
        {
            foreach (var leftover in Directory.GetFiles(TestDbDir, "ui_test.db*"))
                TryDelete(leftover);
            File.Copy(runDbPath, TestDbPath, overwrite: true);
            File.WriteAllText(
                Path.Combine(TestDbDir, "READY.txt"),
                $"db={TestDbPath}\nrun_db={runDbPath}\nnormal={normalDb}\nisolated=true\nlogin=admin_d1\npass=teste1234\n");
        }
        catch (IOException)
        {
            File.WriteAllText(
                Path.Combine(TestDbDir, "READY.txt"),
                $"db={runDbPath}\nnormal={normalDb}\nisolated=true\nlogin=admin_d1\npass=teste1234\nnote=ui_test.db locked; use run_db\n");
        }
    }

    private static int Seed(string code, string name, double stock, double fridge, double cost)
    {
        var id = TestDataHelper.SeedSimpleProduct(stock, salePrice: 5, costPrice: cost, code: code, name: name);
        if (Math.Abs(fridge) > 0.0001)
            TestDataHelper.SetProductFridge(id, fridge);
        return id;
    }

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORN D1', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void SetStock(int productId, double stock)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET stock = $s WHERE id = $id;";
        cmd.Parameters.AddWithValue("$s", stock);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    private static double SumLots(int productId) => TestDataHelper.SumLots(productId);

    private static (int? PurchaseId, double UnitCost) GetOrigin(int lotId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT purchase_id, IFNULL(unit_cost,0) FROM product_lots WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", lotId);
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        return (reader.IsDBNull(0) ? null : reader.GetInt32(0), reader.GetDouble(1));
    }

    private static List<(int Id, int? PurchaseId, double UnitCost)> ListLots(int productId)
    {
        var list = new List<(int, int?, double)>();
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, purchase_id, IFNULL(unit_cost,0) FROM product_lots WHERE product_id = $id AND quantity > 0.0001;";
        cmd.Parameters.AddWithValue("$id", productId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1), reader.GetDouble(2)));
        return list;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            /* ignore locked wal */
        }
    }
}
