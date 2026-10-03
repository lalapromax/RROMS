using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RROMS.Models;

namespace RROMS.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<DiningArea> DiningAreas => Set<DiningArea>();
        public DbSet<RestaurantSetting> RestaurantSettings => Set<RestaurantSetting>();
        public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();
        public DbSet<MenuItem> MenuItems => Set<MenuItem>();

        public DbSet<Reservation> Reservations => Set<Reservation>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);


            // ---- MenuItem ----
            builder.Entity<MenuItem>()
                .HasOne(m => m.MenuCategory)
                .WithMany(c => c.MenuItems)
                .HasForeignKey(m => m.MenuCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

        }
    }
}


