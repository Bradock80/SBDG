namespace SGDB.Services;

/// <summary>
/// 70B3A-B — mensagens e códigos do motor de cobertura validade/lote.
/// Regras ficam no serviço; a UI futura só exibe.
/// </summary>
public static class LotCoverageRules
{
    public const string PhysicalConferenceReason = "Conferência física";
    public const string OriginLegacyConference = "legacy_conference";

    public const string Entity = "cobertura_lote";

    public const string ActionAdd = "lot_coverage_add";
    public const string ActionEdit = "lot_coverage_edit";
    public const string ActionSplit = "lot_coverage_split";
    public const string ActionRemove = "lot_coverage_remove";
    public const string ActionQuantityCorrect = "lot_coverage_quantity_correct";

    public const string ProductNotFound = "ProductNotFound";
    public const string InactiveProduct = "InactiveProduct";
    public const string AbsorbedProduct = "AbsorbedProduct";
    public const string NegativeStock = "NegativeStock";
    public const string ZeroStock = "ZeroStock";
    public const string OverTracked = "OverTracked";
    public const string QuantityInvalid = "QuantityInvalid";
    public const string QuantityExceedsUntracked = "QuantityExceedsUntracked";
    public const string ReasonRequired = "ReasonRequired";
    public const string KeyCollision = "KeyCollision";
    public const string OpenInventory = "OpenInventory";
    public const string AccessDenied = "AccessDenied";
    public const string LotNotFound = "LotNotFound";
    public const string SplitInvalid = "SplitInvalid";
    public const string ExpiryRequired = "ExpiryRequired";
    public const string PurchaseOriginProtected = "PurchaseOriginProtected";

    public const string ProductNotFoundMessage = "Produto não encontrado.";

    public const string InactiveProductMessage =
        "Produto inativo não pode ter cobertura de validade/lote alterada.";

    public const string AbsorbedProductMessage =
        "Este cadastro foi unificado (absorb). Não altere a rastreabilidade dele; use o produto principal.";

    public const string NegativeStockMessage =
        "O estoque deste produto está negativo. Regularize o estoque antes de cadastrar ou corrigir sua rastreabilidade.";

    public const string ZeroStockMessage =
        "Não existe estoque físico disponível para rastrear.";

    public const string OverTrackedMessage =
        "Há mais quantidade rastreada do que estoque físico. Esta inconsistência não é corrigida automaticamente.";

    public const string QuantityInvalidMessage =
        "A quantidade de cobertura deve ser maior que zero.";

    public const string QuantityExceedsUntrackedMessage =
        "A quantidade supera o estoque ainda sem rastreamento. A cobertura não pode ficar acima do estoque físico.";

    public const string ReasonRequiredMessage =
        "Informe o motivo desta correção.";

    public const string KeyCollisionMessage =
        "Já existe outra cobertura com o mesmo produto, lote e validade. A mescla automática foi recusada para preservar referências (purchase_item_lots).";

    public const string OpenInventoryMessage =
        "Existe um inventário em aberto. Conclua ou cancele o inventário antes de alterar a cobertura de lotes.";

    public const string AccessDeniedMessage =
        "Seu usuário não tem permissão para alterar cobertura de validade/lote.";

    public const string LotNotFoundMessage =
        "Cobertura de lote não encontrada.";

    public const string SplitInvalidMessage =
        "A divisão deve deixar quantidade positiva na origem e no destino, com identidades diferentes.";

    public const string ExpiryRequiredMessage =
        "Informe a data de validade. Não se cadastra cobertura sem validade nesta manutenção.";

    public const string SplitSameIdentityMessage =
        "O destino da divisão precisa ter lote ou validade diferente da origem.";

    public const string PurchaseOriginRemoveMessage =
        "Esta cobertura veio de uma compra. Removê-la quebraria o vínculo com o histórico da compra (purchase_item_lots) e o cancelamento seguro. Ajuste pela compra ou corrija só lote/validade com motivo.";

    public const string PurchaseOriginSplitMessage =
        "Não é seguro dividir uma cobertura originada de compra neste modelo: a origem financeira ficaria ambígua e o cancelamento da compra pode falhar. Divida apenas coberturas manuais.";

    public const string PurchaseOriginQuantityMessage =
        "Não é seguro corrigir a quantidade de uma cobertura originada de compra neste modelo: o cancelamento da compra exige que a quantidade atual do lote cubra a origem registrada. Use cobertura manual para o excedente ou ajuste pela compra.";
}
