namespace SGDB.Services;

/// <summary>
/// ETAPA 69T-F — saneamento administrativo só no host/standalone.
/// Não há RPC: protocolo antigo não consegue chamar em silêncio.
/// </summary>
public static class LegacyMergeCleanupRules
{
    public const string AtomicFeature = "legacy_merge_cleanup_v1";

    public const string ModuleId = "residuos_unificacoes";

    public const string ClientBlockedMessage =
        "O saneamento de resíduos de unificações antigas só pode ser feito no computador servidor da loja (este PC).\n\n" +
        "No notebook (cliente) esta operação está bloqueada.";

    public const string HostNeedsUpgradeMessage =
        "O PC da loja precisa ser atualizado antes de sanear resíduos de unificações antigas.";

    public const string AccessDeniedMessage =
        "Seu usuário não tem permissão para resíduos de unificações antigas.";

    public const string BackupRequiredMessage =
        "É obrigatório fazer um backup válido desta execução antes de sanear.";

    public const string BackupConsistentFailedMessage =
        "Não foi possível criar um backup consistente (VACUUM INTO). O saneamento permanece bloqueado.";

    public const string NoTransferMessage =
        "O saneamento NÃO transfere estoque novamente.\nEle apenas zera o saldo residual do cadastro antigo.";

    public const string InventoryWarningMessage =
        "Alguns produtos principais possuem histórico de ajustes manuais.\n" +
        "Recomenda-se conferência física antes de considerar o estoque validado.";

    public static readonly string[] PhysicalInventoryPriorityNames =
    [
        "Original 300",
        "Brahma 300",
        "Rothmans Blue",
        "Coca-Cola 350",
        "Coca Cola 350",
    ];

    public static bool SupportsCleanup(IEnumerable<string>? features) =>
        features is not null
        && features.Any(f => string.Equals(f, AtomicFeature, StringComparison.OrdinalIgnoreCase));

    public static bool MatchesPhysicalInventoryPriority(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        return PhysicalInventoryPriorityNames.Any(p =>
            name.Contains(p, StringComparison.OrdinalIgnoreCase));
    }
}
