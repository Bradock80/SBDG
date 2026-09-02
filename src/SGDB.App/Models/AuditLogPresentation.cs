namespace SGDB.Models;

using SGDB.Services;

public sealed record AuditActionFilterOption(string Key, string Label)
{
    public static IReadOnlyList<AuditActionFilterOption> All { get; } =
    [
        new("", "Todas as ações"),
        new("cancel_venda", "🔴 Cancelamento de Venda"),
        new("desconto", "🟠 Desconto Concedido"),
        new("caixa_mov", "🟡 Sangria / Suprimento"),
        new("caixa_sessao", "🔵 Abertura / Fechamento de Caixa"),
        new("estoque_cadastro", "🟣 Estoque e Cadastros"),
        new("login_logout", "⚪ Login / Logout"),
    ];
}

public sealed record AuditUserFilterOption(string Login, string DisplayName)
{
    public static AuditUserFilterOption All { get; } = new("", "Todos os usuários");
}

public sealed record AuditActionBadge(string Label, string Kind);

public static class AuditLogPresentation
{
    private static readonly Dictionary<string, AuditActionBadge> BadgeByKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["login"] = new("Login", "info"),
        ["logout"] = new("Logout", "info"),
        ["venda"] = new("Venda", "info"),
        ["cancel_vd"] = new("Cancelamento", "danger"),
        ["desconto"] = new("Desconto", "warning"),
        ["sangria"] = new("Sangria", "warning"),
        ["suprimento"] = new("Suprimento", "success"),
        ["abrir_cx"] = new("Abertura de Caixa", "info"),
        ["fechar_cx"] = new("Fechamento de Caixa", "info"),
        ["cancel_item"] = new("Cancelamento de Item", "danger"),
        ["alterar_prod"] = new("Alteração de Produto", "warning"),
        ["entrada_compra"] = new("Entrada de Compra", "info"),
        ["criar_pessoa"] = new("Cadastro de Pessoa", "secondary"),
        ["alterar_pessoa"] = new("Alteração de Pessoa", "secondary"),
        ["remover"] = new("Remoção de Item", "danger"),
        ["salvar_emp"] = new("Salvar Empresa", "secondary"),
        ["alterar_politica"] = new("Política comercial", "warning"),
        ["salvar_imp"] = new("Salvar Impressora", "secondary"),
        ["salvar_per"] = new("Salvar Periféricos", "secondary"),
        ["criar_user"] = new("Criar Usuário", "secondary"),
        ["alterar_user"] = new("Alterar Usuário", "secondary"),
        ["desativar_user"] = new("Desativar Usuário", "warning"),
        ["backup"] = new("Backup", "warning"),
        ["restore"] = new("Restauração", "danger"),
    };

    public static AuditActionBadge GetActionBadge(string action, string entity)
    {
        var key = GetActionKey(action, entity);
        if (BadgeByKey.TryGetValue(key, out var badge))
            return badge;

        var fallback = string.IsNullOrEmpty(key) ? "Evento" : ToTitle(key.Replace('_', ' '));
        return new AuditActionBadge(fallback, "secondary");
    }

    public static string GetRiskLevel(string action, string entity) =>
        GetActionBadge(action, entity).Kind switch
        {
            "danger" => "high",
            "warning" => "sensitive",
            _ => "normal",
        };

    public static string GetActionKey(string action, string entity)
    {
        var a = Norm(action);
        var e = Norm(entity);

        return (a, e) switch
        {
            ("login", _) => "login",
            ("logout", _) => "logout",
            ("venda", "venda") => "venda",
            ("cancelar", "venda") => "cancel_vd",
            ("remover", "item") => "cancel_item",
            ("alterar", "produto") => "alterar_prod",
            ("entrada", "compra") => "entrada_compra",
            ("criar", "cliente" or "fornecedor" or "pessoa") => "criar_pessoa",
            ("alterar", "cliente" or "fornecedor" or "pessoa") => "alterar_pessoa",
            (_, _) when a.Contains("desconto") => "desconto",
            ("sangria", _) => "sangria",
            ("suprimento", _) => "suprimento",
            ("abrir", _) or ("abertura", _) => "abrir_cx",
            ("fechar", _) or ("fechamento", _) => "fechar_cx",
            ("salvar", "empresa") => "salvar_emp",
            ("alterar", "politica_comercial") => "alterar_politica",
            ("salvar", "impressora") => "salvar_imp",
            ("salvar", "perifericos") => "salvar_per",
            ("criar", "usuario") => "criar_user",
            ("alterar", "usuario") => "alterar_user",
            ("desativar", "usuario") => "desativar_user",
            ("backup", _) => "backup",
            ("restore", _) => "restore",
            _ => string.IsNullOrEmpty(a) ? "evento" : a,
        };
    }

    /// <summary>Rótulo legível exibido na badge (ex: "Cancelamento").</summary>
    public static string GetActionBadgeLabel(string action, string entity) =>
        GetActionBadge(action, entity).Label;

    /// <summary>Tipo visual da badge: info, danger, warning, success, secondary.</summary>
    public static string GetBadgeKind(string action, string entity) =>
        GetActionBadge(action, entity).Kind;

    public static string GetActionBadgeDisplay(string action, string entity)
    {
        var key = GetActionKey(action, entity);
        var kind = GetBadgeKind(action, entity);
        var emoji = kind switch
        {
            "danger" => "🔴",
            "warning" => "🟠",
            "success" => "🟢",
            "info" => "🔵",
            _ => "⚪",
        };
        return $"[ {emoji} {key} ]";
    }

    public static string GetEntityDisplay(string entity)
    {
        return Norm(entity) switch
        {
            "sessao" => "Sessão",
            "venda" => "Venda",
            "item" => "Item",
            "usuario" => "Usuário",
            "empresa" => "Empresa",
            "politica_comercial" => "Política comercial",
            "impressora" => "Impressora",
            "perifericos" => "Periféricos",
            "database" => "Banco de Dados",
            "caixa" => "Caixa",
            "" => "—",
            var e => ToTitle(e),
        };
    }

    public static string GetActionBadgeText(string action, string entity) =>
        GetActionBadgeDisplay(action, entity);

    public static string GetDetailsDisplay(AuditLogRow row)
    {
        var summary = AuditPayloadBuilder.GetSummary(row.Details);
        if (!string.IsNullOrWhiteSpace(summary))
            return summary;

        var a = Norm(row.Action);
        var e = Norm(row.Entity);
        var details = (row.Details ?? "").Trim();
        var id = row.EntityId?.Trim();

        if (a is "login")
            return "Login efetuado com sucesso";
        if (a is "logout")
            return $"Logout: {row.UserName}";

        if (a is "cancelar" && e == "venda")
        {
            if (details.Contains("R$", StringComparison.Ordinal))
            {
                if (details.Contains("cancelada", StringComparison.OrdinalIgnoreCase))
                    return details.StartsWith("Venda", StringComparison.OrdinalIgnoreCase)
                        ? details
                        : $"Venda {details.TrimStart()}";
                var money = ExtractMoney(details);
                return money is not null
                    ? $"Venda de R$ {money} cancelada"
                    : details;
            }
            return id is not null ? $"Venda #{id} cancelada" : "Venda cancelada";
        }

        if (a is "venda" && e == "venda")
        {
            if (details.Contains("R$", StringComparison.Ordinal))
                return id is not null ? $"Venda #{id} · {details}" : details;
            return id is not null ? $"Venda #{id} registrada · {details}" : details;
        }

        if (a is "remover" && e == "item")
            return string.IsNullOrEmpty(details) ? "Item removido do PDV" : details;

        if (a is "alterar" && e == "produto")
            return string.IsNullOrEmpty(details) ? "Produto alterado" : details;

        if (a is "entrada" && e == "compra")
            return string.IsNullOrEmpty(details) ? "Entrada de compra registrada" : details;

        if (a is "criar" && e is "cliente" or "fornecedor" or "pessoa")
            return string.IsNullOrEmpty(details) ? "Cadastro de pessoa" : details;

        if (a is "alterar" && e is "cliente" or "fornecedor" or "pessoa")
            return string.IsNullOrEmpty(details) ? "Alteração de pessoa" : details;

        if (a is "desconto" || details.Contains("desconto", StringComparison.OrdinalIgnoreCase))
        {
            if (details.Contains('%'))
                return details;
            return string.IsNullOrEmpty(details) ? "Desconto aplicado" : details;
        }

        if (a is "sangria")
            return string.IsNullOrEmpty(details) ? "Sangria de caixa registrada" : details;
        if (a is "suprimento")
            return string.IsNullOrEmpty(details) ? "Suprimento de caixa registrado" : details;
        if (a is "abrir" or "abertura")
            return string.IsNullOrEmpty(details) ? "Caixa aberto" : details;
        if (a is "fechar" or "fechamento")
            return string.IsNullOrEmpty(details) ? "Caixa fechado" : details;

        if (a is "salvar")
        {
            var what = e switch
            {
                "empresa" => "Dados da empresa",
                "impressora" => "Configuração de impressora",
                "perifericos" => "Periféricos",
                _ => e,
            };
            return string.IsNullOrEmpty(details) ? $"{what} salvo(a)" : $"{what}: {details}";
        }

        if (a is "criar" && e == "usuario")
            return string.IsNullOrEmpty(details) ? "Usuário criado" : $"Usuário criado · {details}";
        if (a is "alterar" && e == "usuario")
            return string.IsNullOrEmpty(details) ? "Usuário alterado" : $"Usuário alterado · {details}";
        if (a is "desativar" && e == "usuario")
            return id is not null ? $"Usuário #{id} desativado" : "Usuário desativado";

        if (a is "backup")
            return string.IsNullOrEmpty(details) ? "Backup do banco de dados" : details;
        if (a is "restore")
            return string.IsNullOrEmpty(details) ? "Restauração do banco de dados" : details;

        if (!string.IsNullOrEmpty(details))
            return details;
        if (!string.IsNullOrEmpty(id))
            return $"{row.Entity} #{id}";
        return row.UserLogin;
    }

    public static string BuildDetailBody(AuditLogRow row)
    {
        return $"""
               ID do registro: {row.Id}
               Data/Hora: {row.DateDisplay}
               Usuário: {row.UserName} ({row.UserLogin})
               Ação: {row.Action}
               Entidade: {GetEntityDisplay(row.Entity)} ({row.Entity})
               ID entidade: {row.EntityId ?? "—"}
               Nível: {GetActionBadge(row.Action, row.Entity).Kind} ({GetActionBadgeLabel(row.Action, row.Entity)})

               Detalhes:
               {GetDetailsDisplay(row)}

               Dados brutos (detalhes):
               {(string.IsNullOrWhiteSpace(row.Details) ? "—" : row.Details)}
               """;
    }

    private static string? ExtractMoney(string text)
    {
        var idx = text.IndexOf("R$", StringComparison.Ordinal);
        if (idx < 0) return null;
        var slice = text[(idx + 2)..].Trim();
        var end = 0;
        while (end < slice.Length && (char.IsDigit(slice[end]) || slice[end] is '.' or ','))
            end++;
        var token = slice[..end].Trim();
        return string.IsNullOrEmpty(token) ? null : token;
    }

    private static string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant();

    private static string ToTitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "Evento";
        return char.ToUpper(text[0]) + text[1..];
    }
}
