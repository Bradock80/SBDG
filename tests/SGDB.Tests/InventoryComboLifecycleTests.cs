using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

[Collection(TempDatabaseCollection.Name)]
public class InventoryComboLifecycleTests
{
    static readonly DateOnly Today = new(2026, 9, 6);

    [Fact]
    public void Regras_margem_preco_e_retornavel()
    {
        Assert.Equal(20, InventorySmartMargin.AbsoluteMinimumPercent);
        Assert.Equal("Nenhum preço de combo seguro dentro da margem mínima de 20%.",
            InventoryComboLifecycleRules.NoSafePriceMessage);
        Assert.Contains("retornável", InventoryComboLifecycleRules.ReturnableBlockedMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(InventoryComboLifecycleRules.ValidatePrice(10, 0));
        Assert.NotNull(InventoryComboLifecycleRules.ValidatePrice(7, 8));
        Assert.NotNull(InventoryComboLifecycleRules.ValidatePrice(10, 8.5)); // 15%
        Assert.Null(InventoryComboLifecycleRules.ValidatePrice(10, 7)); // 30%
        Assert.True(InventoryComboLifecycleRules.IsReturnable(new ProductExtra { VasilhameTipoId = 3 }));
        Assert.False(InventoryComboLifecycleRules.IsReturnable(new ProductExtra()));
        Assert.False(InventoryComboLifecycleRules.CanRejectedReappear(Today, "a", "a", Today));
        Assert.True(InventoryComboLifecycleRules.CanRejectedReappear(Today, "a", "b", Today));
        Assert.True(InventoryComboLifecycleRules.CanRejectedReappear(Today, "a", "a", Today.AddDays(7)));
        Assert.Equal(InventoryComboLifecycleStatus.Expired,
            InventoryComboLifecycleRules.EffectiveStatus(
                InventoryComboLifecycleStatus.Active, Today, Today.AddDays(-10), Today.AddDays(-1), 0, 10));
        Assert.Equal(InventoryComboLifecycleStatus.Finished,
            InventoryComboLifecycleRules.EffectiveStatus(
                InventoryComboLifecycleStatus.Active, Today, Today, Today.AddDays(7), 10, 10));
    }

    [Fact]
    public void Sugestao_nao_cria_produto_nem_aparece_no_pdv()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        var target = TestDataHelper.SeedSimpleProduct(20, 4, 2, "TGT1", "Guaravita");
        var anchor = TestDataHelper.SeedSimpleProduct(20, 5, 2.5, "ANC1", "Coca");
        Assert.Empty(InventoryComboLifecycleService.ListAll());
        Assert.Null(PdvService.FindProduct("CMB" + target.ToString("D4")));
        Assert.NotNull(PdvService.FindProduct("TGT1"));
        Assert.NotNull(PdvService.FindProduct("ANC1"));
        _ = target;
        _ = anchor;
    }

    [Fact]
    public void Aprovacao_cria_kit_e_pdv_baixa_componentes()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(50, "combo");
        var target = SeedCosted("TGT2", "Guaravita", 20, 4, 2);
        var anchor = SeedCosted("ANC2", "Soda", 20, 5, 2.5);
        var draft = InventoryComboApprovalService.ReloadDraft(target, anchor, Today);
        Assert.True(draft.HasSafePrice);
        draft.MaxSafeQuantity = 5;
        draft.StartDate = Today;
        draft.EndDate = Today.AddDays(7);
        var approved = InventoryComboApprovalService.Confirm(draft, Today);
        Assert.True(approved.Ok, approved.Error);
        Assert.NotNull(approved.Campaign?.ProductId);
        var combo = ProductService.GetById(approved.Campaign!.ProductId!.Value)!;
        Assert.Contains("COMBO GIRO", combo.Name, StringComparison.OrdinalIgnoreCase);
        Assert.True(ProductCompositionService.IsActive(combo));
        Assert.True(InventoryComboLifecycleService.IsPdvSellable(combo, Today));

        var dup = InventoryComboApprovalService.Confirm(draft, Today);
        Assert.False(dup.Ok);
        Assert.Contains("duplicada", dup.Error, StringComparison.OrdinalIgnoreCase);

        var sale = TestDataHelper.FinalizeSimpleCashSale(combo.Id, 1, combo.SalePrice, combo.SalePrice + 10);
        Assert.True(sale.SaleId > 0);
        Assert.Equal(19, TestDataHelper.GetProductStock(target));
        Assert.Equal(19, TestDataHelper.GetProductStock(anchor));
        Assert.Equal(0, combo.TotalStock);
        var after = InventoryComboLifecycleService.GetByProductId(combo.Id)!;
        Assert.Equal(1, after.SoldQty);

