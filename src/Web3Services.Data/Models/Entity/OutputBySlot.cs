using Argus.Sync.Data.Models;

namespace Web3Services.Data.Models.Entity;

public record OutputBySlot(
    string OutRef,
    ulong Slot,
    string SpentTxHash,
    ulong? SpentSlot,
    string PaymentKeyHash,
    string StakeKeyHash,
    byte[] Raw
) : IReducerModel;