using Chrysalis.Cbor.Extensions.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Web3Services.Data.Models.Enums;

namespace Web3Services.Data.Extensions;

public static class CertificateExtensions
{
    public static CertificateType GetCertificateType(this Certificate certificate)
    {
        return certificate switch
        {
            StakeRegistration => CertificateType.StakeRegistration,
            StakeDeregistration => CertificateType.StakeDeregistration,
            StakeDelegation => CertificateType.StakeDelegation,
            PoolRegistration => CertificateType.PoolRegistration,
            PoolRetirement => CertificateType.PoolRetirement,
            RegCert => CertificateType.RegCert,
            UnRegCert => CertificateType.UnregCert,
            VoteDelegCert => CertificateType.VoteDelegCert,
            StakeVoteDelegCert => CertificateType.StakeVoteDelegCert,
            StakeRegDelegCert => CertificateType.StakeRegDelegCert,
            VoteRegDelegCert => CertificateType.VoteRegDelegCert,
            StakeVoteRegDelegCert => CertificateType.StakeVoteRegDelegCert,
            AuthCommitteeHotCert => CertificateType.AuthCommitteeHotCert,
            ResignCommitteeColdCert => CertificateType.ResignCommitteeColdCert,
            RegDrepCert => CertificateType.RegDrepCert,
            UnRegDrepCert => CertificateType.UnregDrepCert,
            UpdateDrepCert => CertificateType.UpdateDrepCert,
            _ => throw new ArgumentOutOfRangeException($"Unknown certificate type: {certificate}")
        };
    }

    public static string GetCertificateTypeName(this Certificate certificate)
    {
        return certificate.GetCertificateType() switch
        {
            CertificateType.StakeRegistration => "Stake Registration",
            CertificateType.StakeDeregistration => "Stake Deregistration",
            CertificateType.StakeDelegation => "Stake Delegation",
            CertificateType.PoolRegistration => "Pool Registration",
            CertificateType.PoolRetirement => "Pool Retirement",
            CertificateType.RegCert => "Registration Certificate",
            CertificateType.UnregCert => "Unregistration Certificate",
            CertificateType.VoteDelegCert => "Vote Delegation Certificate",
            CertificateType.StakeVoteDelegCert => "Stake Vote Delegation Certificate",
            CertificateType.StakeRegDelegCert => "Stake Registration Delegation Certificate",
            CertificateType.VoteRegDelegCert => "Vote Registration Delegation Certificate",
            CertificateType.StakeVoteRegDelegCert => "Stake Vote Registration Delegation Certificate",
            CertificateType.AuthCommitteeHotCert => "Authorize Committee Hot Certificate",
            CertificateType.ResignCommitteeColdCert => "Resign Committee Cold Certificate",
            CertificateType.RegDrepCert => "Register DRep Certificate",
            CertificateType.UnregDrepCert => "Unregister DRep Certificate",
            CertificateType.UpdateDrepCert => "Update DRep Certificate",
            _ => "Unknown Certificate Type"
        };
    }

    public static bool IsStakeRelated(this Certificate certificate)
    {
        CertificateType certType = certificate.GetCertificateType();
        return certType is
            CertificateType.StakeRegistration or
            CertificateType.StakeDeregistration or
            CertificateType.StakeDelegation or
            CertificateType.RegCert or
            CertificateType.UnregCert or
            CertificateType.StakeVoteDelegCert or
            CertificateType.StakeRegDelegCert or
            CertificateType.StakeVoteRegDelegCert;
    }

    public static bool IsPoolRelated(this Certificate certificate)
    {
        CertificateType certType = certificate.GetCertificateType();
        return certType is
            CertificateType.PoolRegistration or
            CertificateType.PoolRetirement;
    }

    public static bool IsVoteRelated(this Certificate certificate)
    {
        CertificateType certType = certificate.GetCertificateType();
        return certType is
            CertificateType.VoteDelegCert or
            CertificateType.StakeVoteDelegCert or
            CertificateType.VoteRegDelegCert or
            CertificateType.StakeVoteRegDelegCert or
            CertificateType.AuthCommitteeHotCert or
            CertificateType.ResignCommitteeColdCert or
            CertificateType.RegDrepCert or
            CertificateType.UnregDrepCert or
            CertificateType.UpdateDrepCert;
    }

    public static string? GetPoolId(this Certificate certificate)
    {
        try
        {
            return certificate switch
            {
                StakeDelegation stakeDel => stakeDel.PoolKeyHash != null
                    ? Convert.ToHexStringLower(stakeDel.PoolKeyHash!)
                    : null,
                StakeVoteDelegCert stakeVoteDel => stakeVoteDel.PoolKeyHash != null
                    ? Convert.ToHexStringLower(stakeVoteDel.PoolKeyHash)
                    : null,
                StakeRegDelegCert stakeRegDel => stakeRegDel.PoolKeyHash != null
                    ? Convert.ToHexStringLower(stakeRegDel.PoolKeyHash)
                    : null,
                StakeVoteRegDelegCert stakeVoteRegDel => stakeVoteRegDel.PoolKeyHash != null
                    ? Convert.ToHexStringLower(stakeVoteRegDel.PoolKeyHash).ToLower()
                    : null,
                PoolRetirement poolRet => poolRet.PoolKeyHash != null
                    ? Convert.ToHexStringLower(poolRet.PoolKeyHash)
                    : null,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
}