
namespace dotnetrpg_again.Data;

public class DataContext : DbContext
{
    public DataContext(DbContextOptions<DataContext> options) : base(options)
    {
    }


    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Automatically converts all standard and nullable enums to strings globally
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
    }
}
