namespace SGDB.Domain.Purchases;

/// <summary>Componentes fiscais/comerciais de uma linha de NF-e. Sem persistência.</summary>
public sealed record NfeEffectiveCostInput
{
    public double VProd { get; init; }
    public double QCom { get; init; }
    public string UCom { get; init; } = "UN";
    public double VUnCom { get; init; }
    public double QTrib { get; init; }
    public string UTrib { get; init; } = "";
    public double VUnTrib { get; init; }
    public double VDesc { get; init; }
    public double VFrete { get; init; }
    public double VSeg { get; init; }
    public double VOutro { get; init; }
    public double VIpi { get; init; }
    public double VIcmsSt { get; init; }
    public double VIcmsStRet { get; init; }
    public double VFcpSt { get; init; }
    public double VFcpStRet { get; init; }
    public double? VItem { get; init; }
    public string? InfAdProd { get; init; }
    public string? InfCpl { get; init; }
    public string? Cfop { get; init; }
    public int? IndTot { get; init; }
    public string? EmitCnpj { get; init; }
    public string? EmitName { get; init; }
    public double HeaderVProd { get; init; }
    public double HeaderVNf { get; init; }
    public double HeaderSt { get; init; }
    public double HeaderDesc { get; init; }
    public double HeaderFrete { get; init; }
    public double HeaderOutro { get; init; }
    public double FatLiq { get; init; }
    public double DupSum { get; init; }
    public double PagSum { get; init; }
    public bool HeaderStUnallocated { get; init; }
    public bool HeaderFreightUnallocated { get; init; }
    public bool HeaderOtherUnallocated { get; init; }
    public bool HeaderDiscountUnallocated { get; init; }
}
