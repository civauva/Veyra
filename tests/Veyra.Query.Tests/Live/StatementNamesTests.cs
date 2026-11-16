using Veyra.Query.Live;

namespace Veyra.Query.Tests.Live;

/// <summary>
/// The Simple view names statements by what they do (ADR 0020). These tests prove the common shapes read as words and
/// that anything else keeps a short form of its text.
/// </summary>
public class StatementNamesTests
{
    [Theory]
    [InlineData(
        "UPDATE dbo.Orders SET Status = @status WHERE CustomerId = @customerId",
        "Update of Orders by CustomerId")]
    [InlineData("UPDATE [dbo].[Orders] set [Status] = @1  WHERE [Id]=@2", "Update of Orders by Id")]
    [InlineData("SELECT o.Id, o.Total FROM dbo.Orders AS o WHERE o.CustomerId = @c", "Read from Orders by CustomerId")]
    [InlineData("SELECT TOP (50) * FROM dbo.OrderLines WHERE OrderId IN (SELECT 1)", "Read from OrderLines by OrderId")]
    [InlineData("select count(*) from sales.Invoices", "Read from sales.Invoices")]
    [InlineData("INSERT INTO dbo.OrderEvents (OrderId) VALUES (@id)", "Insert into OrderEvents")]
    [InlineData("DELETE FROM dbo.Sessions WHERE ExpiresAt < @now", "Delete from Sessions by ExpiresAt")]
    [InlineData("MERGE INTO dbo.Stock AS t USING @rows AS s ON t.Id = s.Id", "Merge into Stock")]
    [InlineData("EXEC dbo.RefreshTokens @batch = 100", "Run RefreshTokens")]
    [InlineData("/* app */ EXECUTE reporting.MonthlyReport @month", "Run reporting.MonthlyReport")]
    [InlineData(
        "BEGIN TRAN;\nUPDATE dbo.Orders SET Status = N'held' WHERE Id = 1;\n",
        "Transaction: update of Orders by Id")]
    [InlineData("BEGIN TRANSACTION", "Transaction")]
    [InlineData("", "Nothing sent yet")]
    [InlineData("DBCC CHECKDB (shop) WITH NO_INFOMSGS", "DBCC CHECKDB (shop) WITH NO_INFOMSGS")]
    public void Describe_CommonStatements_ReadAsWords(string text, string expected)
    {
        Assert.Equal(expected, StatementNames.Describe(text));
    }

    [Fact]
    public void Describe_LongTextItCannotRead_IsShortened()
    {
        var text = "WAITFOR DELAY '00:00:10'; " + new string('x', 100);

        var name = StatementNames.Describe(text);

        Assert.Equal(61, name.Length);
        Assert.EndsWith("…", name, StringComparison.Ordinal);
    }
}
