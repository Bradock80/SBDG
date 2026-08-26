using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using SGDB.Domain.Products;
using SGDB.Domain.Purchases;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// Importa notas fiscais eletrônicas (NF-e) em XML: lê o emitente, cabeçalho e itens,
/// tenta casar cada item com um produto existente (por GTIN ou nome) e, ao aplicar,
/// gera automaticamente o cadastro de fornecedor/produtos e a compra correspondente.
/// </summary>
public static class NfeXmlImportService
{
    public static NfeImportPreview ParseFile(
        string path, bool includeIcmsStInCost = NfeEffectiveCostImportPolicy.DefaultIncludeIcmsStInCost)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException("Arquivo XML não encontrado.");
        var xml = File.ReadAllText(path);
        return ParseXml(xml, includeIcmsStInCost);
    }

    /// <param name="includeIcmsStInCost">
    /// Default true: custo efetivo (landed com ST destacado).
    /// False = override avançado DANFE sem ST. O resolver ainda calcula os dois.
    /// </param>
    public static NfeImportPreview ParseXml(
        string xml, bool includeIcmsStInCost = NfeEffectiveCostImportPolicy.DefaultIncludeIcmsStInCost)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new InvalidOperationException("XML vazio.");

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"XML inválido: {ex.Message}");
        }

        var infNFe = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "infNFe")
            ?? throw new InvalidOperationException("XML não é uma NF-e válida (elemento infNFe não encontrado).");

        var emit = Child(infNFe, "emit");
        var ide = Child(infNFe, "ide");

        var chave = ExtractChave(doc, infNFe);
        var cnpjDigits = TextNorm.DigitsOnly(Value(emit, "CNPJ")) ?? "";
        var nomeEmit = (Value(emit, "xNome") ?? "").Trim();
        var fantasia = (Value(emit, "xFant") ?? "").Trim();
        var ie = (Value(emit, "IE") ?? "").Trim();
        var ender = Child(emit, "enderEmit");
        var numero = (Value(ide, "nNF") ?? "").Trim();
        var serie = (Value(ide, "serie") ?? "1").Trim();
        var dhEmi = (Value(ide, "dhEmi") ?? Value(ide, "dEmi") ?? "").Trim();

        var totalNode = Child(infNFe, "total");
        var icmsTot = totalNode is null ? null : Child(totalNode, "ICMSTot");
        var headerSt = ParseDecimal(Value(icmsTot, "vST"));
        var headerDesc = ParseDecimal(Value(icmsTot, "vDesc"));
        var headerVNf = ParseDecimal(Value(icmsTot, "vNF"));
        var headerVProd = ParseDecimal(Value(icmsTot, "vProd"));
        var headerFrete = ParseDecimal(Value(icmsTot, "vFrete"));
        var headerOutro = ParseDecimal(Value(icmsTot, "vOutro"));
        var headerIpi = ParseDecimal(Value(icmsTot, "vIPI"));

        var cobr = Child(infNFe, "cobr");
        var fat = cobr is null ? null : Child(cobr, "fat");
        var fatOrig = ParseDecimal(Value(fat, "vOrig"));
        var fatDesc = ParseDecimal(Value(fat, "vDesc"));
        var fatLiq = ParseDecimal(Value(fat, "vLiq"));
        var dupSum = cobr is null
            ? 0
            : cobr.Elements().Where(e => e.Name.LocalName == "dup")
                .Sum(d => ParseDecimal(Value(d, "vDup")));
        var pagNode = Child(infNFe, "pag");
        var pagSum = pagNode is null
            ? 0
            : pagNode.Descendants().Where(e => e.Name.LocalName == "vPag")
                .Sum(e => ParseDecimal(e.Value));
        var infAdic = Child(infNFe, "infAdic");
        var infCpl = Value(infAdic, "infCpl");

        var rawLines = new List<RawNfeLine>();
        foreach (var det in infNFe.Elements().Where(e => e.Name.LocalName == "det"))
        {
            var prod = Child(det, "prod");
            if (prod is null)
                continue;

            var cProd = (Value(prod, "cProd") ?? "").Trim();
            var packBarcode = NormalizeEan(Value(prod, "cEAN"));
            var unitBarcode = NormalizeEan(Value(prod, "cEANTrib"));
            var name = (Value(prod, "xProd") ?? "").Trim();
            var uCom = (Value(prod, "uCom") ?? "UN").Trim().ToUpperInvariant();
            var qCom = ParseDecimal(Value(prod, "qCom"));
            var vUnCom = ParseDecimal(Value(prod, "vUnCom"));
            var uTrib = (Value(prod, "uTrib") ?? "").Trim().ToUpperInvariant();
            var qTrib = ParseDecimal(Value(prod, "qTrib"));
            var vUnTrib = ParseDecimal(Value(prod, "vUnTrib"));
            var vProd = ParseDecimal(Value(prod, "vProd"));
            var vDesc = ParseDecimal(Value(prod, "vDesc"));
            var vFrete = ParseDecimal(Value(prod, "vFrete"));
            var vSeg = ParseDecimal(Value(prod, "vSeg"));
            var vOutro = ParseDecimal(Value(prod, "vOutro"));
            var vItemRaw = Value(prod, "vItem");
            double? vItem = string.IsNullOrWhiteSpace(vItemRaw) ? null : ParseDecimal(vItemRaw);
            var cfop = (Value(prod, "CFOP") ?? "").Trim();
            var indTotRaw = Value(prod, "indTot");
            int? indTot = int.TryParse(indTotRaw, out var indParsed) ? indParsed : null;
            var infAdProd = (Value(det, "infAdProd") ?? "").Trim();
            if (string.IsNullOrEmpty(infAdProd))
                infAdProd = (Value(prod, "infAdProd") ?? "").Trim();

            var vIpi = SumTax(det, "vIPI");
            var vIcmsSt = SumTax(det, "vICMSST");
            var vIcmsStRet = SumTax(det, "vICMSSTRet");
            var vFcpSt = SumTax(det, "vFCPST");
            var vFcpStRet = SumTax(det, "vFCPSTRet");

            if (string.IsNullOrWhiteSpace(name))
                continue;

            var (matchedId, matchedName) = MatchProduct(unitBarcode, packBarcode, name);
            double packFactorFromProduct = 1;
            if (matchedId is int mid)
            {
                var existing = ProductService.GetById(mid);
                if (existing is not null)
                {
                    var fx = ProductExtra.Parse(existing.ExtraJson).FatorEmbalagem;
                    if (fx > 1) packFactorFromProduct = fx;
                }
            }

            var rastro = ParseRastro(det);

            rawLines.Add(new RawNfeLine
            {
                Cprod = cProd,
                PackBarcode = packBarcode,
                UnitBarcode = unitBarcode,
                Name = name,
                UCom = uCom,
                QCom = qCom,
                VUnCom = vUnCom,
                UTrib = uTrib,
                QTrib = qTrib,
                VUnTrib = vUnTrib,
                VProd = vProd,
                VIpi = vIpi,
                VIcmsSt = vIcmsSt,
                VIcmsStRet = vIcmsStRet,
                VFcpSt = vFcpSt,
                VFcpStRet = vFcpStRet,
                VFrete = vFrete,
                VSeg = vSeg,
                VOutro = vOutro,
                VDesc = vDesc,
                VItem = vItem,
                Cfop = cfop,
                IndTot = indTot,
                InfAdProd = infAdProd,
                PackFactorFromProduct = packFactorFromProduct,
                MatchedId = matchedId,
                MatchedName = matchedName,
                LotNumber = rastro.LotNumber,
                ExpiryDateIso = rastro.ExpiryDateIso,
                HasXmlRastro = rastro.HasRastro,
            });
        }

        if (rawLines.Count == 0)
            throw new InvalidOperationException("Nenhum item (det/prod) encontrado no XML.");

        var itemStSum = rawLines.Sum(r => r.VIcmsSt + r.VFcpSt);
        var headerStUnallocated = headerSt > 0.05 && itemStSum < 0.05;
        var headerFreightUnallocated = headerFrete > 0.05 && rawLines.Sum(r => r.VFrete) < 0.05;
        var headerOtherUnallocated = headerOutro > 0.05 && rawLines.Sum(r => r.VOutro) < 0.05;
        var itemDescSum = rawLines.Sum(r => r.VDesc);
        var headerDiscountUnallocated = Math.Max(headerDesc, fatDesc) - itemDescSum > 0.05;

        var items = new List<NfeImportItem>();
        foreach (var line in rawLines)
        {
            var costInput = new NfeEffectiveCostInput
            {
                VProd = line.VProd,
                QCom = line.QCom,
                UCom = line.UCom,
                VUnCom = line.VUnCom,
                QTrib = line.QTrib,
                UTrib = line.UTrib,
                VUnTrib = line.VUnTrib,
                VDesc = line.VDesc,
                VFrete = line.VFrete,
                VSeg = line.VSeg,
                VOutro = line.VOutro,
                VIpi = line.VIpi,
                VIcmsSt = line.VIcmsSt,
                VIcmsStRet = line.VIcmsStRet,
                VFcpSt = line.VFcpSt,
                VFcpStRet = line.VFcpStRet,
                VItem = line.VItem,
                InfAdProd = line.InfAdProd,
                InfCpl = infCpl,
                Cfop = line.Cfop,
                IndTot = line.IndTot,
                EmitCnpj = cnpjDigits,
                EmitName = nomeEmit,
                HeaderVProd = headerVProd,
                HeaderVNf = headerVNf,
                HeaderSt = headerSt,
                HeaderDesc = headerDesc,
                HeaderFrete = headerFrete,
                HeaderOutro = headerOutro,
                FatLiq = fatLiq,
                DupSum = dupSum,
                PagSum = pagSum,
                HeaderStUnallocated = headerStUnallocated,
                HeaderFreightUnallocated = headerFreightUnallocated,
                HeaderOtherUnallocated = headerOtherUnallocated,
                HeaderDiscountUnallocated = headerDiscountUnallocated,
            };
            var decision = NfeEffectiveCostResolver.Resolve(costInput);

            var effectiveTotal = decision.EffectiveLineCost;
            var withoutStTotal = decision.DanfeLineCostWithoutSt;
            var appliedTotal = includeIcmsStInCost || decision.IsNonPayable
                ? effectiveTotal
                : withoutStTotal;

            var converted = ConvertPackToSaleUnits(
                line.UCom, line.QCom,
                line.QCom > 0 ? appliedTotal / line.QCom : line.VUnCom,
                line.UTrib, line.QTrib, line.VUnTrib,
                appliedTotal, line.PackFactorFromProduct, line.Name);
            var convertedNoSt = ConvertPackToSaleUnits(
                line.UCom, line.QCom,
                line.QCom > 0 ? withoutStTotal / line.QCom : line.VUnCom,
                line.UTrib, line.QTrib, line.VUnTrib,
                withoutStTotal, line.PackFactorFromProduct, line.Name);
            var convertedWithSt = ConvertPackToSaleUnits(
                line.UCom, line.QCom,
                line.QCom > 0 ? effectiveTotal / line.QCom : line.VUnCom,
                line.UTrib, line.QTrib, line.VUnTrib,
                effectiveTotal, line.PackFactorFromProduct, line.Name);

            if (!includeIcmsStInCost && decision.IncludeInPayable)
                decision = decision.WithDanfeWithoutStOverride(line.QCom, converted.Quantity);
            else
                decision = decision.WithPhysicalQuantity(converted.Quantity);

            var packFactor = converted.PackFactor;
            if (ProductClassificationHelper.IsCigarette(line.Name))
                packFactor = ProductPriceHelper.ResolveCigarettesPerPack(line.Name, packFactor);

            var item = new NfeImportItem
            {
                Cprod = line.Cprod,
                Barcode = line.UnitBarcode ?? line.PackBarcode,
                PackBarcode = line.PackBarcode is not null && line.PackBarcode != (line.UnitBarcode ?? "")
                    ? line.PackBarcode
                    : null,
                Name = line.Name,
                Unit = converted.Unit,
                NfUnit = string.IsNullOrWhiteSpace(line.UCom) ? "UN" : line.UCom,
                NfQuantity = line.QCom,
                NfUnitPrice = line.VUnCom,
                Quantity = Math.Round(converted.Quantity, 4),
                UnitPriceWithoutSt = Math.Round(convertedNoSt.UnitPrice, 4),
                UnitPriceWithSt = Math.Round(convertedWithSt.UnitPrice, 4),
                PackFactor = packFactor,
                PackNote = MergeNotes(converted.Note, decision.Explanation),
                MatchedProductId = line.MatchedId,
                MatchedProductName = line.MatchedName,
                LotNumber = line.LotNumber,
                ExpiryDateIso = line.ExpiryDateIso,
                HasXmlRastro = line.HasXmlRastro,
            };
            item.ApplyResolvedCost(
                decision,
                Math.Round(converted.UnitPrice, 6),
                appliedTotal);
            items.Add(item);
        }

        var payable = items.Where(i => i.IncludeInPayable).Select(i => i.EffectiveLineCost).ToList();
        var excludedGross = rawLines
            .Where((_, i) => i < items.Count && !items[i].IncludeInPayable)
            .Sum(r => r.VProd);
        var reconciliation = NfeCostReconciliation.Reconcile(
            payable, fatLiq, dupSum, pagSum, headerVNf, excludedGross);

        return new NfeImportPreview
        {
            Chave = chave,
            EmitenteCnpj = cnpjDigits,
            EmitenteNome = nomeEmit,
            EmitenteFantasia = fantasia,
            EmitenteIe = ie,
            EmitentePhone = NullIfEmpty(Value(ender, "fone")),
            EmitenteCep = NullIfEmpty(Value(ender, "CEP")),
            EmitenteAddress = NullIfEmpty(Value(ender, "xLgr")),
            EmitenteAddressNumber = NullIfEmpty(Value(ender, "nro")),
            EmitenteComplement = NullIfEmpty(Value(ender, "xCpl")),
            EmitenteNeighborhood = NullIfEmpty(Value(ender, "xBairro")),
            EmitenteCity = NullIfEmpty(Value(ender, "xMun")),
            EmitenteState = NullIfEmpty(Value(ender, "UF")),
            Numero = numero,
            Serie = string.IsNullOrWhiteSpace(serie) ? "1" : serie,
            DataEmissao = dhEmi,
            HeaderVProd = headerVProd,
            HeaderSt = headerSt,
            HeaderDesc = headerDesc,
            HeaderVNf = headerVNf,
            HeaderFrete = headerFrete,
            HeaderOutro = headerOutro,
            HeaderIpi = headerIpi,
            FatOrig = fatOrig,
            FatDesc = fatDesc,
            FatLiq = fatLiq,
            DupSum = dupSum,
            PagSum = pagSum,
            Reconciliation = reconciliation,
            Items = items,
        };
    }

    private sealed class RawNfeLine
    {
        public string Cprod { get; set; } = "";
        public string? PackBarcode { get; set; }
        public string? UnitBarcode { get; set; }
        public string Name { get; set; } = "";
        public string UCom { get; set; } = "UN";
        public double QCom { get; set; }
        public double VUnCom { get; set; }
        public string UTrib { get; set; } = "";
        public double QTrib { get; set; }
        public double VUnTrib { get; set; }
        public double VProd { get; set; }
        public double VIpi { get; set; }
        public double VIcmsSt { get; set; }
        public double VIcmsStRet { get; set; }
        public double VFcpSt { get; set; }
        public double VFcpStRet { get; set; }
        public double VFrete { get; set; }
        public double VSeg { get; set; }
        public double VOutro { get; set; }
        public double VDesc { get; set; }
        public double? VItem { get; set; }
        public string Cfop { get; set; } = "";
        public int? IndTot { get; set; }
        public string InfAdProd { get; set; } = "";
        public double PackFactorFromProduct { get; set; } = 1;
        public int? MatchedId { get; set; }
        public string? MatchedName { get; set; }
        public string LotNumber { get; set; } = "";
        public string? ExpiryDateIso { get; set; }
        public bool HasXmlRastro { get; set; }
    }

    /// <summary>Lê &lt;rastro&gt; (nLote / dVal). Se houver vários, usa o de validade mais próxima.</summary>
    private static (string LotNumber, string? ExpiryDateIso, bool HasRastro) ParseRastro(XElement det)
    {
        var rastros = det.Elements().Where(e => e.Name.LocalName == "rastro").ToList();
        if (rastros.Count == 0)
        {
            var prod = Child(det, "prod");
            if (prod is not null)
                rastros = prod.Elements().Where(e => e.Name.LocalName == "rastro").ToList();
        }

        if (rastros.Count == 0)
            return ("", null, false);

        string? bestLot = null;
        DateTime? bestExp = null;
        foreach (var r in rastros)
        {
            var nLote = (Value(r, "nLote") ?? "").Trim();
            var dValRaw = (Value(r, "dVal") ?? "").Trim();
            DateTime? exp = null;
            if (!string.IsNullOrWhiteSpace(dValRaw) && DateTime.TryParse(dValRaw, out var dt))
                exp = dt.Date;

            if (exp is null && string.IsNullOrWhiteSpace(nLote))
                continue;

            if (bestExp is null || (exp is not null && exp < bestExp))
            {
                bestExp = exp;
                bestLot = nLote;
            }
            else if (bestExp is null && exp is null && string.IsNullOrWhiteSpace(bestLot))
            {
                bestLot = nLote;
            }
        }

        var has = !string.IsNullOrWhiteSpace(bestLot) || bestExp is not null;
        return (bestLot ?? "", bestExp?.ToString("yyyy-MM-dd"), has);
    }

    /// <summary>
    /// Grava fornecedor (se necessário), produtos novos (se solicitado) e a compra.
    /// </summary>
    public static NfeImportApplyResult Apply(
        NfeImportPreview preview,
        bool createMissingProducts,
        bool updateStock,
        bool updateCost,
        double? marginPercent = null)
    {
        if (preview.Items.Count == 0)
            throw new InvalidOperationException("Nota sem itens para importar.");

        if (!string.IsNullOrWhiteSpace(preview.Chave)
            && PurchaseService.NfeKeyExists(preview.Chave))
        {
            throw new InvalidOperationException("Esta NF-e já foi importada anteriormente (mesma chave de acesso).");
        }

        var supplier = EnsureSupplier(preview);

        var purchaseItems = new List<PurchaseItemInput>();
        var createdProducts = 0;
        var supplierId = supplier.Id;

        foreach (var item in preview.Items)
        {
            int productId;
            if (item.MatchedProductId is int existingId)
            {
                productId = existingId;
                var existing = ProductService.GetById(existingId);
                if (existing is not null)
                    ProductService.EnsureCleanCatalogName(existing, item.Name);
                if (item.PackFactor > 1)
                    TryUpdatePackInfo(productId, item);
                if (item.SalePrice > 0 && updateCost)
                    ApplySalePrice(productId, item, updateCostPrice: false);
            }
            else
            {
                // Revalida barcode/nome — evita duplicar se o cadastro já tem o EAN
                var resolved = ResolveExistingProduct(item);
                if (resolved is not null)
                {
                    productId = resolved.Id;
                    ProductService.EnsureCleanCatalogName(resolved, item.Name);
                    if (item.PackFactor > 1)
                        TryUpdatePackInfo(productId, item);
                    if (item.SalePrice > 0 && updateCost)
                        ApplySalePrice(productId, item, updateCostPrice: false);
                    item.MatchedProductId = productId;
                    item.MatchedProductName = resolved.Name;
                }
                else
                {
                    if (!createMissingProducts)
                        throw new InvalidOperationException(
                            $"Produto \"{item.Name}\" não encontrado e a criação automática de produtos está desativada.");

                    var catalogName = ProductClassificationHelper.NormalizeCommercialName(item.Name);
                    var packFactor = item.PackFactor > 1 ? item.PackFactor : 1;
                    var inferred = ProductClassificationHelper.Infer(catalogName);
                    var isCigPack = ProductClassificationHelper.UsesPackPurchasePrice(catalogName, inferred.Group);
                    if (isCigPack)
                        packFactor = ProductPriceHelper.ResolveCigarettesPerPack(catalogName, packFactor);
                    var costStore = ProductPriceHelper.ResolveCatalogCost(
                        item.UnitPrice, packFactor, catalogName, inferred.Group,
                        item.TotalValue, item.Quantity);
                    var saleStore = ProductPriceHelper.ResolveCatalogSale(
                        item.SalePrice, item.UnitPrice, packFactor, catalogName, inferred.Group, marginPercent);

                    var extra = new ProductExtra
                    {
                        Marca = inferred.Brand,
                        FatorEmbalagem = packFactor,
                        BarcodeEmbalagem = TextNorm.DistinctPackBarcode(item.PackBarcode, item.Barcode),
                        QtdAtacado = packFactor > 1 ? packFactor : 0,
                        PrecoAtacado = isCigPack ? saleStore : (packFactor > 1 ? ProductPriceCalculator.RoundPrice(saleStore * packFactor) : saleStore),
                        PrecoCompra = costStore,
                        ControleValidade = ProductClassificationHelper.SuggestsExpiryControl(catalogName, inferred.Group),
                    };
                    if (saleStore > 0)
                        extra.LucroPercent = ProductPriceHelper.MarginOnSale(costStore, saleStore);

                    // Última trava: se o barcode já existir (corrida / parsing), vincula
                    var barcodeGuard = ProductService.FindByBarcodeOrPack(item.Barcode)
                        ?? ProductService.FindByBarcodeOrPack(item.PackBarcode);
                    if (barcodeGuard is not null)
                    {
                        productId = barcodeGuard.Id;
                        ProductService.EnsureCleanCatalogName(barcodeGuard, item.Name);
                    }
                    else
                    {
                        var created = ProductService.Create(new ProductInput
                        {
                            Barcode = item.Barcode ?? item.PackBarcode,
                            Name = catalogName,
                            GroupName = inferred.Group,
                            Unit = "UN",
                            CostPrice = costStore,
                            SalePrice = saleStore,
                            Stock = 0,
                            Extra = extra,
                        });
                        productId = created.Id;
                        createdProducts++;
                    }
                }
            }

            purchaseItems.Add(ToPurchaseItem(productId, item));
        }

        var purchaseTotal = ProductPriceCalculator.RoundPrice(purchaseItems.Sum(i => i.Quantity * i.UnitPrice));
        var dueBr = DateBrHelper.AddDaysBr(DateBrHelper.TodayBr(), 28);
        var financeiro = new PurchaseFinanceiroMeta
        {
            Entrada = 0,
            Qtd = 1,
            Parcelas =
            [
                new PurchaseParcelaDraft
                {
                    Vencimento = dueBr,
                    Tipo = "Boleto",
                    Valor = purchaseTotal,
                },
            ],
        };
        var baseNotes = $"Importado via XML NF-e{(string.IsNullOrWhiteSpace(preview.Chave) ? "" : $" — chave {preview.Chave}")}";

        var input = new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = ResolveEmissionDateIso(preview.DataEmissao),
            EntryDate = DateBrHelper.TodayIso(),
            Series = preview.Serie,
            Number = preview.Numero,
            NfeKey = string.IsNullOrWhiteSpace(preview.Chave) ? null : preview.Chave,
            GerarEstoque = updateStock,
            UpdateAverageCost = updateCost,
            Notes = PurchaseFinanceHelper.AppendFinanceiroToNotes(baseNotes, financeiro),
            Items = purchaseItems,
        };

        // Sempre fecha a compra para lançar Contas a Pagar (parcela pendente — ainda não paga).
        // Estoque só entra se updateStock=true.
        var purchaseId = PurchaseService.Create(input, closeOnSave: true);

        // Meta de embalagem / venda da tela Importar XML (custo médio já entrou na tx da compra).
        if (updateStock)
            ApplyPackMetaAfterPurchase(preview, purchaseItems, updateCost, marginPercent);

        return new NfeImportApplyResult
        {
            PurchaseId = purchaseId,
            SupplierId = supplierId,
            SupplierName = supplier.Name,
            SupplierCreated = supplier.Created,
            ProductsCreated = createdProducts,
            StockUpdated = updateStock,
            CostUpdated = updateStock && updateCost,
        };
    }

    private static void ApplyPackMetaAfterPurchase(
        NfeImportPreview preview,
        List<PurchaseItemInput> purchaseItems,
        bool updateCost,
        double? marginPercent)
    {
        for (var i = 0; i < preview.Items.Count && i < purchaseItems.Count; i++)
        {
            var item = preview.Items[i];
            var purchaseItem = purchaseItems[i];
            var product = ProductService.GetById(purchaseItem.ProductId);
            if (product is null) continue;

            var group = product.GroupName;
            var extra = ProductExtra.Parse(product.ExtraJson);
            ProductClassificationHelper.FillMissing(product.Name, ref group, extra);

            var packFactor = item.PackFactor > 1 ? item.PackFactor : 1;
            var usePack = ProductClassificationHelper.UsesPackPurchasePrice(product.Name, group)
                          && packFactor >= 2;
            var cigsPerPack = ProductClassificationHelper.UsesPackPurchasePrice(product.Name, group)
                ? ProductPriceHelper.ResolveCigarettesPerPack(product.Name, packFactor)
                : packFactor;

            if (packFactor > 1.0001)
            {
                if (extra.FatorEmbalagem <= 1 || ProductClassificationHelper.UsesPackPurchasePrice(product.Name, group))
                    extra.FatorEmbalagem = cigsPerPack >= 2 ? cigsPerPack : packFactor;
                if (extra.QtdAtacado <= 1 || ProductClassificationHelper.UsesPackPurchasePrice(product.Name, group))
                    extra.QtdAtacado = extra.FatorEmbalagem;
                if (string.IsNullOrWhiteSpace(extra.BarcodeEmbalagem))
                    extra.BarcodeEmbalagem = TextNorm.DistinctPackBarcode(item.PackBarcode, item.Barcode ?? product.Barcode);
            }

            var costUnit = Math.Max(0, item.UnitPrice);
            var sale = product.SalePrice;
            var costToStore = product.CostPrice;

            if (updateCost)
            {
                if (item.SalePrice > 0 || usePack)
                {
                    sale = ProductPriceHelper.ResolveCatalogSale(
                        item.SalePrice, costUnit, Math.Max(packFactor, cigsPerPack), product.Name, group);
                    if (usePack)
                        extra.PrecoAtacado = sale;
                    else if (packFactor > 1 && sale > 0)
                        extra.PrecoAtacado = ProductPriceCalculator.RoundPrice(sale * packFactor);
                }

                // Venda abaixo do custo do maço → recalcula com a margem da tela (padrão 30%).
                if (sale <= 0 || (costToStore > 0.009 && sale + 0.009 < costToStore))
                {
                    var margin = marginPercent is > 0 ? marginPercent.Value : 30;
                    sale = ProductPriceHelper.SaleFromCostAndMargin(costToStore, margin);
                    if (usePack && sale > 0)
                        extra.PrecoAtacado = sale;
                }

                if (sale > 0 && costToStore > 0)
                    extra.LucroPercent = ProductPriceHelper.MarginOnSale(costToStore, sale);
            }

            ProductService.Update(product.Id, new ProductInput
            {
                Code = product.Code,
                Barcode = product.Barcode,
                Name = product.Name,
                GroupName = group,
                Unit = string.IsNullOrWhiteSpace(product.Unit) ? "UN" : product.Unit,
                CostPrice = updateCost ? costToStore : product.CostPrice,
                SalePrice = updateCost && sale > 0 ? sale : product.SalePrice,
                MinStock = product.MinStock,
                Stock = product.Stock,
                Location = product.Location,
                Extra = extra,
                Active = product.Active,
            });
        }
    }

    /// <summary>Garante fornecedor do emitente da NF-e (cria se necessário).</summary>
    public static int EnsureSupplierId(NfeImportPreview preview) =>
        EnsureSupplier(preview).Id;

    private sealed class EnsureSupplierResult
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public bool Created { get; init; }
    }

    private static EnsureSupplierResult EnsureSupplier(NfeImportPreview preview)
    {
        var digits = TextNorm.DigitsOnly(preview.EmitenteCnpj, 14);
        if (string.IsNullOrWhiteSpace(digits))
            throw new InvalidOperationException("CNPJ do emitente não encontrado no XML.");

        var existing = PersonService.FindByCnpjDigits(digits);
        if (existing is not null)
        {
            var roles = existing.Roles;
            var needRole = !roles.Fornecedores || !roles.Clientes || !existing.Active;
            var needAddress = string.IsNullOrWhiteSpace(existing.Address)
                && !string.IsNullOrWhiteSpace(preview.EmitenteAddress);

            if (needRole || needAddress)
            {
                roles.Fornecedores = true;
                roles.Clientes = true;
                roles.Ativo = true;
                var updated = PersonService.Update(existing.Id, BuildSupplierInput(preview, existing, roles),
                    requireClienteRole: false);
                return new EnsureSupplierResult { Id = updated.Id, Name = updated.Name, Created = false };
            }

            return new EnsureSupplierResult { Id = existing.Id, Name = existing.Name, Created = false };
        }

        var created = PersonService.Create(
            BuildSupplierInput(preview, null, new PersonRoles { Ativo = true, Fornecedores = true, Clientes = true }),
            requireClienteRole: false);
        return new EnsureSupplierResult { Id = created.Id, Name = created.Name, Created = true };
    }

    private static PersonInput BuildSupplierInput(NfeImportPreview preview, Person? existing, PersonRoles roles)
    {
        var name = !string.IsNullOrWhiteSpace(preview.EmitenteNome)
            ? preview.EmitenteNome
            : (existing?.Name ?? preview.EmitenteCnpj);

        return new PersonInput
        {
            PersonKind = "juridica",
            Name = name,
            TradeName = FirstNonEmpty(preview.EmitenteFantasia, existing?.TradeName),
            CpfCnpj = preview.EmitenteCnpj,
            RgIe = FirstNonEmpty(preview.EmitenteIe, existing?.RgIe),
            Phone = FirstNonEmpty(preview.EmitentePhone, existing?.Phone),
            Cep = FirstNonEmpty(preview.EmitenteCep, existing?.Cep),
            Address = FirstNonEmpty(preview.EmitenteAddress, existing?.Address),
            AddressNumber = FirstNonEmpty(preview.EmitenteAddressNumber, existing?.AddressNumber),
            Complement = FirstNonEmpty(preview.EmitenteComplement, existing?.Complement),
            Neighborhood = FirstNonEmpty(preview.EmitenteNeighborhood, existing?.Neighborhood),
            City = FirstNonEmpty(preview.EmitenteCity, existing?.City),
            State = FirstNonEmpty(preview.EmitenteState, existing?.State),
            Roles = roles,
            Notes = existing?.Notes,
            Active = true,
        };
    }

    private static string? FirstNonEmpty(string? preferred, string? fallback) =>
        !string.IsNullOrWhiteSpace(preferred) ? preferred.Trim() : fallback;

    private static double SumTax(XElement det, string localName)
    {
        double sum = 0;
        foreach (var el in det.Descendants().Where(e => e.Name.LocalName == localName))
        {
            if (double.TryParse(el.Value.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                sum += v;
        }
        return sum;
    }

    private static string? MergeNotes(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a)) return b;
        if (string.IsNullOrWhiteSpace(b)) return a;
        return a + " · " + b;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ResolveEmissionDateIso(string? dhEmi)
    {
        if (!string.IsNullOrWhiteSpace(dhEmi) && DateTime.TryParse(
                dhEmi, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.ToString("yyyy-MM-dd");
        return DateBrHelper.TodayIso();
    }

    private static (int? Id, string? Name) MatchProduct(string? unitBarcode, string? packBarcode, string name)
    {
        foreach (var bc in new[] { unitBarcode, packBarcode })
        {
            if (string.IsNullOrWhiteSpace(bc))
                continue;
            var byBarcode = ProductService.FindByBarcodeOrPack(bc);
            if (byBarcode is not null)
                return (byBarcode.Id, byBarcode.Name);
        }

        // Nome bruto da NF
        var byName = FindByExactName(name);
        if (byName is not null)
            return (byName.Value.Id, byName.Value.Name);

        // Nome limpo (sem CX C/12, HW25, etc.) — cadastro costuma estar sanitizado
        var sanitized = ProductClassificationHelper.SanitizeProductName(name);
        if (!string.IsNullOrWhiteSpace(sanitized)
            && !string.Equals(sanitized, name.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            byName = FindByExactName(sanitized);
            if (byName is not null)
                return (byName.Value.Id, byName.Value.Name);
        }

        // Chave normalizada (sem acento / pontuação)
        var byNorm = FindByNormalizedName(sanitized.Length > 0 ? sanitized : name);
        if (byNorm is not null)
            return (byNorm.Value.Id, byNorm.Value.Name);

        return (null, null);
    }

    /// <summary>
    /// Resolve produto já cadastrado (barcode/fardo/nome). Nunca cria.
    /// Usado antes de Create para evitar duplicata.
    /// </summary>
    public static Product? ResolveExistingProduct(NfeImportItem item)
    {
        if (item.MatchedProductId is int mid)
        {
            var byId = ProductService.GetById(mid);
            if (byId is not null)
                return byId;
        }

        foreach (var bc in new[] { item.Barcode, item.PackBarcode })
        {
            if (string.IsNullOrWhiteSpace(bc))
                continue;
            var hit = ProductService.FindByBarcodeOrPack(bc);
            if (hit is not null)
                return hit;
        }

        var rematch = MatchProduct(item.Barcode, item.PackBarcode, item.Name);
        return rematch.Id is int id ? ProductService.GetById(id) : null;
    }

    /// <summary>
    /// Compra/estoque: cigarro na grade já vem em cigarros; se ainda estiver em maços (preço ≥ 4), converte.
    /// </summary>
    private static PurchaseItemInput ToPurchaseItem(int productId, NfeImportItem item)
    {
        var qty = item.Quantity;
        var unit = item.UnitPrice;
        var group = ProductClassificationHelper.Infer(item.Name).Group;
        if (ProductClassificationHelper.UsesPackPurchasePrice(item.Name, group)
            && item.PackFactor >= 2
            && item.UnitPrice >= 4.0)
        {
            // Grade ainda em maços → estoque em cigarros
            var cigs = ProductPriceHelper.ResolveCigarettesPerPack(item.Name, item.PackFactor);
            if (cigs >= 2)
            {
                qty = Math.Round(item.Quantity * cigs, 4);
                var lineTotal = item.TotalValue > 0.009
                    ? item.TotalValue
                    : ProductPriceCalculator.RoundPrice(item.Quantity * item.UnitPrice);
                unit = qty > 0 ? Math.Round(lineTotal / qty, 6) : 0;
            }
        }

        return new PurchaseItemInput
        {
            ProductId = productId,
            ProductName = item.Name,
            Quantity = qty,
            UnitPrice = unit,
            LotNumber = item.LotNumber,
            ExpiryDate = item.ExpiryDate,
        };
    }

    private static void ApplySalePrice(int productId, NfeImportItem item, bool updateCostPrice = true)
    {
        var product = ProductService.GetById(productId);
        if (product is null) return;

        var packFactor = item.PackFactor > 1 ? item.PackFactor : 1;
        var extra = ProductExtra.Parse(product.ExtraJson);
        var group = product.GroupName;
        ProductClassificationHelper.FillMissing(product.Name, ref group, extra);

        var usePack = ProductClassificationHelper.UsesPackPurchasePrice(product.Name, group)
                      && packFactor >= 2;
        var costStore = ProductPriceHelper.ResolveCatalogCost(
            item.UnitPrice, packFactor, product.Name, group,
            item.TotalValue > 0 ? item.TotalValue : item.Quantity * item.UnitPrice,
            item.Quantity);
        var saleStore = ProductPriceHelper.ResolveCatalogSale(
            item.SalePrice, item.UnitPrice, packFactor, product.Name, group);

        if (packFactor > 1)
        {
            extra.FatorEmbalagem = ProductPriceHelper.ResolveCigarettesPerPack(product.Name, packFactor);
            extra.QtdAtacado = extra.FatorEmbalagem;
        }

        // Custo médio fica em ApplyPackMetaAfterPurchase (usa custo original + esta NF).
        if (updateCostPrice)
            extra.PrecoCompra = costStore;
        if (usePack)
            extra.PrecoAtacado = saleStore;
        else if (packFactor > 1 && saleStore > 0)
            extra.PrecoAtacado = ProductPriceCalculator.RoundPrice(saleStore * packFactor);
        var costForMargin = updateCostPrice ? costStore : product.CostPrice;
        if (saleStore > 0 && costForMargin > 0)
            extra.LucroPercent = ProductPriceHelper.MarginOnSale(costForMargin, saleStore);

        ProductService.Update(productId, new ProductInput
        {
            Code = product.Code,
            Barcode = product.Barcode,
            Name = product.Name,
            GroupName = group,
            Unit = string.IsNullOrWhiteSpace(product.Unit) ? "UN" : product.Unit,
            CostPrice = updateCostPrice ? costStore : product.CostPrice,
            SalePrice = saleStore > 0 ? saleStore : product.SalePrice,
            MinStock = product.MinStock,
            Stock = product.Stock,
            Location = product.Location,
            Extra = extra,
            Active = product.Active,
        });
    }

    private static void TryUpdatePackInfo(int productId, NfeImportItem item)
    {
        var product = ProductService.GetById(productId);
        if (product is null) return;

        var extra = ProductExtra.Parse(product.ExtraJson);
        var changed = false;
        if (extra.FatorEmbalagem <= 1 && item.PackFactor > 1)
        {
            extra.FatorEmbalagem = item.PackFactor;
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(extra.BarcodeEmbalagem)
            && !string.IsNullOrWhiteSpace(item.PackBarcode)
            && TextNorm.DistinctPackBarcode(item.PackBarcode, product.Barcode) is { } packBc)
        {
            extra.BarcodeEmbalagem = packBc;
            changed = true;
        }
        if (!changed) return;

        ProductService.Update(productId, new ProductInput
        {
            Code = product.Code,
            Barcode = product.Barcode,
            Name = product.Name,
            GroupName = product.GroupName,
            Unit = string.IsNullOrWhiteSpace(product.Unit) ? "UN" : product.Unit,
            CostPrice = product.CostPrice,
            SalePrice = product.SalePrice,
            MinStock = product.MinStock,
            Stock = product.Stock,
            Location = product.Location,
            Extra = extra,
            Active = product.Active,
        });
    }

    /// <summary>
    /// Cigarro na grade de compra/NF = <b>cigarros</b> (unidade de estoque).
    /// PackFactor = cigarros/maço; custo/venda de cadastro continuam em maço via ResolveCatalogCost.
    /// </summary>
    private static (string Unit, double Quantity, double UnitPrice, double PackFactor, string? Note)
        ConvertCigaretteLineToStockUnits(
            string uCom, double qCom, double vUnCom,
            string uTrib, double qTrib, double vUnTrib,
            double total, double productFactor, string productName)
    {
        var cigsPerPack = ProductPriceHelper.ResolveCigarettesPerPack(
            productName, productFactor >= 2 ? productFactor : 20);
        if (cigsPerPack < 2)
            cigsPerPack = 20;

        var nameFactor = InferPackFactorFromName(productName);
        double totalCigarettes;
        string basis;

        if (IsMilUnit(uCom) || IsMilUnit(uTrib))
        {
            var milQty = IsMilUnit(uCom) ? qCom : qTrib;
            if (milQty <= 0) milQty = qCom;
            totalCigarettes = Math.Round(milQty * 1000.0, 4);
            basis = $"{FormatQty(milQty)} MIL";
        }
        else if (qCom > 0 && qTrib > qCom * 2.5)
        {
            // Ex.: 2 CX → 400 UN (cigarros) na tribuição
            totalCigarettes = qTrib;
            basis = $"{FormatQty(qCom)} {uCom} → {FormatQty(qTrib)} cig (trib)";
        }
        else if (nameFactor > 30 && (IsPackUnit(uCom) || vUnCom >= 40))
        {
            // BOX 200s / cartela: cada UN comercial = N cigarros
            totalCigarettes = Math.Round(qCom * nameFactor, 4);
            basis = $"{FormatQty(qCom)} × {FormatQty(nameFactor)} cig (BOX/cartela)";
        }
        else if (vUnCom > 0 && vUnCom < 4.0)
        {
            // NF já em cigarros
            totalCigarettes = qCom;
            basis = $"{FormatQty(qCom)} cigarros";
        }
        else
        {
            // Souza Cruz típico: UN/PC = maço → expande para cigarros (estoque)
            totalCigarettes = Math.Round(qCom * cigsPerPack, 4);
            basis = $"{FormatQty(qCom)} maços × {FormatQty(cigsPerPack)}";
        }

        if (totalCigarettes <= 0)
            totalCigarettes = qCom > 0 ? qCom : 1;

        var priceCig = totalCigarettes > 0
            ? (total > 0 ? total / totalCigarettes : vUnCom / Math.Max(cigsPerPack, 1))
            : 0;
        var macos = Math.Round(totalCigarettes / cigsPerPack, 2);
        var note =
            $"{basis} → {FormatQty(totalCigarettes)} cig (≈ {FormatQty(macos)} maços × {ProductPriceHelper.MoneyBr(priceCig * cigsPerPack)})";
        return ("UN", totalCigarettes, Math.Round(priceCig, 6), cigsPerPack, note);
    }

    /// <summary>
    /// Converte quantidade da NF (fardo/CX/MIL) para unidades de venda usadas no estoque/PDV.
    /// Cigarro: grade em cigarros (estoque); custo maço só no cadastro/média.
    /// </summary>
    private static (string Unit, double Quantity, double UnitPrice, double PackFactor, string? Note)
        ConvertPackToSaleUnits(
            string uCom, double qCom, double vUnCom,
            string uTrib, double qTrib, double vUnTrib,
            double total, double productFactor, string productName)
    {
        // Souza Cruz / cigarro: estoque em cigarros (Qtd/Preço unitário na compra).
        if (ProductClassificationHelper.IsCigarette(productName))
            return ConvertCigaretteLineToStockUnits(
                uCom, qCom, vUnCom, uTrib, qTrib, vUnTrib, total, productFactor, productName);

        // Demais produtos: MIL (milheiro) → unidades
        if (IsMilUnit(uCom) || IsMilUnit(uTrib))
        {
            var milQty = IsMilUnit(uCom) ? qCom : qTrib;
            if (milQty <= 0) milQty = qCom;
            var qty = Math.Round(milQty * 1000.0, 4);
            var price = qty > 0
                ? (total > 0 ? total / qty : (IsMilUnit(uCom) ? vUnCom : vUnTrib) / 1000.0)
                : 0;
            var factor = productFactor >= 2 ? productFactor : 1;
            var note = $"{FormatQty(milQty)} MIL = {FormatQty(qty)} UN";
            return ("UN", qty, price, factor >= 2 ? factor : 1, note);
        }

        var packLike = IsPackUnit(uCom);
        var tribIsUnit = string.IsNullOrWhiteSpace(uTrib) || IsSaleUnit(uTrib);
        var inferredFactor = qCom > 0 && qTrib > qCom + 0.0001
            ? Math.Round(qTrib / qCom, 4)
            : 0;
        var nameFactor = InferPackFactorFromName(productName);
        var factorHint = inferredFactor >= 2 ? inferredFactor
            : productFactor >= 2 ? productFactor
            : nameFactor >= 2 ? nameFactor
            : 0;

        // Caso típico: 3 EB → 36 UN (fator 12 via qTrib)
        if (packLike && tribIsUnit && qTrib > 0 && inferredFactor >= 2)
        {
            var factor = inferredFactor;
            var qty = qTrib;
            var price = qty > 0 ? (total > 0 ? total / qty : vUnTrib) : 0;
            var note = $"{FormatQty(qCom)} {uCom} × {FormatQty(factor)} = {FormatQty(qty)} UN";
            return ("UN", qty, price, factor, note);
        }

        // Fardo/CX sem qTrib útil, ou descrição "PET 12" / "C/6".
        // Se a NF já veio em UN (ex.: 18 isqueiros), NÃO multiplica pelo fator do cadastro/nome.
        if (factorHint >= 2 && qCom > 0)
        {
            var metaFactor = productFactor >= 2 ? productFactor
                : nameFactor >= 2 ? nameFactor
                : factorHint;

            // NF em unidade de venda: qtd já é o estoque; fator fica só como meta (cartela/CX).
            if (!packLike && inferredFactor < 2)
            {
                var unitSkip = string.IsNullOrWhiteSpace(uCom) ? "UN" : uCom[..Math.Min(10, uCom.Length)];
                var note = metaFactor >= 2
                    ? $"NF em {unitSkip}; cartela/CX {FormatQty(metaFactor)} un (não multiplica qtd)"
                    : null;
                return (unitSkip, qCom, total > 0 && qCom > 0 ? total / qCom : vUnCom, metaFactor >= 2 ? metaFactor : 1, note);
            }

            // CX/FD sem qTrib: só multiplica se qCom parece contagem de caixas (ex.: 2 CX × 12).
            // Se qCom >= fator (ex.: 24 CX com fator 12), a NF/digitação já veio em unidades de venda.
            if (packLike && inferredFactor < 2 && qCom + 0.0001 >= factorHint)
            {
                var noteSkip =
                    $"{FormatQty(qCom)} {uCom} já como UN (fator {FormatQty(metaFactor)} — não multiplica)";
                return ("UN", qCom, total > 0 && qCom > 0 ? total / qCom : vUnCom, metaFactor >= 2 ? metaFactor : factorHint, noteSkip);
            }

            var qty = Math.Round(qCom * factorHint, 4);
            var price = qty > 0 ? (total > 0 ? total / qty : vUnCom / factorHint) : 0;
            var origem = inferredFactor >= 2 ? uCom
                : productFactor >= 2 ? "fator do produto"
                : "descrição";
            var notePack = $"{FormatQty(qCom)} {(packLike ? uCom : "FD")} × {FormatQty(factorHint)} = {FormatQty(qty)} UN ({origem})";
            return ("UN", qty, price, factorHint, notePack);
        }

        var unit = string.IsNullOrWhiteSpace(uCom) ? "UN" : uCom[..Math.Min(10, uCom.Length)];
        return (unit, qCom, total > 0 && qCom > 0 ? total / qCom : vUnCom, 1, null);
    }

    /// <summary>Extrai qtd do fardo pelo nome: C/12, CX12, DP16X29G, 12UN, PET 12, etc.</summary>
    public static double InferPackFactorFromProductName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return 0;

        var n = name.ToUpperInvariant();
        string[] patterns =
        [
            @"\bC/\s*(\d{1,3})\b",
            @"\b(?:PACOTES?|PACOTE|PCTE|PCT|PAC)\.?\s*C?\s*/?\s*(\d{1,3})(?:UN)?\b",
            @"CX(\d{1,3})X",                 // CX100X15G, CX23X300ML (antes do \bCX\d\b)
            @"\bCX\s*(\d{1,3})(?:X|\b|/)",  // CX100X…, CX23, CX/12
            @"\bCX\s*C?/?\s*(\d{1,3})\b",
            @"\bCX(\d{1,3})\b",
            @"\bDP\s*(\d{1,3})(?:X|\b|/)",   // DP16X29G, DP15X41G (display)
            @"\bLT\s*(\d{1,3})\b",
            @"\bLT(\d{1,3})\b",
            @"(\d{1,3})\s*UN\b",
            @"(\d{1,3})UN\b",
            @"\b(\d{1,3})U\b",
            @"\bPET\s+(\d{1,3})\b",
            @"\b(\d{1,3})X1\b",
            @"(\d{1,3})X\d{1,3}\s*,?\d*\s*G\b", // 100X15G / 16X37,5GR
            @"\b(\d{1,3})X\d{1,3}G\b",
            @"\bX\s*(\d{1,3})\b",
            @"\b(\d{1,3})\s*JD\b",
            @"\bBOX\s*(\d{2,3})\s*S\b",     // BOX 200s = 200 cigarros/pacote
            @"\b(\d{2,3})\s*S\b",            // 200s
        ];

        foreach (var pattern in patterns)
        {
            var m = Regex.Match(n, pattern);
            if (!m.Success) continue;
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var f))
                continue;
            if (f is >= 2 and <= 500)
                return f;
        }

        return 0;
    }

    private static double InferPackFactorFromName(string? name) =>
        InferPackFactorFromProductName(name);

    private static bool IsMilUnit(string unit)
    {
        var u = unit.Trim().ToUpperInvariant();
        return u is "MIL" or "MI" or "MILHEIRO";
    }

    private static bool IsPackUnit(string unit)
    {
        var u = unit.Trim().ToUpperInvariant();
        return u is "EB" or "CX" or "CXA" or "FD" or "FARDO" or "PCT" or "DP" or "DZ"
            or "DISPLAY" or "CJ" or "KIT" or "SC" or "BDJ" or "BANDEJA"
            or "CT" or "CRT" or "CARTELA" or "CART";
    }

    private static bool IsSaleUnit(string unit)
    {
        var u = unit.Trim().ToUpperInvariant();
        return u is "UN" or "UND" or "UNID" or "LT" or "LATA" or "PECA" or "PÇ" or "KG" or "G";
    }

    private static string FormatQty(double v) =>
        Math.Abs(v - Math.Round(v)) < 0.0001 ? ((int)Math.Round(v)).ToString() : v.ToString("0.####");

    private static (int Id, string Name)? FindByExactName(string name)
    {
        var upper = name.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(upper))
            return null;

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM products WHERE active = 1 AND UPPER(name) = $name LIMIT 1;";
        cmd.Parameters.AddWithValue("$name", upper);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? (reader.GetInt32(0), reader.GetString(1)) : null;
    }

    /// <summary>
    /// Compara chave normalizada (sem acento) entre nome da NF e candidatos do cadastro.
    /// </summary>
    private static (int Id, string Name)? FindByNormalizedName(string name)
    {
        var key = NormalizeNameKey(name);
        if (key.Length < 4)
            return null;

        var words = key.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3)
            .Take(3)
            .ToList();
        if (words.Count == 0)
            return null;

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var clauses = new List<string>();
        for (var i = 0; i < words.Count; i++)
            clauses.Add($"UPPER(name) LIKE $w{i} ESCAPE '\\'");
        cmd.CommandText =
            $"SELECT id, name FROM products WHERE active = 1 AND {string.Join(" AND ", clauses)} LIMIT 50;";
        for (var i = 0; i < words.Count; i++)
            cmd.Parameters.AddWithValue($"$w{i}", "%" + EscapeLike(words[i]) + "%");

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt32(0);
            var catalogName = reader.GetString(1);
            if (NormalizeNameKey(catalogName) == key)
                return (id, catalogName);
            // Também aceita se o nome sanitizado do cadastro bater
            var catalogClean = ProductClassificationHelper.SanitizeProductName(catalogName);
            if (NormalizeNameKey(catalogClean) == key)
                return (id, catalogName);
        }

        return null;
    }

    private static string NormalizeNameKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var cleaned = ProductClassificationHelper.SanitizeProductName(name);
        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = name.Trim();

        var formD = cleaned.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        var noAccents = sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        return Regex.Replace(noAccents, @"[^A-Z0-9]+", " ").Trim();
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static string ExtractChave(XDocument doc, XElement infNFe)
    {
        var protNFe = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "infProt");
        var chNFe = Value(protNFe, "chNFe");
        if (!string.IsNullOrWhiteSpace(chNFe))
            return TextNorm.DigitsOnly(chNFe) ?? chNFe.Trim();

        var idAttr = infNFe.Attribute("Id")?.Value ?? "";
        return new string(idAttr.Where(char.IsDigit).ToArray());
    }

    private static string? NormalizeEan(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrWhiteSpace(v))
            return null;
        if (v.Equals("SEM GTIN", StringComparison.OrdinalIgnoreCase))
            return null;
        var digits = new string(v.Where(char.IsDigit).ToArray());
        return string.IsNullOrEmpty(digits) ? null : digits;
    }

    private static double ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        return double.TryParse(value.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static XElement? Child(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    private static string? Value(XElement? parent, string localName) =>
        Child(parent, localName)?.Value;
}
