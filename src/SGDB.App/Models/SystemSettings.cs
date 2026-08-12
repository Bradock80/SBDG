using SGDB.Utils;

namespace SGDB.Models;

public sealed class CompanyProfile
{
    public string RazaoSocial { get; set; } = "";
    public string NomeFantasia { get; set; } = "";
    public string Cnpj { get; set; } = "";
    public string Ie { get; set; } = "";
    public string Endereco { get; set; } = "";
    public string Numero { get; set; } = "";
    public string Bairro { get; set; } = "";
    public string Cidade { get; set; } = "";
    public string Uf { get; set; } = "";
    public string Cep { get; set; } = "";
    public string Telefone { get; set; } = "";
    public string Email { get; set; } = "";
    public string PixKey { get; set; } = "";
    public string? LogoPath { get; set; }

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(NomeFantasia) ? NomeFantasia
        : !string.IsNullOrWhiteSpace(RazaoSocial) ? RazaoSocial
        : "Meu Depósito";

    public string AddressLine
    {
        get
        {
            var parts = new List<string>();
            var street = Endereco.Trim();
            if (!string.IsNullOrEmpty(Numero))
                street = string.IsNullOrEmpty(street) ? $"nº {Numero}" : $"{street}, {Numero}";
            if (!string.IsNullOrEmpty(street)) parts.Add(street);
            if (!string.IsNullOrEmpty(Bairro)) parts.Add(Bairro);
            var city = string.Join("/", new[] { Cidade, Uf }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrEmpty(city)) parts.Add(city);
            if (!string.IsNullOrEmpty(Cep)) parts.Add($"CEP {Cep}");
            return string.Join(" — ", parts);
        }
    }
}

public sealed class PrinterSettings
{
    public string PrinterName { get; set; } = "";
    public int PaperWidthMm { get; set; } = 80;
    public bool AutoCut { get; set; } = true;
    /// <summary>Imprime pré-conta no caixa quando o celular solicita fechamento.</summary>
    public bool AutoPrintDeckPreConta { get; set; }
    public string FooterText { get; set; } =
        "Agradecemos a preferência!\nConferir o troco e os cascos no ato da compra.";
    public int Copies { get; set; } = 1;
}

public sealed class PeripheralSettings
{
    public bool DrawerEnabled { get; set; } = true;
    public bool DrawerOpenOnCashSale { get; set; } = true;
    public string ScannerMode { get; set; } = "teclado";
    public bool ScaleEnabled { get; set; }
    public string ScalePort { get; set; } = "COM1";
    public int ScaleBaud { get; set; } = 9600;
    /// <summary>Toledo, Filizola, Urano, Elgin ou NTS.</summary>
    public string ScaleProtocol { get; set; } = "toledo";
}

public sealed class AuditLogRow
{
    public long Id { get; init; }
    public string CreatedAt { get; init; } = "";
    public string UserLogin { get; init; } = "";
    public string UserName { get; init; } = "";
    public string Action { get; init; } = "";
    public string Entity { get; init; } = "";
    public string? EntityId { get; init; }
    public string Details { get; init; } = "";

    public string DateDisplay =>
        string.IsNullOrWhiteSpace(CreatedAt)
            ? ""
            : DateBrHelper.FormatUtcToBrazil(CreatedAt, "dd/MM/yyyy HH:mm:ss");

    public string RiskLevel => AuditLogPresentation.GetRiskLevel(Action, Entity);

    public string ActionLabel => AuditLogPresentation.GetActionKey(Action, Entity);

    public string ActionBadgeLabel => AuditLogPresentation.GetActionBadgeLabel(Action, Entity);

    public string BadgeKind => AuditLogPresentation.GetBadgeKind(Action, Entity);

    public string ActionBadgeDisplay => AuditLogPresentation.GetActionBadgeDisplay(Action, Entity);

    public string EntityDisplay => AuditLogPresentation.GetEntityDisplay(Entity);

    public string ActionBadgeText => ActionBadgeDisplay;

    public string DetailsDisplay => AuditLogPresentation.GetDetailsDisplay(this);
}

public sealed class SystemUserRow
{
    public int Id { get; init; }
    public string Login { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Role { get; set; } = "vendedor";
    public bool Active { get; set; } = true;
    public string CreatedAt { get; init; } = "";

    public string RoleDisplay => Role.ToLowerInvariant() switch
    {
        "admin" => "Administrador",
        "gestor" => "Gestor",
        "vendedor" => "Vendedor",
        _ => Role,
    };
    public string ActiveDisplay => Active ? "Ativo" : "Aguardando";

    public UserPermissions Permissions { get; set; } = UserPermissions.ForRole("vendedor");
}
