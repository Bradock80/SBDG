using SGDB.Services;

namespace SGDB.Tests;

/// <summary>Parse puro 70D-B2. Sem SQLite. Sem CurrentCulture.</summary>
public class InventoryProjectionLotParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Missing_expiry_is_undated_not_invalid(string? raw)
    {
        var parsed = InventoryProjectionLotParser.ParseExpiry(raw);
        Assert.Equal(InventoryProjectionLotParser.ExpiryKind.Missing, parsed.Kind);
        Assert.Null(parsed.Date);
    }

    [Fact]
    public void Iso_yyyy_MM_dd_is_civil_date()
    {
        var parsed = InventoryProjectionLotParser.ParseExpiry("2026-08-30");
        Assert.Equal(InventoryProjectionLotParser.ExpiryKind.ValidIso, parsed.Kind);
        Assert.Equal(new DateTime(2026, 8, 30), parsed.Date);
    }

    [Fact]
    public void Iso_with_surrounding_spaces_is_valid()
    {
        var parsed = InventoryProjectionLotParser.ParseExpiry("  2026-08-30  ");
        Assert.Equal(InventoryProjectionLotParser.ExpiryKind.ValidIso, parsed.Kind);
        Assert.Equal(new DateTime(2026, 8, 30), parsed.Date);
    }

    [Theory]
    [InlineData("30/08/2026")]
    [InlineData("08/30/2026")]
    [InlineData("2026/08/30")]
    [InlineData("abc")]
    [InlineData("2026-99-99")]
    [InlineData("2026-08-30T00:00:00")]
    [InlineData("2026-08-30 00:00:00")]
    public void Non_iso_non_empty_is_invalid_not_undated(string raw)
    {
        var parsed = InventoryProjectionLotParser.ParseExpiry(raw);
        Assert.Equal(InventoryProjectionLotParser.ExpiryKind.Invalid, parsed.Kind);
        Assert.Null(parsed.Date);
    }

    [Fact]
    public void Sqlite_number_reads_numeric_types_without_culture()
    {
        Assert.Equal(5.5, InventoryProjectionLotParser.ReadSqliteNumber(5.5), 8);
        Assert.Equal(8, InventoryProjectionLotParser.ReadSqliteNumber(8L), 8);
        Assert.Equal(-3, InventoryProjectionLotParser.ReadSqliteNumber(-3.0), 8);
        Assert.Equal(0, InventoryProjectionLotParser.ReadSqliteNumber(0.0), 8);
        Assert.True(double.IsNaN(InventoryProjectionLotParser.ReadSqliteNumber(null)));
        Assert.True(double.IsNaN(InventoryProjectionLotParser.ReadSqliteNumber(DBNull.Value)));
        Assert.True(double.IsNaN(InventoryProjectionLotParser.ReadSqliteNumber("abc")));
        Assert.Equal(1.25, InventoryProjectionLotParser.ReadSqliteNumber("1.25"), 8);
    }
}
