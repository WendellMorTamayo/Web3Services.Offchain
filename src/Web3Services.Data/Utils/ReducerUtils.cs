using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Enums;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Web3Services.Data.Utils;

public static class ReducerUtils
{
    public static bool TryGetBech32AddressParts(TransactionOutput output, out string paymentKeyHashHex, out string stakeKeyHashHex)
    {
        paymentKeyHashHex = string.Empty;
        stakeKeyHashHex = string.Empty;

        try
        {
            WalletAddress address = new(output.Address());
            if (!address.ToBech32().StartsWith("addr")) return false;

            byte[]? pkhBytes = address.GetPaymentKeyHash();
            byte[]? skBytes = address.GetStakeKeyHash();

            paymentKeyHashHex = pkhBytes is null ? string.Empty : Convert.ToHexStringLower(pkhBytes);
            stakeKeyHashHex = skBytes is null ? string.Empty : Convert.ToHexStringLower(skBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetBech32AddressParts(string bech32Address, out string paymentKeyHashHex, out string? stakeKeyHashHex)
    {
        paymentKeyHashHex = string.Empty;
        stakeKeyHashHex = null;

        try
        {
            WalletAddress address = new(bech32Address);
            if (!address.ToBech32().StartsWith("addr")) return false;

            byte[]? pkhBytes = address.GetPaymentKeyHash();
            byte[]? skBytes = address.GetStakeKeyHash();

            paymentKeyHashHex = pkhBytes is null ? string.Empty : Convert.ToHexStringLower(pkhBytes);
            stakeKeyHashHex = skBytes is null ? null : Convert.ToHexStringLower(skBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static IEnumerable<(string PaymentHash, string StakeHash)> ExtractAddressHashesFromBlock(IEnumerable<TransactionBody> transactions)
    {
        IEnumerable<(string PaymentHash, string StakeHash)> blockAddresses = transactions
            .SelectMany(tx => tx.Outputs())
            .Select(output =>
            {
                if (TryGetBech32AddressParts(output, out string paymentHash, out string? stakeHash))
                    return (PaymentHash: paymentHash, StakeHash: stakeHash ?? string.Empty);
                return (PaymentHash: string.Empty, StakeHash: string.Empty);
            })
            .Where(h => !string.IsNullOrEmpty(h.PaymentHash))
            .Distinct();

        return blockAddresses;
    }

    public static IEnumerable<string> ExtractSubjectsFromOutput(TransactionOutput output)
    {
        Dictionary<byte[], TokenBundleOutput> multiAsset = output.Amount().MultiAsset();
        if (multiAsset == null) return [];

        IEnumerable<string> subjects = multiAsset.SelectMany(policyEntry =>
        {
            string policyId = Convert.ToHexStringLower(policyEntry.Key);
            return policyEntry.Value.Value.Select(assetEntry =>
            {
                string assetName = Convert.ToHexStringLower(assetEntry.Key);
                return policyId + assetName;
            });
        });

        return subjects;
    }

    public static string ConstructBech32Address(string paymentKeyHash, string stakeKeyHash, NetworkType network = NetworkType.Mainnet)
    {
        try
        {
            byte[] paymentBytes = Convert.FromHexString(paymentKeyHash);
            byte[]? stakeBytes = string.IsNullOrEmpty(stakeKeyHash) ? null : Convert.FromHexString(stakeKeyHash);

            AddressType addressType = stakeBytes != null ? AddressType.Base : AddressType.EnterprisePayment;
            WalletAddress address = new(network, addressType, paymentBytes, stakeBytes);
            return address.ToBech32();
        }
        catch
        {
            return string.Empty;
        }
    }
}