using System.Windows;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>Checagem de permissões do usuário logado (users.permissions_json / perfil).</summary>
public static class AccessControl
{
    private static readonly AsyncLocal<int> RemoteStoreRequestDepth = new();
    private static readonly AsyncLocal<StoreNetworkRemoteSession?> RemoteSessionLocal = new();

    /// <summary>
    /// Requisição RPC no host da Rede Loja. A sessão do PC servidor NÃO é a
    /// identidade do notebook — 68C ainda precisa transportar usuário/permissão.
    /// </summary>
    public static bool IsRemoteStoreRequest => RemoteStoreRequestDepth.Value > 0;

    /// <summary>Identidade remota da request atual. Null em PIN-only (68C2/B1).</summary>
    public static StoreNetworkRemoteSession? CurrentRemoteSession => RemoteSessionLocal.Value;

    public static IDisposable EnterRemoteStoreRequest() =>
        EnterRemoteStoreRequest(null);

    public static IDisposable EnterRemoteStoreRequest(StoreNetworkRemoteSession? session)
    {
        var previous = RemoteSessionLocal.Value;
        RemoteStoreRequestDepth.Value++;
        RemoteSessionLocal.Value = session;
        return new RemoteStoreRequestScope(previous);
    }

    /// <summary>
    /// Permissão do usuário da sessão local. Em RPC do host, não usa AppSession
    /// do servidor como prova do usuário do notebook.
    /// </summary>
    public static bool AllowsLocalUser(string key) =>
        IsRemoteStoreRequest || Can(key);

    public static UserPermissions Permissions =>
        AppSession.Permissions;

    public static bool Can(string key) => Permissions.Get(key);

    /// <summary>69T-F — manutenção de resíduos: somente Administrador ou Gestor (pelo perfil, não por RelatoriosAcesso).</summary>
    public static bool CanAccessLegacyMergeCleanup()
    {
        var role = AppSession.CurrentUser?.Role ?? "";
        return role.Equals("admin", StringComparison.OrdinalIgnoreCase)
            || role.Equals("gestor", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 70B3A-B — mutação de cobertura validade/lote: Administrador ou Gestor pelo perfil.
    /// Vendedor não muta mesmo com RelatoriosAcesso (consulta continua na UI futura).
    /// </summary>
    public static bool CanMutateLotCoverage() => CanAccessLegacyMergeCleanup();

    public static bool CanAccessModule(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId) || moduleId == "home")
            return true;

        var p = Permissions;
        return moduleId switch
        {
            "pdv" or "decks" => p.PdvVenda,
            "devolucao_venda" => p.PdvTrocaDevolucao,

            "clientes" => p.ClientesConsultar,

            "produtos" or "estoque_grupos" or "estoque_unidades" or "estoque_marcas"
                => p.ProdutosConsultar,

            "compras" or "estoque_importar_xml" or "estoque_inventario"
                or "ajusta_estoque" or "ajusta_saldo" or "estoque_zera_negativo"
                => p.EstoqueAjustar,

            "ajusta_preco" or "tabela_preco" => p.ProdutosEditar,

            "pagar" => p.ContasPagarAcesso,

            "caixa" or "fiado" or "contas_bancarias"
                or "vasilhame" or "tipos_vasilhame"
                or "categorias_financeiras" or "depositos_caixa"
                => p.FinanceiroAcesso,

            "inicio" or "relatorio" or "relatorio_vendas" or "relatorio_mais_vendidos"
                or "relatorio_fiado" or "relatorio_vendedores" or "relatorio_dre"
                or "relatorio_estoque_io"
                or "consultar_movimentacao" or "movimentacao_vendas" or "movimentacao_compras"
                or "estoque_curva_abc" or "estoque_mais_vendidos" or "estoque_menos_vendidos"
                or "estoque_mais_lucrativos" or "estoque_menos_lucrativos"
                or "estoque_negativo" or "estoque_minimo" or "estoque_validade"
                or "estoque_validade_lotes" or "estoque_controle_validades"
                or "estoque_consistencia_lotes"
                => p.RelatoriosAcesso,

            "usuarios" or "auditoria" => p.SistemaUsuarios,
            "backup" => p.SistemaBackup,
            "residuos_unificacoes" => CanAccessLegacyMergeCleanup(),

            // Configurações da loja: admin/usuários OU gestor (relatórios)
            "empresa" or "impressoras" or "perifericos"
                or "vendedores" or "formas_pagamento"
                => p.SistemaUsuarios || p.RelatoriosAcesso,

            _ => AppSession.IsAdmin,
        };
    }

    public static bool EnsureModule(string moduleId, Window? owner = null)
    {
        if (CanAccessModule(moduleId))
            return true;

        MessageBox.Show(
            owner,
            "Seu usuário não tem permissão para acessar este módulo.\nPeça ao administrador para liberar o acesso.",
            "Acesso negado",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    public static bool Ensure(string key, string actionLabel, Window? owner = null)
    {
        if (Can(key))
            return true;

        MessageBox.Show(
            owner,
            $"Seu usuário não tem permissão para: {actionLabel}.\nPeça ao administrador para liberar.",
            "Acesso negado",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    public static string DenyReason(string moduleId) =>
        CanAccessModule(moduleId) ? "" : "Sem permissão";

    private sealed class RemoteStoreRequestScope : IDisposable
    {
        private readonly StoreNetworkRemoteSession? _previous;
        private bool _disposed;

        public RemoteStoreRequestScope(StoreNetworkRemoteSession? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (RemoteStoreRequestDepth.Value > 0)
                RemoteStoreRequestDepth.Value--;
            RemoteSessionLocal.Value = RemoteStoreRequestDepth.Value == 0
                ? null
                : _previous;
        }
    }
}
