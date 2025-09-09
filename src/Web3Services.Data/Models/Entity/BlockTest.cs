using Argus.Sync.Data.Models;

namespace Web3Services.Data.Models.Entity;

public record BlockTest(string Hash, ulong Height, ulong Slot, DateTime CreatedAt) : IReducerModel;