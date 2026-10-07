using WebApplication3.Domain;

namespace WebApplication3.Infrastructure
{
    public static class DbSeeder
    {
        public static void Seed(AppDbContext db)
        {
            if (!db.Customers.Any())
            {
                var blocked = new Customer("blocked@example.com", "Заблокированный клиент");
                blocked.Block();

                db.Customers.Add(new Customer("ivan@example.com", "Иван Иванов"));
                db.Customers.Add(blocked);
                db.SaveChanges();
            }

            if (!db.Products.Any())
            {
                db.Products.AddRange(
                    new Product("Клавиатура", 2500m, 10),
                    new Product("Мышь", 1200m, 25),
                    new Product("Монитор 27\"", 18900m, 3));
                db.SaveChanges();
            }
        }
    }
}
