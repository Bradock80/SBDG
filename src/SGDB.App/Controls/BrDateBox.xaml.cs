using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using SGDB.Utils;

namespace SGDB.Controls;

/// <summary>
/// Campo de data com calendário (pt-BR, dd/MM/yyyy).
/// Expõe <see cref="Text"/> e <see cref="SelectedDate"/> para uso fácil no código atual.
/// </summary>
public partial class BrDateBox : UserControl
{
    private static readonly SolidColorBrush NormalBorder;
    private static readonly SolidColorBrush FocusBorder;

    static BrDateBox()
    {
        NormalBorder = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
        FocusBorder = new SolidColorBrush(Color.FromRgb(0x25, 0x4A, 0x75));
        NormalBorder.Freeze();
        FocusBorder.Freeze();
    }

    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(
            nameof(SelectedDate),
            typeof(DateTime?),
            typeof(BrDateBox),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedDateChanged));

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(
            nameof(IsReadOnly),
            typeof(bool),
            typeof(BrDateBox),
            new PropertyMetadata(false, OnIsReadOnlyChanged));

    private bool _syncing;

    public BrDateBox()
    {
        InitializeComponent();
        Chrome.BorderBrush = NormalBorder;

        var culture = CultureInfo.GetCultureInfo("pt-BR");
        Language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
        Picker.Language = Language;
        Picker.SelectedDateFormat = DatePickerFormat.Short;

        Picker.GotKeyboardFocus += (_, _) => Chrome.BorderBrush = FocusBorder;
        Picker.LostKeyboardFocus += (_, _) => Chrome.BorderBrush = NormalBorder;
    }

    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>Data em dd/MM/yyyy (vazio se não houver seleção).</summary>
    public string Text
    {
        get => SelectedDate is DateTime d ? d.ToString("dd/MM/yyyy") : "";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                SelectedDate = null;
                return;
            }

            if (DateBrHelper.TryParseBr(value, out var dt))
                SelectedDate = dt.Date;
        }
    }

    public bool TryGetDate(out DateTime date)
    {
        if (SelectedDate is DateTime d)
        {
            date = d.Date;
            return true;
        }

        date = default;
        return false;
    }

    public void SetDate(DateTime date) => SelectedDate = date.Date;

    public void Clear() => SelectedDate = null;

    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not BrDateBox box || box._syncing)
            return;
        box._syncing = true;
        try
        {
            box.Picker.SelectedDate = e.NewValue as DateTime?;
        }
        finally
        {
            box._syncing = false;
        }
    }

    private static void OnIsReadOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BrDateBox box)
            box.Picker.IsEnabled = !(bool)e.NewValue;
    }

    private void Picker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing)
            return;
        _syncing = true;
        try
        {
            SelectedDate = Picker.SelectedDate?.Date;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void BrDateBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!ReferenceEquals(e.NewFocus, this))
            return;
        Picker.Focus();
        e.Handled = true;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty && Chrome is not null)
            Chrome.Opacity = IsEnabled ? 1 : 0.7;
    }
}
