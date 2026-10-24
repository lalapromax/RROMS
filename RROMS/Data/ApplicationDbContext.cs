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
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<Announcement> Announcements => Set<Announcement>();
        public DbSet<Reservation> Reservations => Set<Reservation>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderItem> OrderItems => Set<OrderItem>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<Feedback> Feedbacks => Set<Feedback>();
        public DbSet<Collaboration> Collaborations => Set<Collaboration>();
        public DbSet<RewardTier> RewardTiers => Set<RewardTier>();
        public DbSet<SetMenu> SetMenus => Set<SetMenu>();
        public DbSet<SetMenuCourse> SetMenuCourses => Set<SetMenuCourse>();
        public DbSet<RankBand> RankBands => Set<RankBand>();
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

            // ---- Notification ----
            builder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // ---- Announcement ----
            builder.Entity<Announcement>()
                .HasOne(a => a.CreatedByAdmin)
                .WithMany()
                .HasForeignKey(a => a.CreatedByAdminId)
                .OnDelete(DeleteBehavior.SetNull);

            // ---- Promotion ----
            builder.Entity<Promotion>()
                .HasOne(p => p.MenuItem)
                .WithMany(m => m.Promotions)
                .HasForeignKey(p => p.MenuItemId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Promotion>()
                .HasOne(p => p.MenuCategory)
                .WithMany()
                .HasForeignKey(p => p.MenuCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // ---- PromotionRedemption ----
            builder.Entity<PromotionRedemption>()
                .HasOne(pr => pr.Promotion)
                .WithMany(p => p.Redemptions)
                .HasForeignKey(pr => pr.PromotionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<PromotionRedemption>()
                .HasOne(pr => pr.Order)
                .WithMany(o => o.PromotionRedemptions)
                .HasForeignKey(pr => pr.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PromotionRedemption>()
                .HasOne(pr => pr.Customer)
                .WithMany()
                .HasForeignKey(pr => pr.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

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

            // ---- Reward tiers, perks and claims ----
            builder.Entity<RewardTier>().HasIndex(t => t.MinPoints).IsUnique();

            builder.Entity<RewardPerk>()
                .HasOne(p => p.Tier)
                .WithMany(t => t.Perks)
                .HasForeignKey(p => p.RewardTierId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<RewardClaim>()
                .HasOne(c => c.Perk)
                .WithMany()
                .HasForeignKey(c => c.RewardPerkId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<RewardClaim>()
                .HasOne(c => c.Customer)
                .WithMany()
                .HasForeignKey(c => c.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<RewardClaim>().HasIndex(c => new { c.Status, c.CreatedAt });
            builder.Entity<RewardClaim>().HasIndex(c => new { c.CustomerId, c.RewardPerkId });

            // ---- RewardLedgerEntry ----
            builder.Entity<RewardLedgerEntry>()
                .HasOne(r => r.Customer)
                .WithMany()
                .HasForeignKey(r => r.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<RewardLedgerEntry>()
                .HasOne(r => r.Order)
                .WithMany()
                .HasForeignKey(r => r.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // One award per order, enforced by the database itself.
            builder.Entity<RewardLedgerEntry>().HasIndex(r => r.OrderId).IsUnique();
            builder.Entity<RewardLedgerEntry>().HasIndex(r => r.CustomerId);


            // ---- Feedback: one review per order / per dining experience ----
            // Filtered unique indexes: many reviews may have a null OrderId or ReservationId, but a
            // given order or reservation can only ever be reviewed once, even if the form is
            // double-submitted or two browser tabs race each other.

            builder.Entity<Feedback>().HasIndex(f => f.OrderId).IsUnique().HasFilter("[OrderId] IS NOT NULL");
            builder.Entity<Feedback>().HasIndex(f => f.ReservationId).IsUnique().HasFilter("[ReservationId] IS NOT NULL");
            builder.Entity<Feedback>().HasIndex(f => f.CreatedAt);

            // ---- Waiting list ----
            builder.Entity<WaitlistEntry>()
                .HasOne(w => w.Customer)
                .WithMany()
                .HasForeignKey(w => w.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.Entity<WaitlistEntry>().HasIndex(w => new { w.WantedDateTime, w.NotifiedAt });

            // ---- Rank bands ----
            builder.Entity<RankBand>().HasIndex(b => b.UpToPercent).IsUnique();

            // ---- Indexes that matter for the availability engine & reporting ----
            builder.Entity<Reservation>().HasIndex(r => r.ReservationDateTime);
            builder.Entity<Order>().HasIndex(o => o.OrderDate);
            builder.Entity<MenuItem>().HasIndex(m => m.IsAvailable);
        }
    }   
}