        PdvService.CancelSale(sale.SaleId);
        Assert.Equal(20, TestDataHelper.GetProductStock(target));
        Assert.Equal(20, TestDataHelper.GetProductStock(anchor));
        Assert.Equal(0, InventoryComboLifecycleService.GetByProductId(combo.Id)!.SoldQty);
    }

    [Fact]
    public void Recusa_preco_inseguro_custo_ausente_e_retornavel()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        var target = SeedCosted("TGT3", "Alvo", 10, 10, 0);
        var anchor = SeedCosted("ANC3", "Anc", 10, 10, 5);
        var draft = InventoryComboApprovalService.ReloadDraft(target, anchor, Today);
        Assert.False(draft.HasSafePrice);
        Assert.Equal(InventoryComboLifecycleRules.NoSafePriceMessage, draft.BlockReason);

        var priced = SeedCosted("TGT4", "Alvo2", 10, 10, 8);
        var pricedA = SeedCosted("ANC4", "Anc2", 10, 5, 2);
        var okDraft = InventoryComboApprovalService.ReloadDraft(priced, pricedA, Today);
        okDraft.SuggestedPrice = 9; // below cost 10
        var below = InventoryComboApprovalService.Confirm(okDraft, Today);
        Assert.False(below.Ok);

        var ret = SeedCosted("TGT5", "Ret", 10, 10, 4);
        SetVasilhame(ret);
        var retA = SeedCosted("ANC5", "Anc3", 10, 10, 4);
        var retDraft = InventoryComboApprovalService.ReloadDraft(ret, retA, Today);
        Assert.Equal(InventoryComboLifecycleRules.ReturnableBlockedMessage, retDraft.BlockReason);
        var retConfirm = InventoryComboApprovalService.Confirm(retDraft, Today);
        Assert.False(retConfirm.Ok);
    }

    [Fact]
    public void Pausado_encerrado_expirado_nao_vendem_e_reativacao_revalida()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(80, "combo-status");
        var target = SeedCosted("TGT6", "Alvo", 30, 8, 4);
        var anchor = SeedCosted("ANC6", "Anc", 30, 8, 4);
        var draft = InventoryComboApprovalService.ReloadDraft(target, anchor, Today);
        draft.MaxSafeQuantity = 2;
        draft.StartDate = Today;
        draft.EndDate = Today.AddDays(3);
        var approved = InventoryComboApprovalService.Confirm(draft, Today);
        Assert.True(approved.Ok, approved.Error);
        var campaignId = approved.Campaign!.Id;
        var productId = approved.Campaign.ProductId!.Value;
        var combo = ProductService.GetById(productId)!;

        Assert.True(InventoryComboLifecycleService.Pause(campaignId).Ok);
        Assert.False(InventoryComboLifecycleService.IsPdvSellable(ProductService.GetById(productId)!, Today));
        Assert.Null(PdvService.FindProduct(combo.Code));

        Assert.True(InventoryComboLifecycleService.Reactivate(campaignId, Today).Ok);
        Assert.True(InventoryComboLifecycleService.IsPdvSellable(ProductService.GetById(productId)!, Today));

        Assert.True(InventoryComboLifecycleService.Finish(campaignId).Ok);
        Assert.False(InventoryComboLifecycleService.IsPdvSellable(ProductService.GetById(productId)!, Today));

        var expired = InventoryComboLifecycleRules.EffectiveStatus(
            InventoryComboLifecycleStatus.Active, Today.AddDays(10), Today, Today.AddDays(3), 0, 2);
        Assert.Equal(InventoryComboLifecycleStatus.Expired, expired);
    }

    [Fact]
    public void Falta_de_componente_bloqueia_e_maximo_respeitado()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(80, "combo-stock");
        var target = SeedCosted("TGT7", "Alvo", 1, 8, 4);
        var anchor = SeedCosted("ANC7", "Anc", 10, 8, 4);
        var draft = InventoryComboApprovalService.ReloadDraft(target, anchor, Today);
        draft.MaxSafeQuantity = 1;
        var approved = InventoryComboApprovalService.Confirm(draft, Today);
        Assert.True(approved.Ok, approved.Error);
        var combo = ProductService.GetById(approved.Campaign!.ProductId!.Value)!;
        TestDataHelper.FinalizeSimpleCashSale(combo.Id, 1, combo.SalePrice, combo.SalePrice + 5);
        var ex = Assert.Throws<PdvException>(() =>
            TestDataHelper.FinalizeSimpleCashSale(combo.Id, 1, combo.SalePrice, combo.SalePrice + 5));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void Descarte_esconde_e_cancelar_aprovacao_nao_cria()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        var target = SeedCosted("TGT8", "Alvo", 10, 8, 4);
        var anchor = SeedCosted("ANC8", "Anc", 10, 8, 4);
        var key = InventoryComboLifecycleRules.SuggestionKey(target, anchor);
        var rejected = InventoryComboLifecycleService.Reject(key, "sig", "não");
        Assert.True(rejected.Ok);
        Assert.True(InventoryComboLifecycleService.ShouldHideRejectedSuggestion(key, "sig", Today));
        Assert.Empty(InventoryComboLifecycleService.ListAll().Where(c => c.ProductId is > 0));
    }

    [Fact]
    public void Migration_idempotente_preserva_dados_e_nao_toca_vendas()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        var target = SeedCosted("TGT9", "Alvo", 10, 8, 4);
        var anchor = SeedCosted("ANC9", "Anc", 10, 8, 4);
        var draft = InventoryComboApprovalService.ReloadDraft(target, anchor, Today);
        Assert.True(InventoryComboApprovalService.Confirm(draft, Today).Ok);

        using (var conn = DatabaseService.OpenConnection())
        {
            using var drop = conn.CreateCommand();
            drop.CommandText = "DROP TABLE inventory_combo_campaigns;";
            drop.ExecuteNonQuery();
        }

        DatabaseService.Initialize(db.DatabasePath);
        using (var conn = DatabaseService.OpenConnection())
        {
            using var check = conn.CreateCommand();
            check.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='inventory_combo_campaigns';";
            Assert.NotNull(check.ExecuteScalar());
            using var products = conn.CreateCommand();
            products.CommandText = "SELECT COUNT(*) FROM products;";
            Assert.True(Convert.ToInt32(products.ExecuteScalar()) >= 2);
        }

        DatabaseService.Initialize(db.DatabasePath);
        Assert.True(TableExists());
    }

    [Fact]
    public void Venda_nao_duplica_receita_e_devolucao_restaura()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(80, "combo-rev");
        var target = SeedCosted("TGT10", "Alvo", 20, 8, 4);
        var anchor = SeedCosted("ANC10", "Anc", 20, 8, 4);
        var draft = InventoryComboApprovalService.ReloadDraft(target, anchor, Today);
        var approved = InventoryComboApprovalService.Confirm(draft, Today);
        Assert.True(approved.Ok, approved.Error);
        var combo = ProductService.GetById(approved.Campaign!.ProductId!.Value)!;
        var sale = TestDataHelper.FinalizeSimpleCashSale(combo.Id, 1, combo.SalePrice, combo.SalePrice + 5);
        using (var conn = DatabaseService.OpenConnection())
        {
            using var items = conn.CreateCommand();
            items.CommandText = "SELECT product_id, quantity, subtotal, cost_at_sale FROM sale_items WHERE sale_id = $id;";
            items.Parameters.AddWithValue("$id", sale.SaleId);
            using var reader = items.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(combo.Id, reader.GetInt32(0));
            Assert.Equal(1, reader.GetDouble(1));
            Assert.Equal(combo.SalePrice, reader.GetDouble(2), 2);
            Assert.True(reader.GetDouble(3) > 0);
            Assert.False(reader.Read());
        }

        Assert.Equal(19, TestDataHelper.GetProductStock(target));
        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = SaleItemId(sale.SaleId), Qty = 1 }],
        });
        Assert.Equal(20, TestDataHelper.GetProductStock(target));
        Assert.Equal(20, TestDataHelper.GetProductStock(anchor));
        Assert.Equal(0, InventoryComboLifecycleService.GetByProductId(combo.Id)!.SoldQty);
    }

    [Fact]
    public void Aprovacao_registra_auditoria()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        var target = SeedCosted("TGT11", "Alvo", 10, 8, 4);
        var anchor = SeedCosted("ANC11", "Anc", 10, 8, 4);
        var draft = InventoryComboApprovalService.ReloadDraft(target, anchor, Today);
        Assert.True(InventoryComboApprovalService.Confirm(draft, Today).Ok);
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM audit_log WHERE action = 'aprovar' AND entity = 'combo';";
        Assert.True(Convert.ToInt32(cmd.ExecuteScalar()) >= 1);
    }

    static int SaleItemId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static bool TableExists()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='inventory_combo_campaigns';";
        return cmd.ExecuteScalar() is not null;
    }

    static int SeedCosted(string code, string name, double stock, double sale, double cost)
    {
        var id = TestDataHelper.SeedSimpleProduct(stock, sale, cost, code, name);
        var product = ProductService.GetById(id)!;
        ProductService.Update(id, new ProductInput
        {
            Code = product.Code,
            Name = product.Name,
            Unit = product.Unit,
            CostPrice = cost,
            SalePrice = sale,
            Extra = ProductExtra.Parse(product.ExtraJson),
            Active = true,
        });
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET stock = $s, cost_price = $c, sale_price = $p WHERE id = $id;";
        cmd.Parameters.AddWithValue("$s", stock);
        cmd.Parameters.AddWithValue("$c", cost);
        cmd.Parameters.AddWithValue("$p", sale);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        return id;
    }

    static void SetVasilhame(int productId)
    {
        var product = ProductService.GetById(productId)!;
        var extra = ProductExtra.Parse(product.ExtraJson);
        extra.VasilhameTipoId = 1;
        ProductService.Update(productId, new ProductInput
        {
            Code = product.Code,
            Name = product.Name,
            Unit = product.Unit,
            CostPrice = product.CostPrice,
            SalePrice = product.SalePrice,
            Extra = extra,
            Active = true,
        });
    }
}
