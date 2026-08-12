namespace SGDB.Models;

public sealed class Person
{
    public int Id { get; init; }
    public string PersonKind { get; init; } = "juridica";
    public required string Name { get; init; }
    public string? TradeName { get; init; }
    public string? CpfCnpj { get; init; }
    public string? RgIe { get; init; }
    public string? Phone { get; init; }
    public string? Phone2 { get; init; }
    public string? Cell1 { get; init; }
    public string? Whatsapp { get; init; }
    public string? Cell2 { get; init; }
    public string? Email { get; init; }
    public string? Cep { get; init; }
    public string? Address { get; init; }
    public string? AddressNumber { get; init; }
    public string? Complement { get; init; }
    public string? Neighborhood { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? ReceiptType { get; init; }
    public string RolesJson { get; init; } = "{}";
    public string? Notes { get; init; }
    public bool Active { get; init; } = true;
    /// <summary>Acréscimo por unidade nas vendas fiado (0 = desligado). Ex.: 0,50.</summary>
    public double FiadoUnitSurcharge { get; init; }
    public string CreatedAt { get; init; } = "";

    public string TradeDisplay => TradeName ?? "";
    public string DocDisplay => CpfCnpj ?? "";
    public string RgDisplay => RgIe ?? "";
    public string StateDisplay => State ?? "";

    public string AddressDisplay => FormatAddress();

    public PersonRoles Roles => PersonRoles.Parse(RolesJson);

    private string FormatAddress()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Address))
        {
            var line = Address.Trim();
            if (!string.IsNullOrWhiteSpace(AddressNumber))
                line += ", " + AddressNumber.Trim();
            if (!string.IsNullOrWhiteSpace(Complement))
                line += " — " + Complement.Trim();
            parts.Add(line);
        }

        if (!string.IsNullOrWhiteSpace(Neighborhood))
            parts.Add(Neighborhood.Trim());

        var cityUf = "";
        if (!string.IsNullOrWhiteSpace(City))
            cityUf = City.Trim();
        if (!string.IsNullOrWhiteSpace(State))
            cityUf = string.IsNullOrEmpty(cityUf) ? State.Trim() : $"{cityUf}/{State.Trim()}";
        if (!string.IsNullOrEmpty(cityUf))
            parts.Add(cityUf);

        if (!string.IsNullOrWhiteSpace(Cep))
            parts.Add($"CEP {Cep.Trim()}");

        return string.Join(" — ", parts);
    }
}
