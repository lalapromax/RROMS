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
        public DbSet<RestaurantTable> RestaurantTables => Set<RestaurantTable>();
        public DbSet<OperatingHours> OperatingHours => Set<OperatingHours>();
        public DbSet<RestaurantSetting> RestaurantSettings => Set<RestaurantSetting>();
        public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();
        public DbSet<MenuItem> MenuItems => Set<MenuItem>();
        public DbSet<Announcement> Announcements => Set<Announcement>();
        public DbSet<Reservation> Reservations => Set<Reservation>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderItem> OrderItems => Set<OrderItem>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<Collaboration> Collaborations => Set<Collaboration>();
        public DbSet<SetMenu> SetMenus => Set<SetMenu>();
        public DbSet<SetMenuCourse> SetMenuCourses => Set<SetMenuCourse>();
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ---- RestaurantTable ----
            builder.Entity<RestaurantTable>()
                .HasOne(t => t.DiningArea)
                .WithMany(a => a.Tables)
                .HasForeignKey(t => t.DiningAreaId)
                .OnDelete(DeleteBehavior.Restrict);

            // ---- MenuItem ----
            builder.Entity<MenuItem>()
                .HasOne(m => m.MenuCategory)
                .WithMany(c => c.MenuItems)
                .HasForeignKey(m => m.MenuCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // ---- Announcement ----
            builder.Entity<Announcement>()
                .HasOne(a => a.CreatedByAdmin)
                .WithMany()
                .HasForeignKey(a => a.CreatedByAdminId)
                .OnDelete(DeleteBehavior.SetNull);

            // ---- Reservation ----
            builder.Entity<Reservation>()
                .HasOne(r => r.Customer)
                .WithMany(u => u.Reservations)
                .HasForeignKey(r => r.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Reservation>()
                .HasOne(r => r.Table)
                .WithMany(t => t.Reservations)
                .HasForeignKey(r => r.TableId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Reservation>()
                .HasOne(r => r.ConfirmedByStaff)
                .WithMany()
                .HasForeignKey(r => r.ConfirmedByStaffId)
                .OnDelete(DeleteBehavior.Restrict);

            // ---- Order ----
            builder.Entity<Order>()
                .HasOne(o => o.Customer)
                .WithMany(u => u.Orders)
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Order>()
                .HasOne(o => o.Reservation)
                .WithMany(r => r.Orders)
                .HasForeignKey(o => o.ReservationId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Order>()
                .HasOne(o => o.Table)
                .WithMany(t => t.Orders)
                .HasForeignKey(o => o.TableId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Order>()
                .HasOne(o => o.HandledByStaff)
                .WithMany()
                .HasForeignKey(o => o.HandledByStaffId)
                .OnDelete(DeleteBehavior.Restrict);

            // ---- OrderItem ----
            builder.Entity<OrderItem>()
                .HasOne(oi => oi.Order)
                .WithMany(o => o.OrderItems)
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<OrderItem>()
                .HasOne(oi => oi.MenuItem)
                .WithMany(m => m.OrderItems)
                .HasForeignKey(oi => oi.MenuItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // ---- Payment (1:1 with Order) ----
            builder.Entity<Payment>()
                .HasOne(p => p.Order)
                .WithOne(o => o.Payment)
                .HasForeignKey<Payment>(p => p.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // ---- Collaboration ----
            builder.Entity<Collaboration>().HasIndex(c => new { c.IsPublished, c.DisplayOrder });

            // ---- Set menus: courses and dishes go with the menu; a dish on the carte cannot be deleted while a set menu offers it ----
            builder.Entity<SetMenuCourse>()
                .HasOne(c => c.SetMenu)
                .WithMany(m => m.Courses)
                .HasForeignKey(c => c.SetMenuId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<SetMenuDish>()
                .HasOne(d => d.Course)
                .WithMany(c => c.Dishes)
                .HasForeignKey(d => d.SetMenuCourseId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<SetMenuDish>()
                .HasOne(d => d.MenuItem)
                .WithMany()
                .HasForeignKey(d => d.MenuItemId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SetMenu>().HasIndex(m => new { m.Status, m.SortOrder });

            builder.Entity<Reservation>()
                .HasOne(r => r.SetMenu)
                .WithMany()
                .HasForeignKey(r => r.SetMenuId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}


