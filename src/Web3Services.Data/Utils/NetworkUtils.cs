using Chrysalis.Wallet.Models.Enums;
using Microsoft.Extensions.Configuration;

namespace Web3Services.Data.Utils;

public static class NetworkUtils
{
    public static NetworkType GetNetworkType(IConfiguration configuration)
    {
        return configuration.GetValue<int>("NetworkMagic") switch
        {
            764824073 => NetworkType.Mainnet,
            1 => NetworkType.Preprod,
            2 => NetworkType.Testnet,
            _ => throw new NotImplementedException()
        };
    }
}