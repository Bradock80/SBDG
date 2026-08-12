using System.Text.RegularExpressions;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Utils;

/// <summary>
/// Infere marca e grupo a partir do nome do produto (NF-e / cadastro).
/// Marcas e grupos encontrados são gravados no catálogo via ProductService.
/// </summary>
public static class ProductClassificationHelper
{
    public readonly record struct Classification(string? Brand, string? Group);

    /// <summary>
    /// Remove siglas e padrões de embalagem/caixa no final do nome (NF-e).
    /// Mantém o nome comercial, tamanho (473ML, 1,5L) e atributos (COM GAS, PET, GFA VD).
    /// </summary>
    public static string SanitizeProductName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var cleaned = Regex.Replace(name.Trim(), @"\s+", " ");
        // "QTD. 15.00 UN" / "QTD 15 UN" no meio ou no fim
        cleaned = Regex.Replace(
            cleaned,
            @"\s+QTD\.?\s*[\d.,]+\s*(?:UN|UND|UNID)?\b",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        var original = cleaned;

        // Embalagem / display / fardo (não incluir PET, GFA, LATA — fazem parte do produto)
        const string PackWords =
            @"DP|BOX|CX|FD|PCT|PCTE|DSP|DES|DISPLAY|CARTELA|FARDO|PACK|PACOTE|EMB|SC|SACOLA|SCH|PT|PTA|FDO|MASTER|CAIXA|CXA|SH|SHRINK|NPAL|PAL|PALLET|PACKING|CARTAO|CARTÃO|PAPELAO|PAPELÃO|SIX|SIXP|SIXPACK";

        // Códigos de planta / canal / fábrica no fim (após qtd)
        const string TrailingCodes =
            @"JD|FL|CX|FD|DP|UN|U|BR|PBR|TTC|CP|SP|RJ|MG|PR|RS|BA|PE|CE|DF|GO|ES|MT|MS|AM|PA|MAINLINE|MAIN|UNIV|ROT|LS|LSN|RET|GB|NL|ARF|ARF1|NPAL|PAL|SH";

        string previous;
        do
        {
            previous = cleaned;

            // Display Nestlé/Garoto: 12(12X24G) BR | 18(30X32G) BR
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]*\d+\s*\(\s*\d+\s*[xX×]\s*\d+[A-Za-z]*\s*\)\s*(?:BR|BRA|BRASIL)?\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // SH C/12 NPAL | SH C/12 | SHRINK C/24
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]*(?:SH|SHRINK|NPAL|PAL|PALLET)\s*C?\s*/?\s*\d+[A-Za-z0-9]*(?:\s+(?:NPAL|PAL|PALLET|SH|BR|JD|FL|CP))?\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // —— Cigarro / tabaco (maço): SC HW25 MACO | BOX HW25 | MACO HW25 | HW25 ——
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]+(?:BOX|SC|SW|KS)?\s*HW\d{2}(?:\s*(?:MACO|MAÇO|MACOS|MAÇOS))?\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]+(?:MACO|MAÇO|MACOS|MAÇOS)\s*HW\d{2}\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]+(?:SC|SW|KS)\s*(?:HW\d{2})?(?:\s*(?:MACO|MAÇO|MACOS|MAÇOS))?\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]+(?:MACO|MAÇO|MACOS|MAÇOS)\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]+HW\d{2}\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // 6PACK FL CP | 10PACK | 4 PACK
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]+\d+\s*PACK(?:\s+[A-Za-z]{1,10}){0,4}\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // Observação ONU / isqueiro no fim: (ONU1057-ISQUEIRO GAS)
            cleaned = Regex.Replace(
                cleaned,
                @"\s*\(\s*ONU\d+[^)]*\)\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // CX C/23 | CX C/ 23 | CX/23 | C/12 no fim
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]*CX\s*C?\s*/\s*\d+[A-Za-z0-9]*\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // CX10 350G | CX12 180G | BOX 10 1KG — caixa + peso/volume TOTAL da embalagem
            // (mantém o tamanho unitário já no nome, ex.: 35G / 15G / 269ML)
            cleaned = Regex.Replace(
                cleaned,
                $@"[\s\-_/]*(?:{PackWords})[\s\-./]*\d+(?:\s*[xX×]\s*\d+)?(?:\s+\d+[.,]?\d*\s*(?:ML|L|G|GR|KG))?[A-Za-z0-9./]*\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // DES 12UNPBR | DP16X29G | BOX 10X1 | CX12 | CX 24UN | FD 10
            cleaned = Regex.Replace(
                cleaned,
                $@"[\s\-_/]*(?:{PackWords})[\s\-./]*\d+(?:\s*[xX×]\s*\d+)?[A-Za-z0-9./]*\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // SIX | SIX PACK | PCT6 no fim (six-pack de bebida)
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]+(?:SIX(?:\s*PACK)?|SIXP|PACK\s*6|PCT\s*C?\s*/?\s*6)\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // C/12 | C/ 24 UN (sem CX na frente)
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]*C/\s*\d+[A-Za-z0-9./]*\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // 12U MAINLINE | 06UN CP | 12UN JD T | 12UNPBR | 12 U FL
            cleaned = Regex.Replace(
                cleaned,
                $@"[\s\-_/]+\d+\s*(?:UN|UND|UNID|U)\s*[A-Za-z0-9]*(?:\s+(?:{TrailingCodes}|[A-Za-z0-9]{{1,20}})){{0,5}}\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // Só código de planta/fardo no fim: "9 JD" | "12 FL" | "06 CP"
            cleaned = Regex.Replace(
                cleaned,
                $@"[\s\-_/]+\d{{1,3}}\s+(?:{TrailingCodes})\b(?:\s+(?:{TrailingCodes}|[A-Za-z]{{1,12}})){{0,3}}\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // Dimensão com volume: 6X330ML | 12X473ML | 1X500ML → mantém só a unidade (330ML)
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]+\d+\s*[xX×]\s*(\d+[.,]?\d*\s*(?:ML|L|G|GR|KG))\b\s*$",
                " $1",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            // Colado no nome: HELE1X40GR → mantém 40GR
            cleaned = Regex.Replace(
                cleaned,
                @"(?<=[A-Za-zÀ-ÿ])\d+\s*[xX×]\s*(\d+[.,]?\d*\s*(?:ML|L|G|GR|KG))\b\s*$",
                " $1",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            // Multiplicador sem volume (só contagem de fardo): 10X1 | 16X29
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]+\d+\s*[xX×]\s*\d+\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            // Só o multiplicador solto com medida: X40GR | X 330ML (mantém a medida)
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]*[xX×]\s*(\d+[.,]?\d*\s*(?:ML|L|G|GR|KG))\b\s*$",
                " $1",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // COM TTC | COM TAMPA (não remove COM GAS / SEM GAS)
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]+COM\s+(?:TTC|TAMPA|ROSC|ROSCAS?|LACRE)\b[A-Za-z0-9\s]*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // CX CARTAO | CAIXA CARTÃO | CX PAPELAO | EMB CARTAO (material da caixa, não do produto)
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]+(?:CX|CXA|CAIXA|EMB|PACK|FARDO|FD|BOX)?\s*(?:DE\s+)?(?:CART[AÃ]O|PAPEL[AÃ]O|PL[AÁ]STICO|PLASTICO)\b[\s\-_/A-Za-z0-9]*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // Palavra de embalagem sozinha no fim: BOX, CX, DISPLAY, CARTAO…
            cleaned = Regex.Replace(
                cleaned,
                $@"[\s\-_/]+(?:{PackWords})\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // Código de canal sozinho no fim (após já ter tirado a qtd)
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]+(?:MAINLINE|MAIN|PBR|TTC|ARF\d*|NPAL|PAL|CAIXA|CXA)\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // Quantidade sobrando no fim (ex.: "965ML 12,9000" / "12.0000")
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]+\d{1,4}[.,]\d{3,4}\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // UN / qtd+UN soltos no fim
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]+\d+\s*(?:UN|UND|UNID)\.?\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            cleaned = Regex.Replace(
                cleaned,
                @"[\s\-_/]+(?:UN|UND|UNID)\.?\s*$",
                "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            cleaned = cleaned.Trim().TrimEnd('-', '/', '_', '.', ',', ' ');
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        }
        while (!string.Equals(previous, cleaned, StringComparison.Ordinal));

        cleaned = ExpandCommercialAbbreviations(cleaned);
        cleaned = FixKnownTruncatedBrands(cleaned);

        return string.IsNullOrWhiteSpace(cleaned) ? original : cleaned;
    }

    /// <summary>
    /// Troca abreviações típicas da xProd (Ambev etc.) por termos comerciais.
    /// LT só vira LATA quando é lata (ex.: LT 473ML); "1 LT" de pote/sorvete vira "1L".
    /// </summary>
    private static string ExpandCommercialAbbreviations(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        var cleaned = name;

        // Litro (pote/galão): "1 LT" / "2LT" quando NÃO seguido de mililitros
        cleaned = Regex.Replace(
            cleaned,
            @"\b(\d+[.,]?\d*)\s*LT\b(?!\s*\d+\s*ML\b)",
            "$1L",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Lata de bebida: LT 473ML / LT350ML
        cleaned = Regex.Replace(
            cleaned,
            @"\bLT\b(?=\s*\d+\s*ML\b)",
            "LATA",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Long neck (com ou sem volume colado): LN 330ML | LN330ML | LN C/6
        cleaned = Regex.Replace(
            cleaned,
            @"\bLN(?=\d)",
            "LONG NECK ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(
            cleaned,
            @"\bLN\b",
            "LONG NECK",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Garrafa de vidro (GFA VD / GF VD / GFA VIDRO)
        cleaned = Regex.Replace(
            cleaned,
            @"\bGF(?:A)?\s*VD\b",
            "GARRAFA VIDRO",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(
            cleaned,
            @"\bGF(?:A)?\s+VIDRO\b",
            "GARRAFA VIDRO",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(
            cleaned,
            @"\bGFA\b",
            "GARRAFA",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Letra isolada de linha/fábrica entre marca e embalagem: "SPATEN N LATA" → "SPATEN LATA"
        cleaned = Regex.Replace(
            cleaned,
            @"\b([A-ZÀ-Ÿ]{3,})\s+[A-Z]\s+(?=LATA|LONG NECK|GARRAFA)\b",
            "$1 ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }

    /// <summary>Corrige marcas truncadas comuns na xProd da NF (limite de caracteres).</summary>
    private static string FixKnownTruncatedBrands(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        // SANTA HEL / HELE / HELEN → SANTA HELENA (depois de tirar 1X40GR)
        name = Regex.Replace(
            name,
            @"\bSANTA\s+HELENA?\b",
            "SANTA HELENA",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        name = Regex.Replace(
            name,
            @"\bSANTA\s+HELE?\b",
            "SANTA HELENA",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return Regex.Replace(name, @"\s+", " ").Trim();
    }

    /// <summary>Aliases de marca → nome canônico (mais longos primeiro).</summary>
    private static readonly (string Needle, string Brand)[] BrandRules =
    [
        ("COCA COLA", "COCA COLA"),
        ("COCA-COLA", "COCA COLA"),
        ("GUARANA MANTIQUEIRA", "MANTIQUEIRA"),
        ("GUARANÁ MANTIQUEIRA", "MANTIQUEIRA"),
        ("MANTIQUEIRA", "MANTIQUEIRA"),
        ("GUARANA ANTARCTICA", "ANTARCTICA"),
        ("GUARANA ANTARTICA", "ANTARCTICA"),
        ("ANTARCTICA", "ANTARCTICA"),
        ("ANTARTICA", "ANTARCTICA"),
        ("KIT KAT", "KIT KAT"),
        ("KITKAT", "KIT KAT"),
        ("PRESTIGIO", "PRESTIGIO"),
        ("PRESTÍGIO", "PRESTIGIO"),
        ("CHOKITO", "CHOKITO"),
        ("SENSACAO", "SENSACAO"),
        ("SENSAÇÃO", "SENSACAO"),
        ("CHARGE", "CHARGE"),
        ("GALAK", "GALAK"),
        ("ALPINO", "ALPINO"),
        ("SURPRESINHA", "SURPRESINHA"),
        ("MOCOTO", "MOCOTO"),
        ("BATON", "BATON"),
        ("BTN ", "BATON"),
        ("CHOCOSTICK", "BATON"),
        ("RED BULL", "RED BULL"),
        ("REDBULL", "RED BULL"),
        ("RED HOT", "RED HOT"),
        ("MONSTER ENERGY", "MONSTER"),
        ("MONSTER", "MONSTER"),
        ("TNT ENERGY", "TNT"),
        ("FUSION ENERGY", "FUSION"),
        ("PEPSI", "PEPSI"),
        ("FANTA", "FANTA"),
        ("SPRITE", "SPRITE"),
        ("KUAT", "KUAT"),
        ("SCHIN", "SCHIN"),
        ("SCHWEPPES", "SCHWEPPES"),
        ("H2OH", "H2OH"),
        ("SUKITA", "SUKITA"),
        ("BRAHMA", "BRAHMA"),
        ("SKOL", "SKOL"),
        ("HEINEKEN", "HEINEKEN"),
        ("BUDWEISER", "BUDWEISER"),
        ("CORONA", "CORONA"),
        ("STELLA ARTOIS", "STELLA ARTOIS"),
        ("EISENBAHN", "EISENBAHN"),
        ("SPATEN", "SPATEN"),
        ("CRYSTAL", "CRYSTAL"),
        ("BONAFONT", "BONAFONT"),
        ("LINDOYA", "LINDOYA"),
        ("SAO LOURENCO", "SAO LOURENCO"),
        ("DEL VALLE", "DEL VALLE"),
        ("MAGUARY", "MAGUARY"),
        ("TANG", "TANG"),
        ("NESTLE", "NESTLE"),
        ("NESTLÉ", "NESTLE"),
        ("LACTA", "LACTA"),
        ("GAROTO", "GAROTO"),
        ("TRENTO", "TRENTO"),
        ("PECORINO", "PECORINO"),
        ("BIS ", "LACTA"),
        ("BAUDUCCO", "BAUDUCCO"),
        ("MARILAN", "MARILAN"),
        ("PIRAQUE", "PIRAQUE"),
        ("TRIDENT", "TRIDENT"),
        ("HALLS", "HALLS"),
        ("MENTOS", "MENTOS"),
        ("FRUITTELLA", "FRUITTELLA"),
        ("FINI", "FINI"),
        ("DORITOS", "DORITOS"),
        ("CHEETOS", "CHEETOS"),
        ("RUFFLES", "RUFFLES"),
        ("FANDANGOS", "FANDANGOS"),
        ("TORCIDA", "TORCIDA"),
        ("LAYS", "LAYS"),
        ("BACANA", "BACANA"),
        ("DORI ", "DORI"),
        ("CROKISSIMO", "DORI"),
        ("MARLBORO", "MARLBORO"),
        ("HOLLYWOOD", "HOLLYWOOD"),
        ("DERBY", "DERBY"),
        ("LUCKY STRIKE", "LUCKY STRIKE"),
        ("ROTHMANS", "ROTHMANS"),
        ("CARLTON", "CARLTON"),
        ("PILAO", "PILAO"),
        ("3 CORACOES", "3 CORACOES"),
        ("TRES CORACOES", "3 CORACOES"),
        ("NESCAFE", "NESCAFE"),
        ("TODDYNHO", "TODDYNHO"),
        ("NINHO", "NINHO"),
        ("ITALAC", "ITALAC"),
        ("PARMALAT", "PARMALAT"),
        ("DANONE", "DANONE"),
        ("YPIOCA", "YPIOCA"),
        ("SMIRNOFF", "SMIRNOFF"),
        ("ABSOLUT", "ABSOLUT"),
        ("JACK DANIELS", "JACK DANIELS"),
        ("COCA", "COCA COLA"),
    ];

    private static readonly HashSet<string> NonBrandWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "CHOCOLATE", "CHOCO", "WAFER", "BISCOITO", "COOKIE", "BOMBOM", "BALA", "CHICLETE",
        "REFRIGERANTE", "CERVEJA", "AGUA", "SUCO", "LEITE", "CAFE", "CAFÉ", "FARDO", "DISPLAY",
        "PACK", "CX", "FD", "PET", "LITROS", "LITRO", "UN", "UNI", "BR", "DARK", "AO", "DE",
        "DA", "DO", "COM", "SEM", "PARA", "THE", "AND", "FNGR", "FINGER", "FINGERS", "STICK",
        "CARIBE", "LEITE", "BRANCO", "AMARGO", "MEIO", "AMARGO", "DESCARTAVEL", "DESCARTÁVEL",
        "EXB", "DP", "LT", "MIL", "PET", "GARRAFA", "LATA", "PACK", "BOX",
    };

    /// <summary>Palavras-chave de grupo (mais específicos primeiro).</summary>
    private static readonly (string[] Needles, string Group)[] GroupRules =
    [
        (["ENERGETICO", "ENERGY DRINK", "RED BULL", "REDBULL", "RED HOT", "MONSTER", "TNT ENERGY", "FUSION"], "ENERGETICO"),
        (["CERVEJA", "BRAHMA", "SKOL", "HEINEKEN", "BUDWEISER", "CORONA", "STELLA", "EISENBAHN", "SPATEN", "LONG NECK"], "CERVEJA"),
        (["CIGARRO", "CIGARR", "MARLBORO", "HOLLYWOOD", "DERBY", "LUCKY", "ROTHMANS", "CARLTON"], "CIGARRO"),
        (["VODKA", "WHISKY", "WHISKEY", "CACHACA", "CACHAÇA", "RUN ", "GIN ", "LICOR", "DESTILADO"], "DESTILADO"),
        (["VINHO", "ESPUMANTE", "CHAMPANHE"], "VINHO"),
        (["REFRIGERANTE", "COCA COLA", "COCA-COLA", "GUARANA", "GUARANÁ", "FANTA", "SPRITE", "PEPSI", "KUAT", "SCHIN", "SCHWEPPES", "H2OH", "SUKITA", "TONICA", "TÔNICA", "SODA", "MANTIQUEIRA"], "REFRIGERANTE"),
        (["AGUA MINERAL", "ÁGUA MINERAL", "AGUA C/GAS", "AGUA S/GAS", "BONAFONT", "CRYSTAL", "LINDOYA"], "AGUA"),
        (["SUCO", "NECTAR", "NÉCTAR", "DEL VALLE", "MAGUARY"], "SUCO"),
        (["CHOCOLATE", "CHOCO", "TRENTO", "BOMBOM", "WAFER", "BIS CX", "LACTA", "GAROTO", "KIT KAT", "KITKAT", "PRESTIGIO", "CHOKITO", "ALPINO", "CHARGE", "BATON", "SENSACAO"], "CHOCOLATE"),
        (["BISCOITO", "COOKIE", "BAUDUCCO", "PASSATEMPO", "NEGRESCO", "OREO", "TORRADA"], "BISCOITO"),
        (["SALGADINHO", "CHIPS", "DORITOS", "RUFFLES", "FANDANGOS", "CHEETOS", "CROKISSIMO", "TORCIDA", "BATATA PALHA"], "SALGADINHO"),
        (["BALA ", "CHICLETE", "GOMA DE MASCAR", "TRIDENT", "HALLS", "MENTOS", "FRUITTELLA", "FINI", "DROPS"], "BALA"),
        (["LEITE", "IOGURTE", "REQUEIJAO", "REQUEIJÃO", "CREME DE LEITE", "NINHO", "ITALAC", "PARMALAT", "DANONE", "TODDYNHO"], "LATICINIOS"),
        (["CAFE", "CAFÉ", "CAPPUCCINO", "PILAO", "3 CORACOES", "NESCAFE"], "CAFE"),
        (["SABONETE", "SHAMPOO", "CONDICIONADOR", "CREME DENTAL", "PAPEL HIGIENICO", "PAPEL HIGIÊNICO", "ABSORVENTE", "FRIALDA", "FRALDA"], "HIGIENE"),
        (["DETERGENTE", "ALVEJANTE", "DESINFETANTE", "AMACIANTE", "SABAO EM PO", "SABÃO EM PÓ", "LIMPA VIDRO"], "LIMPEZA"),
        (["GAS 13", "GAS 45", "GLP", "BOTIJA"], "GAS"),
        (["GELO"], "GELO"),
        (["PAO ", "PÃO ", "SONHO", "BOLO"], "PADARIA"),
        (["CARNE", "FRANGO", "LINGUICA", "LINGUIÇA", "SALSICHA", "MORTADELA", "PRESUNTO"], "FRIOS"),
    ];

    public static Classification Infer(string? productName)
    {
        var name = TextNorm.UpperStr(productName) ?? "";
        if (name.Length == 0)
            return default;

        return new Classification(InferBrand(name), InferGroup(name));
    }

    /// <summary>Preenche marca/grupo vazios a partir do nome (não sobrescreve o que já existe).</summary>
    public static void FillMissing(string? productName, ref string? groupName, ProductExtra extra)
    {
        var inferred = Infer(productName);
        if (string.IsNullOrWhiteSpace(groupName) && !string.IsNullOrWhiteSpace(inferred.Group))
            groupName = inferred.Group;

        // Corrige falso positivo antigo: DORI dentro de DORITOS
        if (string.Equals(extra.Marca, "DORI", StringComparison.OrdinalIgnoreCase)
            && ContainsToken(TextNorm.UpperStr(productName) ?? "", "DORITOS"))
        {
            extra.Marca = "DORITOS";
        }
        else if (string.IsNullOrWhiteSpace(extra.Marca) && !string.IsNullOrWhiteSpace(inferred.Brand))
        {
            extra.Marca = inferred.Brand;
        }
    }

    private static string? InferBrand(string nameUpper)
    {
        try
        {
            var catalog = ProductCatalogService.ListBrands()
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .Select(b => b.Trim().ToUpperInvariant())
                .Distinct()
                .OrderByDescending(b => b.Length)
                .ToList();
            foreach (var brand in catalog)
            {
                // Evita marca curta (DORI) casar dentro de nome maior (DORITOS)
                if (brand.Length >= 3 && ContainsToken(nameUpper, brand))
                    return brand;
            }
        }
        catch
        {
            // Catálogo indisponível — segue para regras fixas.
        }

        foreach (var (needle, brand) in BrandRules)
        {
            if (ContainsToken(nameUpper, needle))
                return brand;
        }

        return InferBrandFromLeadingWords(nameUpper);
    }

    /// <summary>
    /// Ex.: "PRESTIGIO Chocolate 30x33g BR" → PRESTIGIO
    /// "KIT KAT 4Fngr Leite..." → KIT KAT
    /// </summary>
    private static string? InferBrandFromLeadingWords(string nameUpper)
    {
        var cleaned = Regex.Replace(nameUpper, @"\d+\s*[xX×]\s*[\d.,]+[a-zA-Z]*", " ");
        cleaned = Regex.Replace(cleaned, @"\([^)]*\)", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var brandWords = new List<string>();
        foreach (var raw in words)
        {
            var w = raw.Trim();
            if (w.Length == 0)
                continue;
            if (char.IsDigit(w[0]))
                break;
            if (NonBrandWords.Contains(w))
                break;
            if (w.Length < 2)
                continue;

            brandWords.Add(w);
            // Duas palavras curtas (KIT KAT) ou uma longa já basta
            if (brandWords.Count >= 2 || brandWords[0].Length >= 4)
                break;
        }

        if (brandWords.Count == 0)
            return null;

        var brand = string.Join(" ", brandWords);
        return brand.Length >= 3 ? brand : null;
    }

    private static string? InferGroup(string nameUpper)
    {
        try
        {
            var catalog = ProductCatalogService.ListGroups()
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim().ToUpperInvariant())
                .Distinct()
                .OrderByDescending(g => g.Length)
                .ToList();
            foreach (var group in catalog)
            {
                if (group.Length >= 4 && ContainsToken(nameUpper, group))
                    return group;
            }
        }
        catch
        {
            // ignore
        }

        foreach (var (needles, group) in GroupRules)
        {
            foreach (var needle in needles)
            {
                if (ContainsToken(nameUpper, needle))
                    return group;
            }
        }

        return null;
    }

    /// <summary>
    /// Categorias perecíveis / bebidas: sugerem controle de validade (FEFO) quando o cadastro ainda não definiu.
    /// </summary>
    public static bool SuggestsExpiryControl(string? productName, string? groupName = null)
    {
        var g = (groupName ?? "").Trim().ToUpperInvariant();
        if (g.Length > 0)
        {
            if (g.Contains("CERVEJA") || g.Contains("REFRIGERANTE") || g.Contains("REFRI")
                || g.Contains("AGUA") || g.Contains("ÁGUA") || g.Contains("SUCO")
                || g.Contains("ENERGET") || g.Contains("VINHO") || g.Contains("DESTIL")
                || g.Contains("LATICIN") || g.Contains("LEITE") || g.Contains("FRIOS")
                || g.Contains("PADARIA") || g.Contains("GELO") || g.Contains("IOGUR"))
                return true;

            // Não perecíveis típicos
            if (g.Contains("CIGARRO") || g.Contains("HIGIENE") || g.Contains("LIMPEZA")
                || g.Contains("GAS") || g.Contains("VASILH"))
                return false;
        }

        var inferred = Infer(productName).Group ?? "";
        return inferred is "CERVEJA" or "REFRIGERANTE" or "AGUA" or "SUCO" or "ENERGETICO"
            or "VINHO" or "DESTILADO" or "LATICINIOS" or "FRIOS" or "PADARIA" or "GELO";
    }

    private static bool ContainsToken(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle) || haystack.Length < needle.Length)
            return false;

        // Casa palavra inteira: "DORI" não casa dentro de "DORITOS".
        var token = needle.Trim();
        if (token.Length == 0)
            return false;

        var start = 0;
        while (start <= haystack.Length - token.Length)
        {
            var idx = haystack.IndexOf(token, start, StringComparison.Ordinal);
            if (idx < 0)
                return false;

            var beforeOk = idx == 0 || !IsNameChar(haystack[idx - 1]);
            var after = idx + token.Length;
            var afterOk = after >= haystack.Length || !IsNameChar(haystack[after]);
            if (beforeOk && afterOk)
                return true;

            start = idx + 1;
        }

        return false;
    }

    /// <summary>Grupo/nome de cigarro (exceto produto "Varejo …" avulso).</summary>
    public static bool IsCigarette(string? name, string? group = null)
    {
        var n = (name ?? "").ToUpperInvariant();
        if (n.Contains("VAREJO"))
            return false;

        var g = (group ?? "").ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(g))
            g = (Infer(name).Group ?? "").ToUpperInvariant();
        if (g.Contains("CIGARR"))
            return true;

        // Marcas / padrões comuns (Souza Cruz, PMI, BAT…)
        if (n.Contains("CIGARRO") || n.Contains("MACO") || n.Contains("MAÇO")
            || Regex.IsMatch(n, @"\bHW\s*\d{2}\b")
            || n.Contains("BOX HW") || n.Contains("SC HW"))
            return true;

        return n.Contains("DUNHILL") || n.Contains("ROTH") || n.Contains("LUCKY STRIKE")
            || n.Contains("LUCKY") || n.Contains("MARLBORO") || n.Contains("HOLLYWOOD")
            || n.Contains("CARLTON") || n.Contains("DERBY") || n.Contains("MINISTER")
            || n.Contains("FREE ") || n.StartsWith("FREE") || n.Contains("KENT")
            || n.Contains("PARLIAMENT") || n.Contains("NEXT") || n.Contains("SHELL")
            || n.Contains("PLAZA") || n.Contains("CHARTT") || n.Contains("L&M")
            || n.Contains("L & M") || n.Contains("PHILIP") || n.Contains("BENSON");
    }

    /// <summary>
    /// Cigarro: Preço Compra/Custo/Venda no cadastro = valor do maço.
    /// Demais (ex.: refrigerante): unitário.
    /// </summary>
    public static bool UsesPackPurchasePrice(string? name, string? group = null) =>
        IsCigarette(name, group);

    private static bool IsNameChar(char c) =>
        char.IsLetterOrDigit(c) || c is '-' or '_' or '&';
}
