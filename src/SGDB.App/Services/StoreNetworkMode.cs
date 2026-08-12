namespace SGDB.Services;

/// <summary>
/// Papel deste PC na rede da loja: standalone (padrão), servidor ou cliente.
/// </summary>
public static class StoreNetworkMode
{
    public const string SettingRole = "store_network_role";
    public const string SettingPin = "store_network_pin";
    public const string SettingPort = "store_network_port";
    public const string SettingHost = "store_network_host";
    public const string SettingClientPin = "store_network_client_pin";
    public const int DefaultPort = 5055;

    public const string RoleStandalone = "standalone";
    public const string RoleServer = "server";
    public const string RoleClient = "client";

    public static string GetRole()
    {
        var r = (AppSettingsService.GetSetting(SettingRole) ?? "").Trim().ToLowerInvariant();
        return r is RoleServer or RoleClient ? r : RoleStandalone;
    }

    public static bool IsClient => GetRole() == RoleClient;
    public static bool IsServer => GetRole() == RoleServer;

    public static void SetRole(string role)
    {
        role = (role ?? "").Trim().ToLowerInvariant();
        if (role is not (RoleServer or RoleClient or RoleStandalone))
            role = RoleStandalone;
        AppSettingsService.SetSetting(SettingRole, role);
    }

    public static string EnsurePin()
    {
        var pin = AppSettingsService.GetSetting(SettingPin);
        if (string.IsNullOrWhiteSpace(pin) || pin.Length < 4)
        {
            pin = Random.Shared.Next(1000, 9999).ToString();
            AppSettingsService.SetSetting(SettingPin, pin);
        }
        return pin.Trim();
    }

    public static void SavePin(string pin)
    {
        pin = (pin ?? "").Trim();
        if (pin.Length < 4 || pin.Length > 8 || !pin.All(char.IsDigit))
            throw new InvalidOperationException("PIN deve ter 4 a 8 dígitos.");
        AppSettingsService.SetSetting(SettingPin, pin);
        // Se o servidor já estiver ligado, atualiza o PIN em memória na hora
        var host = StoreNetworkHost.Current;
        if (host is not null)
            host.Pin = pin;
    }

    /// <summary>PIN atual do servidor (sempre do banco, já trimado).</summary>
    public static string GetServerPin() => (AppSettingsService.GetSetting(SettingPin) ?? EnsurePin()).Trim();


    public static int GetPort()
    {
        var raw = AppSettingsService.GetSetting(SettingPort);
        if (int.TryParse(raw, out var p) && p is >= 1024 and <= 65535)
            return p;
        return DefaultPort;
    }

    public static void SavePort(int port)
    {
        if (port is < 1024 or > 65535)
            throw new InvalidOperationException("Porta inválida (1024–65535).");
        AppSettingsService.SetSetting(SettingPort, port.ToString());
    }

    public static string GetClientHost() =>
        (AppSettingsService.GetSetting(SettingHost) ?? "").Trim();

    public static string GetClientPin() =>
        (AppSettingsService.GetSetting(SettingClientPin) ?? EnsurePin()).Trim();

    public static (string Host, int Port) NormalizeHostPort(string hostOrUrl, int fallbackPort)
    {
        var host = (hostOrUrl ?? "").Trim();
        var port = fallbackPort is >= 1024 and <= 65535 ? fallbackPort : DefaultPort;
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("Informe o IP do PC da loja.");

        host = host.Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');
        var slash = host.IndexOf('/');
        if (slash > 0) host = host[..slash];

        var colon = host.LastIndexOf(':');
        if (colon > 0 && host.Count(c => c == '.') >= 3
            && int.TryParse(host[(colon + 1)..], out var hostPort)
            && hostPort is >= 1024 and <= 65535)
        {
            port = hostPort;
            host = host[..colon].Trim();
        }

        if (!LooksLikeLanIp(host))
            throw new InvalidOperationException(
                "IP inválido.\n\n" +
                "No campo IP digite SOMENTE o endereço, sem porta, por exemplo:\n" +
                "192.168.18.138\n\n" +
                "A porta fica no campo Porta (5055).\n" +
                "O PIN fica no campo PIN.\n\n" +
                "Pegue o IP na loja: Rede Loja → Servidor → Ligar (aparece na tela).");

        return (host, port);
    }

