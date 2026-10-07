using Microsoft.EntityFrameworkCore;
using WebApplication3.Application.Common.Abstractions;
using WebApplication3.Domain;

namespace WebApplication3.Infrastructure
{
    public class AppDbContext : DbContext, IUnitOfWork
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderItem> OrderItems => Set<OrderItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 1. Простые сущности (Customer, Product) — из JSON
            JsonSimpleConfigurator.Apply(modelBuilder, ResolveJsonPath(), typeof(Order).Assembly);

            // 2. Сложные (Order + OrderItem) — вручную
            ConfigureOrder(modelBuilder);

            base.OnModelCreating(modelBuilder);
        }

        // Файл копируется в bin/.../Infrastructure/ (см. .csproj). Если его нет — падаем явно,
        // а не молча пропускаем настройку.
        private static string ResolveJsonPath()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "entity-config.json"),
                Path.Combine(AppContext.BaseDirectory, "Infrastructure", "entity-config.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "Infrastructure", "entity-config.json")
            };

            return candidates.FirstOrDefault(File.Exists)
                ?? throw new FileNotFoundException(
                    "entity-config.json не найден. Проверьте, что файл лежит в Infrastructure/ " +
                    "и в .csproj для него задан CopyToOutputDirectory.",
                    candidates[1]);
        }

        private static void ConfigureOrder(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OrderItem>(i =>
            {
                i.ToTable("OrderItems");
                i.HasKey(x => x.Id);
                i.Property(x => x.ProductId).IsRequired();
                i.Property(x => x.Name).IsRequired().HasMaxLength(200);
                i.Property(x => x.Price).HasColumnType("decimal(18,2)").IsRequired();
                i.Property(x => x.Quantity).IsRequired();
            });

            modelBuilder.Entity<Order>(b =>
            {
                b.ToTable("Orders");
                b.HasKey(o => o.Id);
                b.Property(o => o.CustomerEmail).IsRequired().HasMaxLength(256);
                b.Property(o => o.Status).HasConversion<int>().IsRequired();
                b.Property(o => o.Total).HasColumnType("decimal(18,2)").IsRequired();
                b.Property(o => o.CreatedAtUtc).IsRequired();
                b.HasIndex(o => o.CustomerEmail);

                // Order 1 -> * OrderItem, внешний ключ OrderId (теневое свойство)
                b.HasMany(o => o.Items)
                    .WithOne()
                    .HasForeignKey("OrderId")
                    .IsRequired()
                    .OnDelete(DeleteBehavior.Cascade);

                // Коллекция только для чтения снаружи, EF пишет в поле _items
                b.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
            });
        }
    }
}
