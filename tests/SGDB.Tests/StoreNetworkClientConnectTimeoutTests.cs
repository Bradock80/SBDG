using System.Diagnostics;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 66F — ConnectTimeout curto evita espera de ~21 s do TCP do Windows
/// quando o PC da loja não completa o handshake.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class StoreNetworkClientConnectTimeoutTests
{
    private static readonly TimeSpan OsTcpTimeout = TimeSpan.FromSeconds(18);
    private static readonly TimeSpan FailFastCeiling = TimeSpan.FromSeconds(10);

    [Fact]
    public void ConnectTimeout_IsTwoSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), StoreNetworkClient.ConnectTimeout);
    }

    [Fact]
    public void ListProducts_UnreachableHost_FailsFasterThanOsTcpTimeout()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SaveClient("192.0.2.1", "1234", StoreNetworkMode.DefaultPort);

        try
        {
            var sw = Stopwatch.StartNew();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                StoreNetworkClient.ListProducts(null, "ativos", null, null, null, "none"));
            sw.Stop();

            Assert.Contains("Não conectou", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(sw.Elapsed < OsTcpTimeout,
                $"ListProducts levou {sw.Elapsed.TotalSeconds:N2}s; o TCP do Windows sem ConnectTimeout espera ~21s.");
            Assert.True(sw.Elapsed < FailFastCeiling,
                $"ListProducts levou {sw.Elapsed.TotalSeconds:N2}s; esperado falhar perto de {StoreNetworkClient.ConnectTimeout.TotalSeconds:N0}s.");
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void Login_UnreachableHost_FailsFasterThanOsTcpTimeout()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SaveClient("192.0.2.1", "1234", StoreNetworkMode.DefaultPort);

        try
        {
            var sw = Stopwatch.StartNew();
            var ex = Record.Exception(() => StoreNetworkClient.Login("1234"));
            sw.Stop();

            Assert.NotNull(ex);
            Assert.True(sw.Elapsed < OsTcpTimeout,
                $"Login levou {sw.Elapsed.TotalSeconds:N2}s; o TCP do Windows sem ConnectTimeout espera ~21s.");
            Assert.True(sw.Elapsed < FailFastCeiling,
                $"Login levou {sw.Elapsed.TotalSeconds:N2}s; esperado falhar perto de {StoreNetworkClient.ConnectTimeout.TotalSeconds:N0}s.");
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }
}
