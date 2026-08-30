using Microsoft.EntityFrameworkCore;

namespace EFCore.SchemaValidation.Tests;

public class FullModelDbContext : DbContext
{
    private readonly string _connectionString;

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Product> Products => Set<Product>();

    public FullModelDbContext(string connectionString) => _connectionString = connectionString;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(_connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(e =>
        {
            e.ToTable("Orders");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id);
            e.Property(x => x.CustomerName);
            e.Property(x => x.Total);
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.ToTable("Products");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id);
            e.Property(x => x.Name);
            e.Property(x => x.Price);
            e.Property(x => x.Quantity);
        });
    }
}

public class NoPrimaryKeyDbContext : DbContext
{
    private readonly string _connectionString;

    public DbSet<Order> Orders => Set<Order>();

    public NoPrimaryKeyDbContext(string connectionString) => _connectionString = connectionString;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(_connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(e =>
        {
            e.ToTable("Orders");
            e.HasNoKey();
            e.Ignore(x => x.CustomerName);
            e.Ignore(x => x.Total);
        });
    }
}

public class NoTableNameDbContext : DbContext
{
    private readonly string _connectionString;

    public DbSet<Order> Orders => Set<Order>();

    public NoTableNameDbContext(string connectionString) => _connectionString = connectionString;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(_connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(e =>
        {
            e.HasNoKey();
            e.Ignore(x => x.CustomerName);
            e.Ignore(x => x.Total);
        });
    }
}

public class Order
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}
