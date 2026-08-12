using System.Text.Json;
using System.Text.Json.Serialization;

namespace SGDB.Models;

/// <summary>Permissões granulares do usuário (JSON em users.permissions_json).</summary>
public sealed class UserPermissions
{
    public bool PdvVenda { get; set; }
    public bool PdvDesconto { get; set; }
    public bool PdvCancelarVenda { get; set; }
    public bool PdvTrocaDevolucao { get; set; }
    public bool PdvAlterarPagamento { get; set; }
    public bool PdvEditarVenda { get; set; }
    public bool PdvResumoDia { get; set; }
    public bool ClientesConsultar { get; set; }
    public bool ClientesEditar { get; set; }
    public bool ProdutosConsultar { get; set; }
    public bool ProdutosEditar { get; set; }
    public bool EstoqueAjustar { get; set; }
    public bool FinanceiroAcesso { get; set; }
    public bool RelatoriosAcesso { get; set; }
    public bool SistemaUsuarios { get; set; }
    public bool SistemaBackup { get; set; }

    public bool Customized { get; set; }

    public static UserPermissions ForRole(string role)
    {
        var r = (role ?? "vendedor").Trim().ToLowerInvariant();
        return r switch
        {
            "admin" => new UserPermissions
            {
                PdvVenda = true,
                PdvDesconto = true,
                PdvCancelarVenda = true,
                PdvTrocaDevolucao = true,
                PdvAlterarPagamento = true,
                PdvEditarVenda = true,
                PdvResumoDia = true,
                ClientesConsultar = true,
                ClientesEditar = true,
                ProdutosConsultar = true,
                ProdutosEditar = true,
                EstoqueAjustar = true,
                FinanceiroAcesso = true,
                RelatoriosAcesso = true,
                SistemaUsuarios = true,
                SistemaBackup = true,
            },
            "gestor" => new UserPermissions
            {
                PdvVenda = true,
                PdvDesconto = true,
                PdvCancelarVenda = true,
                PdvTrocaDevolucao = true,
                PdvAlterarPagamento = true,
                PdvEditarVenda = true,
                PdvResumoDia = true,
                ClientesConsultar = true,
                ClientesEditar = true,
                ProdutosConsultar = true,
                ProdutosEditar = true,
                EstoqueAjustar = true,
                FinanceiroAcesso = true,
                RelatoriosAcesso = true,
            },
            _ => new UserPermissions
            {
                PdvVenda = true,
                ClientesConsultar = true,
                ProdutosConsultar = true,
                FinanceiroAcesso = true, // fiado
                // Resumo do dia: só se liberar manualmente
            },
        };
    }

    public static UserPermissions Parse(string? json, string roleFallback)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ForRole(roleFallback);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var p = JsonSerializer.Deserialize<UserPermissions>(json);
            if (p is null)
                return ForRole(roleFallback);

            if (!p.Customized)
                return ForRole(roleFallback);

            // Chaves novas (ainda ausentes no JSON antigo) herdam o perfil
            var roleDefaults = ForRole(roleFallback);
            foreach (var (key, _) in Catalog)
            {
                if (!doc.RootElement.TryGetProperty(key, out _))
                    p.Set(key, roleDefaults.Get(key));
            }

            return p;
        }
        catch
        {
            return ForRole(roleFallback);
        }
    }

    public string ToJson() =>
        JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        });

    public static IReadOnlyList<(string Key, string Label)> Catalog =>
    [
        ("PdvVenda", "PDV — realizar vendas"),
        ("PdvDesconto", "PDV — dar desconto no balcão"),
        ("PdvCancelarVenda", "PDV — cancelar venda do dia"),
        ("PdvTrocaDevolucao", "PDV — troca / devolução de venda"),
        ("PdvAlterarPagamento", "PDV — alterar forma de pagamento"),
        ("PdvEditarVenda", "PDV — editar / trocar item da venda do dia"),
        ("PdvResumoDia", "PDV — ver resumo do dia"),
        ("ClientesConsultar", "Clientes — consultar cadastro"),
        ("ClientesEditar", "Clientes — cadastrar e alterar"),
        ("ProdutosConsultar", "Produtos — consultar catálogo"),
        ("ProdutosEditar", "Produtos — cadastrar e alterar"),
        ("EstoqueAjustar", "Estoque — ajustar quantidades"),
        ("FinanceiroAcesso", "Financeiro — caixa, fiado e contas"),
        ("RelatoriosAcesso", "Relatórios e Meu Negócio"),
        ("SistemaUsuarios", "Sistema — usuários e permissões"),
        ("SistemaBackup", "Sistema — backup e restauração"),
    ];

    public bool Get(string key) => key switch
    {
        "PdvVenda" => PdvVenda,
        "PdvDesconto" => PdvDesconto,
        "PdvCancelarVenda" => PdvCancelarVenda,
        "PdvTrocaDevolucao" => PdvTrocaDevolucao,
        "PdvAlterarPagamento" => PdvAlterarPagamento,
        "PdvEditarVenda" => PdvEditarVenda,
        "PdvResumoDia" => PdvResumoDia,
        "ClientesConsultar" => ClientesConsultar,
        "ClientesEditar" => ClientesEditar,
        "ProdutosConsultar" => ProdutosConsultar,
        "ProdutosEditar" => ProdutosEditar,
        "EstoqueAjustar" => EstoqueAjustar,
        "FinanceiroAcesso" => FinanceiroAcesso,
        "RelatoriosAcesso" => RelatoriosAcesso,
        "SistemaUsuarios" => SistemaUsuarios,
        "SistemaBackup" => SistemaBackup,
        _ => false,
    };

    public void Set(string key, bool value)
    {
        switch (key)
        {
            case "PdvVenda": PdvVenda = value; break;
            case "PdvDesconto": PdvDesconto = value; break;
            case "PdvCancelarVenda": PdvCancelarVenda = value; break;
            case "PdvTrocaDevolucao": PdvTrocaDevolucao = value; break;
            case "PdvAlterarPagamento": PdvAlterarPagamento = value; break;
            case "PdvEditarVenda": PdvEditarVenda = value; break;
            case "PdvResumoDia": PdvResumoDia = value; break;
            case "ClientesConsultar": ClientesConsultar = value; break;
            case "ClientesEditar": ClientesEditar = value; break;
            case "ProdutosConsultar": ProdutosConsultar = value; break;
            case "ProdutosEditar": ProdutosEditar = value; break;
            case "EstoqueAjustar": EstoqueAjustar = value; break;
            case "FinanceiroAcesso": FinanceiroAcesso = value; break;
            case "RelatoriosAcesso": RelatoriosAcesso = value; break;
            case "SistemaUsuarios": SistemaUsuarios = value; break;
            case "SistemaBackup": SistemaBackup = value; break;
        }
    }
}
