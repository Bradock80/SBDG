using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public static class AuditDetailViewBuilder
{
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
    private static readonly Brush ValueBrush = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
    private static readonly Brush DangerBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
    private static readonly Brush CardBg = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
    private static readonly Brush CardBorder = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));

    public static UIElement Build(AuditLogRow row)
    {
        var root = new StackPanel();

        root.Children.Add(BuildMetaCard(row));

        if (AuditPayloadBuilder.TryParse(row.Details, out var doc) && doc.Payload.ValueKind == JsonValueKind.Object)
        {
            var op = GetString(doc.Payload, "op") ?? InferOp(row);
            BuildPayloadContent(root, op, doc.Payload, row);
        }
        else
        {
            root.Children.Add(BuildPlainCard("Detalhes", AuditLogPresentation.GetDetailsDisplay(row)));
        }

        return root;
    }

    private static string? InferOp(AuditLogRow row)
    {
        var a = (row.Action ?? "").Trim().ToLowerInvariant();
        var e = (row.Entity ?? "").Trim().ToLowerInvariant();
        return (a, e) switch
        {
            ("cancelar", "venda") => "cancel_venda",
            ("remover", "item") => "cancel_item",
            ("desconto", _) => "desconto",
            ("sangria", _) => "sangria",
            ("suprimento", _) => "suprimento",
            ("abrir", "caixa") => "abrir_cx",
            ("fechar", "caixa") => "fechar_cx",
            ("alterar", "produto") => "alterar_produto",
            ("entrada", "compra") => "entrada_compra",
            ("criar", _) => "criar_pessoa",
            ("alterar", "cliente" or "fornecedor" or "pessoa") => "alterar_pessoa",
            ("venda", "venda") => "venda",
            _ => null,
        };
    }

    private static void BuildPayloadContent(StackPanel root, string? op, JsonElement payload, AuditLogRow row)
    {
        switch (op)
        {
            case "fechar_cx":
                BuildCashClose(root, payload, row);
                break;
            case "abrir_cx":
            case "reabrir_cx":
                BuildCashOpen(root, payload, row);
                break;
            case "cancel_venda":
                BuildSaleCancel(root, payload, row);
                break;
            case "cancel_item":
                BuildItemRemove(root, payload, row);
                break;
            case "desconto":
                BuildDiscount(root, payload, row);
                break;
            case "sangria":
            case "suprimento":
                BuildCashMovement(root, payload, row, op);
                break;
            case "alterar_produto":
                BuildProductChange(root, payload, row);
                break;
            case "entrada_compra":
                BuildPurchaseEntry(root, payload, row);
                break;
            case "criar_pessoa":
            case "alterar_pessoa":
                BuildPersonChange(root, payload, row, op);
                break;
            case "venda":
                BuildSale(root, payload, row);
                break;
            default:
                root.Children.Add(BuildPlainCard("Resumo", AuditPayloadBuilder.GetSummary(row.Details) ?? AuditLogPresentation.GetDetailsDisplay(row)));
                root.Children.Add(BuildJsonCard(payload));
                break;
        }
    }

    private static void BuildCashClose(StackPanel root, JsonElement p, AuditLogRow row)
    {
        var expected = GetDouble(p, "expected");
        var counted = GetDouble(p, "counted");
        var diff = GetDouble(p, "difference");
        var sessionId = GetInt(p, "session_id") ?? ParseInt(row.EntityId);

        var card = new StackPanel();
        var operatorName = GetString(p, "operator_name");
        AddField(card, "Operador", string.IsNullOrWhiteSpace(operatorName) ? row.UserName : operatorName!);
        var opId = GetInt(p, "operator_id") ?? GetInt(p, "user_id");
        if (opId is int oid && oid > 0)
            AddField(card, "ID do operador", oid.ToString());
        if (sessionId > 0)
            AddField(card, "Caixa / Sessão", $"#{sessionId}");
        AddField(card, "Valor calculado pelo sistema", Money(expected));
        AddField(card, "Valor informado pelo operador", Money(counted));
        AddField(card, "Diferença / Quebra de caixa", FormatDifference(diff), diff != 0 ? DangerBrush : ValueBrush);
        var notes = GetString(p, "notes");
        if (!string.IsNullOrWhiteSpace(notes))
            AddField(card, "Observações", notes);
        root.Children.Add(WrapCard("🔴 Fechamento de Caixa", card));
    }

    private static void BuildCashOpen(StackPanel root, JsonElement p, AuditLogRow row)
    {
        var reopening = GetBool(p, "reopening");
        var amount = GetDouble(p, "opening_amount");
        var sessionId = GetInt(p, "session_id") ?? ParseInt(row.EntityId);
        var card = new StackPanel();
        AddField(card, "Operador", row.UserName);
        if (sessionId > 0)
            AddField(card, "Sessão", $"#{sessionId}");
        AddField(card, "Troco inicial", Money(amount));
        if (reopening)
            AddField(card, "Tipo", "Reabertura de caixa");
        var notes = GetString(p, "notes");
        if (!string.IsNullOrWhiteSpace(notes))
            AddField(card, "Observações", notes);
        root.Children.Add(WrapCard(reopening ? "🔵 Reabertura de Caixa" : "🔵 Abertura de Caixa", card));
    }

    private static void BuildSaleCancel(StackPanel root, JsonElement p, AuditLogRow row)
    {
        var saleId = GetInt(p, "sale_id") ?? ParseInt(row.EntityId);
        var total = GetDouble(p, "total");
        var reason = GetString(p, "reason");

        var card = new StackPanel();
        AddField(card, "Operador", row.UserName);
        if (saleId > 0)
            AddField(card, "Venda", $"#{saleId}");
        AddField(card, "Valor total", Money(total));
        if (!string.IsNullOrWhiteSpace(reason))
            AddField(card, "Motivo", reason);

        if (p.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
        {
            card.Children.Add(Spacer());
            card.Children.Add(SectionTitle("Itens devolvidos ao estoque"));
            card.Children.Add(BuildItemsTable(items));
        }

        root.Children.Add(WrapCard("🔴 Cancelamento de Venda", card));
    }

    private static void BuildItemRemove(StackPanel root, JsonElement p, AuditLogRow row)
    {
        var card = new StackPanel();
        AddField(card, "Operador", row.UserName);
        if (p.TryGetProperty("line", out var line) && line.ValueKind == JsonValueKind.Object)
        {
            AddField(card, "Produto", GetString(line, "name") ?? "—");
            AddField(card, "Código", GetString(line, "code") ?? "—");
            AddField(card, "Quantidade removida", Qty(GetDouble(line, "quantity")));
            AddField(card, "Preço unitário", Money(GetDouble(line, "unit_price")));
            AddField(card, "Subtotal do item", Money(GetDouble(line, "subtotal")));
        }
        var cartAfter = GetDouble(p, "cart_total_after");
        if (cartAfter > 0)
            AddField(card, "Total do carrinho após remoção", Money(cartAfter));
        root.Children.Add(WrapCard("🔴 Cancelamento de Item", card));
    }

    private static void BuildDiscount(StackPanel root, JsonElement p, AuditLogRow row)
    {
        var saleId = GetInt(p, "sale_id") ?? ParseInt(row.EntityId);
        var card = new StackPanel();
        AddField(card, "Operador", row.UserName);
        if (saleId > 0)
            AddField(card, "Venda", $"#{saleId}");
        AddField(card, "Subtotal", Money(GetDouble(p, "subtotal")));
        AddField(card, "Desconto", Money(GetDouble(p, "discount")));
        AddField(card, "Percentual", $"{GetDouble(p, "discount_pct"):N1}%");
        AddField(card, "Total após desconto", Money(GetDouble(p, "total_after")));
        var pay = GetString(p, "payment_type");
        if (!string.IsNullOrWhiteSpace(pay))
            AddField(card, "Forma de pagamento", pay);
        root.Children.Add(WrapCard("🟠 Desconto Concedido", card));
    }

    private static void BuildCashMovement(StackPanel root, JsonElement p, AuditLogRow row, string op)
    {
        var card = new StackPanel();
        AddField(card, "Operador", row.UserName);
        AddField(card, "Valor", Money(GetDouble(p, "amount")));
        var reason = GetString(p, "reason") ?? GetString(p, "notes");
        if (!string.IsNullOrWhiteSpace(reason))
            AddField(card, "Justificativa", reason);
        var title = op == "sangria" ? "🟡 Sangria de Caixa" : "🟢 Suprimento de Caixa";
        root.Children.Add(WrapCard(title, card));
    }

    private static void BuildProductChange(StackPanel root, JsonElement p, AuditLogRow row)
    {
        var card = new StackPanel();
        AddField(card, "Operador", row.UserName);
        AddField(card, "Produto", GetString(p, "name") ?? "—");
        var code = GetString(p, "code");
        if (!string.IsNullOrWhiteSpace(code))
            AddField(card, "Referência", code);
        var source = GetString(p, "source");
        if (!string.IsNullOrWhiteSpace(source))
            AddField(card, "Origem", source);

        if (p.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Object)
        {
            card.Children.Add(Spacer());
            card.Children.Add(SectionTitle("Alterações"));
            card.Children.Add(BuildChangesTable(changes));
        }

        root.Children.Add(WrapCard("🔵 Alteração de Produto", card));
    }

    private static void BuildPurchaseEntry(StackPanel root, JsonElement p, AuditLogRow row)
    {
        var card = new StackPanel();
        AddField(card, "Operador", row.UserName);
        AddField(card, "Fornecedor", GetString(p, "supplier_name") ?? "—");
        var number = GetString(p, "number");
        if (!string.IsNullOrWhiteSpace(number))
            AddField(card, "Número NF", number);
        var nfe = GetString(p, "nfe_key");
        if (!string.IsNullOrWhiteSpace(nfe))
            AddField(card, "Chave NF-e", nfe);
        AddField(card, "Valor total", Money(GetDouble(p, "total")));
        AddField(card, "Itens", GetInt(p, "items_count")?.ToString() ?? "—");
        AddField(card, "Gerou estoque", GetBool(p, "gerar_estoque") ? "Sim" : "Não");
        var source = GetString(p, "source");
        if (!string.IsNullOrWhiteSpace(source))
            AddField(card, "Origem", source == "nfe_xml" ? "Importação XML NF-e" : "Lançamento manual");
        root.Children.Add(WrapCard("🔵 Entrada de Compra / NF-e", card));
    }

    private static void BuildPersonChange(StackPanel root, JsonElement p, AuditLogRow row, string op)
    {
        var isNew = op == "criar_pessoa";
        var card = new StackPanel();
        AddField(card, "Operador", row.UserName);
        AddField(card, "Nome", GetString(p, "name") ?? "—");

        if (!isNew && p.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Object)
        {
            card.Children.Add(Spacer());
            card.Children.Add(SectionTitle("Alterações"));
            card.Children.Add(BuildChangesTable(changes));
        }

        var title = isNew ? "🔵 Cadastro de Cliente/Fornecedor" : "🔵 Alteração de Cliente/Fornecedor";
        root.Children.Add(WrapCard(title, card));
    }

    private static void BuildSale(StackPanel root, JsonElement p, AuditLogRow row)
    {
        var saleId = GetInt(p, "sale_id") ?? ParseInt(row.EntityId);
        var card = new StackPanel();
        AddField(card, "Operador", row.UserName);
        if (saleId > 0)
            AddField(card, "Venda", $"#{saleId}");
        AddField(card, "Total", Money(GetDouble(p, "total")));
        var pay = GetString(p, "payment_type");
        if (!string.IsNullOrWhiteSpace(pay))
            AddField(card, "Pagamento", pay);
        var itemsCount = GetInt(p, "items_count");
        if (itemsCount is > 0)
            AddField(card, "Itens", itemsCount.Value.ToString());
        var discount = GetDouble(p, "discount");
        if (discount > 0.009)
            AddField(card, "Desconto na venda", Money(discount));
        root.Children.Add(WrapCard("🔵 Venda Registrada", card));
    }

    private static Border WrapCard(string title, Panel content)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = ValueBrush,
            Margin = new Thickness(0, 0, 0, 10),
        });
        stack.Children.Add(content);

        return new Border
        {
            Background = CardBg,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            Child = stack,
        };
    }

    private static Border BuildPlainCard(string title, string text)
    {
        var stack = new StackPanel();
        stack.Children.Add(SectionTitle(title));
        stack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(text) ? "—" : text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ValueBrush,
            FontSize = 12,
        });
        return WrapCard(title, stack);
    }

    private static Border BuildJsonCard(JsonElement payload)
    {
        var formatted = payload.ToString();
        try { formatted = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }); }
        catch { /* keep compact */ }

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = formatted,
            FontFamily = new FontFamily("Consolas, Courier New"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = LabelBrush,
        });
        return WrapCard("Dados técnicos", stack);
    }

    private static Border BuildMetaCard(AuditLogRow row)
    {
        var card = new StackPanel();
        AddField(card, "Data/Hora", row.DateDisplay);
        AddField(card, "Usuário", $"{row.UserName} ({row.UserLogin})");
        AddField(card, "Registro", $"#{row.Id}");
        if (!string.IsNullOrWhiteSpace(row.EntityId))
            AddField(card, "ID entidade", row.EntityId);
        return WrapCard("Informações do evento", card);
    }

    private static DataGrid BuildItemsTable(JsonElement items)
    {
        var rows = new List<ItemRow>();
        foreach (var item in items.EnumerateArray())
        {
            rows.Add(new ItemRow
            {
                Produto = GetString(item, "name") ?? "—",
                Qtd = Qty(GetDouble(item, "qty", "quantity")),
                Preco = Money(GetDouble(item, "unit_price")),
            });
        }

        var grid = CreateGrid(["Produto", "Qtd", "Preço"]);
        foreach (var r in rows)
            grid.Items.Add(r);
        return grid;
    }

    private static DataGrid BuildChangesTable(JsonElement changes)
    {
        var rows = new List<ChangeRow>();
        foreach (var prop in changes.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                rows.Add(new ChangeRow
                {
                    Campo = FormatFieldName(prop.Name),
                    De = FormatChangeValue(prop.Name, prop.Value, "de"),
                    Para = FormatChangeValue(prop.Name, prop.Value, "para"),
                });
            }
            else
            {
                rows.Add(new ChangeRow
                {
                    Campo = FormatFieldName(prop.Name),
                    De = "—",
                    Para = FormatJsonValue(prop.Value),
                });
            }
        }

        var grid = CreateGrid(["Campo", "De", "Para"]);
        foreach (var r in rows)
            grid.Items.Add(r);
        return grid;
    }

    private static DataGrid CreateGrid(IReadOnlyList<string> headers)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            BorderThickness = new Thickness(1),
            BorderBrush = CardBorder,
            Background = Brushes.White,
            RowBackground = Brushes.White,
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0xFA, 0xFB, 0xFC)),
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            Margin = new Thickness(0, 4, 0, 0),
            MaxHeight = 180,
        };

        foreach (var h in headers)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = h,
                Binding = new System.Windows.Data.Binding(h),
                Width = h == "Produto" || h == "Campo" ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto,
            });
        }

        return grid;
    }

    private sealed class ItemRow
    {
        public string Produto { get; init; } = "";
        public string Qtd { get; init; } = "";
        public string Preco { get; init; } = "";
    }

    private sealed class ChangeRow
    {
        public string Campo { get; init; } = "";
        public string De { get; init; } = "";
        public string Para { get; init; } = "";
    }

    private static void AddField(Panel panel, string label, string value, Brush? valueBrush = null)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Muted,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Top,
        });

        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = valueBrush ?? ValueBrush,
            FontSize = 12,
            FontWeight = valueBrush == DangerBrush ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(valueBlock);
        panel.Children.Add(row);
    }

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        FontSize = 12,
        Foreground = LabelBrush,
        Margin = new Thickness(0, 0, 0, 4),
    };

    private static UIElement Spacer() => new Border { Height = 4 };

    private static string Money(double v) => $"R$ {v:N2}";

    private static string Qty(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);

    private static string FormatDifference(double diff)
    {
        if (Math.Abs(diff) < 0.009)
            return "R$ 0,00 (sem diferença)";
        return diff > 0
            ? $"+{Money(diff)} (sobra)"
            : $"-{Money(Math.Abs(diff))} (falta)";
    }

    private static string FormatFieldName(string key) => key switch
    {
        "preco_venda" => "Preço de venda",
        "preco_custo" => "Preço de custo",
        "estoque" => "Estoque",
        "nome" => "Nome",
        "cpf_cnpj" => "CPF/CNPJ",
        "papeis" => "Papéis",
        "ativo" => "Ativo",
        _ => char.ToUpper(key[0]) + key[1..].Replace('_', ' '),
    };

    private static string FormatChangeValue(string field, JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var val))
            return "—";
        if (field is "preco_venda" or "preco_custo" && val.ValueKind == JsonValueKind.Number && val.TryGetDouble(out var money))
            return Money(money);
        if (field == "estoque" && val.ValueKind == JsonValueKind.Number && val.TryGetDouble(out var qty))
            return Qty(qty);
        return FormatJsonValue(val);
    }

    private static string FormatJsonValue(JsonElement val) => val.ValueKind switch
    {
        JsonValueKind.Number when val.TryGetDouble(out var d) => Math.Abs(d) > 1000 || val.ToString().Contains('.')
            ? (d % 1 == 0 ? d.ToString("0.##") : Money(d))
            : Money(d),
        JsonValueKind.True => "Sim",
        JsonValueKind.False => "Não",
        JsonValueKind.String => val.GetString() ?? "—",
        _ => val.ToString(),
    };

    private static string? GetString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        return p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
    }

    private static double GetDouble(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var p))
                continue;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d))
                return d;
        }
        return 0;
    }

    private static int? GetInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i))
            return i;
        return null;
    }

    private static bool GetBool(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static int ParseInt(string? s) =>
        int.TryParse(s, out var i) ? i : 0;
}
