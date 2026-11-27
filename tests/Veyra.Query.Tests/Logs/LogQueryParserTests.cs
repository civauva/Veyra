using Veyra.Query.Logs;

namespace Veyra.Query.Tests.Logs;

/// <summary>The query language of the log bar (ADR 0024): terms, their places in the text, and its errors.</summary>
public class LogQueryParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_Empty_HasNoTerms(string? text)
    {
        var parse = LogQueryParser.Parse(text);

        Assert.Null(parse.Error);
        Assert.Empty(parse.Terms);
    }

    [Fact]
    public void Parse_ScreenshotQuery_GivesEachTermWithItsPlace()
    {
        const string text = "service:CustomerSync AND level>=warning \"timed out\"";

        var parse = LogQueryParser.Parse(text);

        Assert.Null(parse.Error);
        Assert.Equal(
            [
                new LogQueryTerm(LogQueryField.Service, ":", "CustomerSync", 0, 20),
                new LogQueryTerm(LogQueryField.Level, ">=", "warning", 25, 39),
                new LogQueryTerm(LogQueryField.Text, null, "timed out", 40, 51),
            ],
            parse.Terms);
        Assert.Equal("\"timed out\"", text[40..51]);
    }

    [Fact]
    public void Parse_SpacesAndAnd_JoinTermsAlike()
    {
        var spaced = LogQueryParser.Parse("deadlock  orders");
        var joined = LogQueryParser.Parse("deadlock AND orders");

        Assert.Equal(["deadlock", "orders"], spaced.Terms.Select(term => term.Value));
        Assert.Equal(["deadlock", "orders"], joined.Terms.Select(term => term.Value));
        Assert.Equal((10, 16), (spaced.Terms[1].Start, spaced.Terms[1].End));
        Assert.Equal((13, 19), (joined.Terms[1].Start, joined.Terms[1].End));
    }

    [Fact]
    public void Parse_BareWords_AreTextTerms()
    {
        var parse = LogQueryParser.Parse("payment declined");

        Assert.All(parse.Terms, term => Assert.Equal(LogQueryField.Text, term.Field));
        Assert.Equal([(0, 7), (8, 16)], parse.Terms.Select(term => (term.Start, term.End)));
    }

    [Fact]
    public void Parse_LowerCaseAnd_IsAWord()
    {
        var parse = LogQueryParser.Parse("salt and pepper");

        Assert.Equal(["salt", "and", "pepper"], parse.Terms.Select(term => term.Value));
    }

    [Fact]
    public void Parse_QuotedValue_KeepsItsSpaces()
    {
        var parse = LogQueryParser.Parse("service:\"Orders API\" env:production");

        Assert.Equal(
            [
                new LogQueryTerm(LogQueryField.Service, ":", "Orders API", 0, 20),
                new LogQueryTerm(LogQueryField.Environment, ":", "production", 21, 35),
            ],
            parse.Terms);
    }

    [Fact]
    public void Parse_EscapedQuotes_AreKeptInThePhrase()
    {
        var parse = LogQueryParser.Parse("\"say \\\"hi\\\" \\\\ now\"");

        Assert.Equal("say \"hi\" \\ now", Assert.Single(parse.Terms).Value);
    }

    [Theory]
    [InlineData("service:a", nameof(LogQueryField.Service))]
    [InlineData("SERVICE:a", nameof(LogQueryField.Service))]
    [InlineData("env:a", nameof(LogQueryField.Environment))]
    [InlineData("environment:a", nameof(LogQueryField.Environment))]
    [InlineData("host:a", nameof(LogQueryField.Host))]
    [InlineData("level:error", nameof(LogQueryField.Level))]
    [InlineData("trace:4bf92f3577b34da6a3ce929d0e0e4736", nameof(LogQueryField.Trace))]
    [InlineData("span:00f067aa0ba902b7", nameof(LogQueryField.Span))]
    public void Parse_KnownField_HasItsField(string text, string field)
    {
        var term = Assert.Single(LogQueryParser.Parse(text).Terms);

        Assert.Equal(Enum.Parse<LogQueryField>(field), term.Field);
        Assert.Equal(":", term.Op);
        Assert.Null(term.Key);
    }

    [Fact]
    public void Parse_OtherKey_IsAnAttributeWithItsKey()
    {
        var parse = LogQueryParser.Parse("http.route:/pay url:http://shop:8080/cart");

        Assert.Equal(
            [
                new LogQueryTerm(LogQueryField.Attribute, ":", "/pay", 0, 15, "http.route"),
                new LogQueryTerm(LogQueryField.Attribute, ":", "http://shop:8080/cart", 16, 41, "url"),
            ],
            parse.Terms);
    }

    [Theory]
    [InlineData("level>=Warning")]
    [InlineData("level>=warn")]
    [InlineData("level:info")]
    [InlineData("level:FATAL")]
    public void Parse_Levels_AcceptNamesAndShortNamesInAnyCase(string text)
    {
        var parse = LogQueryParser.Parse(text);

        Assert.Null(parse.Error);
        Assert.Single(parse.Terms);
    }

    [Theory]
    [InlineData("time:5m", 0, "a term without time")]
    [InlineData("errors time:\"last hour\"", 7, "a term without time")]
    [InlineData("service:", 8, "a value")]
    [InlineData("service: orders", 8, "a value")]
    [InlineData("level>=", 7, "a value")]
    [InlineData("\"timed out", 10, "a closing quote")]
    [InlineData("service:\"Orders", 15, "a closing quote")]
    [InlineData("\"\"", 1, "text between the quotes")]
    [InlineData("AND deadlock", 0, "a term")]
    [InlineData("deadlock AND", 12, "a term")]
    [InlineData("deadlock AND  AND orders", 14, "a term")]
    [InlineData("deadlock OR orders", 9, "AND")]
    [InlineData("NOT deadlock", 0, "AND")]
    [InlineData(":orders", 0, "a field name")]
    [InlineData("host>=web-1", 0, "level")]
    [InlineData("level:loud", 6, "a level")]
    [InlineData("level>=\"very bad\"", 7, "a level")]
    [InlineData("service:\"a\"b", 11, "a space")]
    [InlineData("dead\"lock\"", 4, "a space")]
    public void Parse_Broken_SaysWhereAndWhatWasExpected(string text, int position, string expected)
    {
        var parse = LogQueryParser.Parse(text);

        Assert.Empty(parse.Terms);
        Assert.NotNull(parse.Error);
        Assert.Equal((position, expected), (parse.Error.Position, parse.Error.Expected));
        Assert.False(string.IsNullOrWhiteSpace(parse.Error.Message));
    }

    [Fact]
    public void Parse_Time_SaysTheTimeComesFromTheRange()
    {
        var parse = LogQueryParser.Parse("time:5m");

        Assert.Contains("range", parse.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_LongerThanTheLimit_IsRefusedAtTheLimit()
    {
        var parse = LogQueryParser.Parse(new string('a', LogQueryParser.MaxLength + 1));

        Assert.Equal(LogQueryParser.MaxLength, parse.Error!.Position);
    }
}
