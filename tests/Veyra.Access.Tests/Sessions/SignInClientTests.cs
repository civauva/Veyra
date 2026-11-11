using System.Net;
using Microsoft.AspNetCore.Http;
using Veyra.Access.Sessions;

namespace Veyra.Access.Tests.Sessions;

public class SignInClientTests
{
    [Fact]
    public void From_IPv4MappedToIPv6_IsStoredAsTheIPv4Address()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:172.19.0.1");

        Assert.Equal("172.19.0.1", SignInClient.From(context).Address);
    }

    [Fact]
    public void From_IPv6_StaysIPv6()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8::7");

        Assert.Equal("2001:db8::7", SignInClient.From(context).Address);
    }

    [Fact]
    public void From_WithoutUserAgent_HasNoBrowser()
    {
        var context = new DefaultHttpContext();

        Assert.Null(SignInClient.From(context).UserAgent);
    }
}
