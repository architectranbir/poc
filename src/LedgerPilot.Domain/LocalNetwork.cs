using System.Net;
namespace LedgerPilot.Domain;
public static class LocalNetwork
{
    public static bool Allows(IPAddress? address, bool containerMode)
    {
        if(address is null)return false;
        if(IPAddress.IsLoopback(address))return true;
        if(!containerMode)return false;
        var ip=address.IsIPv4MappedToIPv6?address.MapToIPv4():address;
        var b=ip.GetAddressBytes();
        return b.Length==4 && (b[0]==10 || (b[0]==172 && b[1]>=16 && b[1]<=31) || (b[0]==192 && b[1]==168));
    }
}
