using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using SGDB.Views;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69M-B — regressão: NfeImportModuleView não pode ter atributos órfãos
/// no StackPanel (XamlParseException ao abrir Notas de Fornecedores).
/// </summary>
public class NfeImportModuleViewLoadTests
{
    [Fact]
    public void XamlSource_CreateMissingBox_NaoTemAtributosOrfaosAposSelfClose()
    {
        var xaml = File.ReadAllText(Path.Combine(AppSourceRoot(), "Views", "NfeImportModuleView.xaml"));
        // Padrão que quebrava: ToolTip="..."/> seguido de VerticalAlignment/Margin órfãos.
        Assert.DoesNotMatch(
            new Regex(
                @"CreateMissingBox[\s\S]*?ToolTip\s*=\s*""[^""]*""\s*/>\s*\r?\n\s*VerticalAlignment",
                RegexOptions.CultureInvariant),
            xaml);

        Assert.Contains("x:Name=\"CreateMissingBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"0,0,14,0\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Construct_DoesNotThrowXamlParseException()
    {
        Exception? error = null;
        var done = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            try
            {
                EnsureWpfApplication();
                var view = new NfeImportModuleView();
                Assert.NotNull(view);
                Assert.NotNull(view.Content);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                done.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(done.Wait(TimeSpan.FromSeconds(45)), "Timeout ao instanciar NfeImportModuleView.");

        if (error is not null)
        {
            Assert.Fail(
                "Falha ao carregar NfeImportModuleView (esperado: sem XamlParseException):\n" +
                error);
        }
    }

    private static void EnsureWpfApplication()
    {
        if (System.Windows.Application.Current is not null)
            return;

        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var theme = new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/SGDB;component/Themes/SgdbTheme.xaml",
                UriKind.Absolute),
        };
        app.Resources.MergedDictionaries.Add(theme);
    }

    private static string AppSourceRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "src", "SGDB.App");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "src", "SGDB.App"));
    }
}
