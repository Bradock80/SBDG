using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class VasilhameLancamentoWindow : Window
{
    private sealed class DraftItem
    {
        public int TypeId { get; init; }
        public string TypeName { get; init; } = "";
        public double Quantity { get; init; }
        public string QtyDisplay => ProductPriceHelper.RoundPrice(Quantity).ToString("0.###");
    }

    private readonly bool _isDevolucao;
    private readonly IReadOnlyList<ContainerType> _tipos;
    private readonly ObservableCollection<DraftItem> _itens = [];

    public VasilhameLancamentoWindow(bool isDevolucao, VasilhameSaldoRow? prefill = null)
    {
        _isDevolucao = isDevolucao;
        InitializeComponent();
        Title = isDevolucao ? "Devolver vasilhame" : "Quem pegou (empréstimo)";
        TitleHint.Text = isDevolucao
            ? "Registre a devolução — pode incluir vários tipos de uma vez."
            : "Registre quem saiu com galão/casco/garrafa. Inclua vários tipos e salve uma vez.";
        DuePanel.Visibility = isDevolucao ? Visibility.Collapsed : Visibility.Visible;
        if (!isDevolucao)
            DueBox.SelectedDate = DateTime.Today.AddDays(7);

        _tipos = ContainerTypesService.List(onlyActive: true);
        TipoBox.ItemsSource = _tipos;
        if (_tipos.Count > 0) TipoBox.SelectedIndex = 0;

        ItensGrid.ItemsSource = _itens;

        var clientes = PersonService.List(null, "ativos", "clientes").ToList();
        ClienteBox.ItemsSource = clientes;

        if (prefill is not null)
        {
            NomeBox.Text = prefill.BorrowerName;
            PhoneBox.Text = prefill.BorrowerPhone;
            QtyBox.Text = prefill.BalanceDisplay;
            TipoBox.SelectedItem = _tipos.FirstOrDefault(t => t.Id == prefill.ContainerTypeId);
            if (prefill.CustomerId is int cid)
                ClienteBox.SelectedItem = clientes.FirstOrDefault(c => c.Id == cid);

            // Já coloca o item do saldo na lista (devolução rápida)
            if (prefill.ContainerTypeId > 0 && prefill.Balance > 0.009)
            {
                _itens.Add(new DraftItem
                {
                    TypeId = prefill.ContainerTypeId,
                    TypeName = prefill.ContainerTypeName,
                    Quantity = prefill.Balance,
                });
                QtyBox.Text = "1";
            }
        }

        ClienteBox.SelectionChanged += (_, _) =>
        {
            if (ClienteBox.SelectedItem is not Person p) return;
            NomeBox.Text = p.Name;
            if (!string.IsNullOrWhiteSpace(p.Phone))
                PhoneBox.Text = p.Phone;
        };

        Loaded += (_, _) =>
        {
            NomeBox.Focus();
            NomeBox.SelectAll();
        };
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        if (TipoBox.SelectedItem is not ContainerType tipo || tipo.Id <= 0)
        {
            MessageBox.Show("Selecione o tipo de vasilhame.", "Vasilhame",
                MessageBoxButton.OK, MessageBoxImage.Information);
            TipoBox.Focus();
            return;
        }

        var qty = ProductPriceHelper.ParseBr(QtyBox.Text);
        if (qty < 0.009)
        {
            MessageBox.Show("Informe a quantidade.", "Vasilhame",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            QtyBox.Focus();
            return;
        }

        var existing = _itens.FirstOrDefault(i => i.TypeId == tipo.Id);
        if (existing is not null)
        {
            var idx = _itens.IndexOf(existing);
            _itens[idx] = new DraftItem
            {
                TypeId = tipo.Id,
                TypeName = tipo.Name,
                Quantity = ProductPriceHelper.RoundPrice(existing.Quantity + qty),
            };
        }
        else
        {
            _itens.Add(new DraftItem
            {
                TypeId = tipo.Id,
                TypeName = tipo.Name,
                Quantity = ProductPriceHelper.RoundPrice(qty),
            });
        }

        QtyBox.Text = "1";
        if (_tipos.Count > 0)
            TipoBox.SelectedIndex = Math.Min(TipoBox.SelectedIndex + 1, _tipos.Count - 1);
        TipoBox.Focus();
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DraftItem item })
            _itens.Remove(item);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Se a lista está vazia, inclui o tipo/qtd atual (atalho: sem clicar em Incluir)
            if (_itens.Count == 0)
                AddItem_Click(sender, e);
            if (_itens.Count == 0)
                return;

            int? customerId = ClienteBox.SelectedItem is Person p && p.Id > 0 ? p.Id : null;

            var name = (NomeBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name) && customerId is null)
                name = (ClienteBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name) && customerId is > 0 && ClienteBox.SelectedItem is Person pe)
                name = pe.Name;

            if (string.IsNullOrWhiteSpace(name) && customerId is null or <= 0)
            {
                MessageBox.Show("Informe o nome de quem pegou.", "Vasilhame",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                NomeBox.Focus();
                return;
            }

            var phone = (PhoneBox.Text ?? "").Trim();
            var notes = NotesBox.Text;
            var due = DueBox.SelectedDate;

            foreach (var item in _itens)
            {
                if (_isDevolucao)
                {
                    VasilhameService.RegistrarDevolucao(
                        item.TypeId, item.Quantity, customerId, name, phone, notes);
                }
                else
                {
                    VasilhameService.RegistrarSaida(
                        item.TypeId, item.Quantity, customerId, name, phone, due, notes);
                }
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Vasilhame", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
