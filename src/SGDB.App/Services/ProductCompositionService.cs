using SGDB.Models;

namespace SGDB.Services;

public static class ProductCompositionService
{
    public static IReadOnlyList<ProductCompositionItem> GetItems(ProductExtra? extra)
    {
        if (extra?.ComposicaoItens is null || extra.ComposicaoItens.Count == 0)
            return [];
        return extra.ComposicaoItens
            .Where(i => i.ProductId > 0 && i.Quantity > 0)
            .Select(i => new ProductCompositionItem
            {
                ProductId = i.ProductId,
                Quantity = Math.Round(i.Quantity, 6),
                Code = i.Code ?? "",
                Name = i.Name ?? "",
                Unit = string.IsNullOrWhiteSpace(i.Unit) ? "UN" : i.Unit,
                Cost = Math.Round(i.Cost, 4),
            })
            .ToList();
    }

    public static bool IsActive(Product product)
    {
        var extra = ProductExtra.Parse(product.ExtraJson);
        return extra.Composicao && GetItems(extra).Count > 0;
    }

    public static void Validate(ProductExtra extra, int? selfId = null)
    {
        if (!extra.Composicao)
            return;
        var items = GetItems(extra);
        if (items.Count == 0)
            throw new InvalidOperationException(
                "Marque Composição e informe ao menos um componente na aba Composição.");

        foreach (var row in items)
        {
            if (selfId is int sid && row.ProductId == sid)
                throw new InvalidOperationException("O produto não pode ser componente de si mesmo.");
            var comp = ProductService.GetById(row.ProductId)
                ?? throw new InvalidOperationException($"Componente #{row.ProductId} não encontrado.");
            if (!comp.Active)
                throw new InvalidOperationException($"Componente inativo: {comp.Name}");
        }
    }

    /// <summary>Retorna (produto, qtd estoque) a baixar na venda.</summary>
    public static IReadOnlyList<(Product Product, double Qty)> StockMovementsForSale(Product sold, double qtySale)
    {
        if (qtySale <= 0)
            throw new InvalidOperationException("Quantidade inválida.");

        var extra = ProductExtra.Parse(sold.ExtraJson);
        if (extra.Composicao)
        {
            var items = GetItems(extra);
            if (items.Count == 0)
                throw new InvalidOperationException(
                    $"\"{sold.Name}\" está com Composição mas sem itens cadastrados.");

            var outList = new List<(Product, double)>();
            foreach (var row in items)
            {
                var comp = ProductService.GetById(row.ProductId)
                    ?? throw new InvalidOperationException($"Componente #{row.ProductId} não encontrado.");
                var deduct = Math.Round(qtySale * row.Quantity, 6);
                if (deduct > 0)
                    outList.Add((comp, deduct));
            }
            return outList;
        }

        return [(sold, qtySale)];
    }

    public static double? AvailableSaleQty(Product product)
    {
        var extra = ProductExtra.Parse(product.ExtraJson);
        if (!extra.Composicao)
            return null;
        var items = GetItems(extra);
        if (items.Count == 0)
            return null;

        double? avail = null;
        foreach (var row in items)
        {
            var comp = ProductService.GetById(row.ProductId);
            if (comp is null) return 0;
            if (row.Quantity <= 0) continue;
            var a = (comp.TotalStock) / row.Quantity;
            avail = avail is null ? a : Math.Min(avail.Value, a);
        }
        return Math.Round(avail ?? 0, 3);
    }
}
