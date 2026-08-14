using MH.Client;

namespace MH.Tests;

public sealed class ClientAppTests
{
    [Fact]
    public void ResolveServerBaseAddressUsesDefaultForMissingOrInvalidValues()
    {
        Assert.Equal("http://localhost:5002/", App.ResolveServerBaseAddress(null).AbsoluteUri);
        Assert.Equal("http://localhost:5002/", App.ResolveServerBaseAddress("localhost:5002").AbsoluteUri);
        Assert.Equal("http://localhost:5002/", App.ResolveServerBaseAddress("ftp://localhost:5002").AbsoluteUri);
        Assert.Equal("http://localhost:5002/", App.ResolveServerBaseAddress("http://localhost:5002/api/").AbsoluteUri);
        Assert.Equal("http://localhost:5002/", App.ResolveServerBaseAddress("http://localhost:5002/?mode=test").AbsoluteUri);
        Assert.Equal("http://localhost:5002/", App.ResolveServerBaseAddress("http://localhost:5002/#fragment").AbsoluteUri);
    }

    [Fact]
    public void ResolveServerBaseAddressAcceptsHttpAndHttpsAndAddsTrailingSlash()
    {
        Assert.Equal("http://127.0.0.1:6000/", App.ResolveServerBaseAddress("http://127.0.0.1:6000").AbsoluteUri);
        Assert.Equal("https://example.test/", App.ResolveServerBaseAddress("https://example.test/").AbsoluteUri);
    }
}
