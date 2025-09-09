using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Types.Cardano.Core;
using Microsoft.EntityFrameworkCore;
using Web3Services.Data.Models;
using Web3Services.Data.Models.Entity;

namespace Web3Services.Sync.Reducers;

public class BlockTestReducer(
    IDbContextFactory<Web3ServicesDbContext> dbContextFactory
) 
// : IReducer<BlockTest>
{
    public async Task RollBackwardAsync(ulong slot)
    {
        await using Web3ServicesDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        await dbContext.BlockTests
            .Where(e => e.Slot >= slot)
            .ExecuteDeleteAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        string blockHash = block.Header().Hash();
        ulong blockNumber = block.Header().HeaderBody().BlockNumber();
        ulong slot = block.Header().HeaderBody().Slot();

        using Web3ServicesDbContext dbContext = dbContextFactory.CreateDbContext();
        dbContext.BlockTests.Add(new BlockTest(blockHash, blockNumber, slot, DateTime.UtcNow));

        await dbContext.SaveChangesAsync();
    }
}