using System.Net;
using LedgerPilot.Domain;
namespace LedgerPilot.Domain.Tests;
public sealed class LocalNetworkTests
{
    [Theory]
    [InlineData("10.2.3.4",true)]
    [InlineData("172.16.0.1",true)]
    [InlineData("172.31.255.255",true)]
    [InlineData("192.168.1.1",true)]
    [InlineData("::ffff:172.20.0.1",true)]
    [InlineData("172.32.0.1",false)]
    [InlineData("8.8.8.8",false)]
    [InlineData("2001:4860:4860::8888",false)]
    public void ContainerModeOnlyAllowsPrivateAddresses(string ip,bool allowed)
    {
        Assert.Equal(allowed,LocalNetwork.Allows(IPAddress.Parse(ip),true));
        Assert.False(LocalNetwork.Allows(IPAddress.Parse(ip),false));
    }
}
