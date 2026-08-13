using Microsoft.EntityFrameworkCore.Infrastructure;

namespace dotnetrpg_again.Data;

public sealed class ProviderModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        return (context.GetType(), context.Database.ProviderName, designTime);
    }
}