    public static void SaveClient(string host, string pin, int port)
    {
        var norm = NormalizeHostPort(host, port);
        host = norm.Host;
        port = norm.Port;

        SavePin(pin);
        SavePort(port);
        AppSettingsService.SetSetting(SettingHost, host);
        AppSettingsService.SetSetting(SettingClientPin, pin.Trim());
        SetRole(RoleClient);
    }

    private static bool LooksLikeLanIp(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        // Aceita hostname local simples, mas exige formato de IP com 3 pontos
        var parts = host.Split('.');
        if (parts.Length != 4) return false;
        foreach (var p in parts)
        {
            if (!int.TryParse(p, out var n) || n is < 0 or > 255)
                return false;
        }
        // Evita 0.0.x.x / números soltos interpretados como IP
        if (parts[0] == "0" && parts[1] == "0") return false;
        return true;
    }

    public static void EnsureClientConfigured()
    {
        if (!IsClient)
            throw new InvalidOperationException("Este PC não está em modo Cliente.");
        if (string.IsNullOrWhiteSpace(GetClientHost()))
            throw new InvalidOperationException("Configure o IP do servidor em Sistema → Rede Loja.");
    }

    public static string ClientBaseUrl
    {
        get
        {
            EnsureClientConfigured();
            return $"http://{GetClientHost()}:{GetPort()}";
        }
    }

    /// <summary>
    /// Operações comerciais locais (venda, caixa, fiado, decks, etc.) só no servidor/standalone.
    /// </summary>
    public static void EnsureLocalMutationAllowed(string? operation = null)
    {
        if (!IsClient)
            return;
        throw new StoreNetworkClientBlockedException(operation);
    }

    /// <summary>
    /// Módulos bloqueados no notebook cliente (usam SQLite local ou induzem erro).
    /// Permitidos via rede: produtos, compras, estoque (ajuste/relatórios StockService),
    /// clientes, pagar, vasilhame, movimentação, início/dashboard, PDV resumo.
    /// </summary>
    public static bool IsModuleBlockedOnClient(string moduleId) =>
        IsClient && moduleId switch
        {
            "devolucao_venda"
                or "caixa"
                or "depositos_caixa"
                or "fiado"
                or "decks"
                or "estoque_inventario"
                or "contas_bancarias"
                or "relatorio"
                or "relatorio_vendas"
                or "relatorio_mais_vendidos"
                or "relatorio_dre"
                or "relatorio_estoque_io"
                or "relatorio_fiado"
                or "relatorio_vendedores"
                or "formas_pagamento"
                or "vendedores"
                or "categorias_financeiras"
                or "tabela_preco"
                or "estoque_grupos"
                or "estoque_unidades"
                or "estoque_marcas"
                or "estoque_validade_lotes"
                or "auditoria"
                => true,
            _ => false,
        };

    public static string ClientBlockedModuleMessage =>
        "Este módulo está disponível apenas no computador servidor da loja.\n\n" +
        "No notebook (cliente) você pode: Produtos, Compras, Estoque (via rede), Clientes, " +
        "Contas a Pagar, Vasilhame, Movimentação, Meu Negócio e Resumo do PDV.";

    /// <summary>Cliente não abre o PDV de venda — só o resumo do dia.</summary>
    public static bool IsPdvSalesBlockedOnClient => IsClient;
}

/// <summary>Operação comercial local recusada no notebook cliente da Rede Loja.</summary>
public sealed class StoreNetworkClientBlockedException : InvalidOperationException
{
    public StoreNetworkClientBlockedException(string? operation = null)
        : base(string.IsNullOrWhiteSpace(operation)
            ? "Esta operação não está disponível no computador cliente da Rede Loja. Execute no computador servidor."
            : $"Esta operação ({operation}) não está disponível no computador cliente da Rede Loja. Execute no computador servidor.")
    {
    }
}
