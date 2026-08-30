using System.Globalization;

namespace SGDB.Tests.Infrastructure;

/// <summary>Troca CurrentCulture/UICulture e restaura. Não altera política do Windows.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture;
    private readonly CultureInfo _ui;

    public CultureScope(string cultureName)
    {
        _culture = CultureInfo.CurrentCulture;
        _ui = CultureInfo.CurrentUICulture;
        var c = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentCulture = c;
        CultureInfo.CurrentUICulture = c;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _ui;
    }
}
