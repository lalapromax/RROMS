//using Microsoft.AspNetCore.Identity;
//using Microsoft.EntityFrameworkCore;
//using RROMS.Models;
//using RROMS.Presentation;

//namespace RROMS.Data;

///// 
///// Runs once at startup (see Program.cs). Applies any pending EF Core migrations,
///// then seeds roles, two demo accounts, and enough reference data (dining areas,
///// tables, menu, operating hours, stock answers) for the app to be usable immediately after
///// `dotnet run` without any manual data entry.
///// 
//public static class SeedData
//{
//    public static async Task InitializeAsync(IServiceProvider services)
//    {
//        var context = services.GetRequiredService<ApplicationDbContext>();
//        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
//        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
//        var config = services.GetRequiredService<IConfiguration>();
//        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("SeedData");

//        await context.Database.MigrateAsync();

//        // ---------------- Roles ----------------
//        foreach (var role in Roles.All)
//        {
//            if (!await roleManager.RoleExistsAsync(role))
//                await roleManager.CreateAsync(new IdentityRole(role));
//        }

//        // ---------------- Admin account ----------------
//        var adminEmail = config["RestaurantSettings:SeedAdminEmail"] ?? "admin@rroms.local";
//        var adminPassword = config["RestaurantSettings:SeedAdminPassword"] ?? "Admin@12345";
//        var admin = await EnsureUserAsync(userManager, adminEmail, adminPassword, "System Administrator", Roles.Administrator);

//        // ---------------- Staff account ----------------
//        var staffEmail = config["RestaurantSettings:SeedStaffEmail"] ?? "staff@rroms.local";
//        var staffPassword = config["RestaurantSettings:SeedStaffPassword"] ?? "Staff@12345";
//        await EnsureUserAsync(userManager, staffEmail, staffPassword, "Front of House Staff", Roles.Staff);

//        // ---------------- Demo customer account ----------------
//        await EnsureUserAsync(userManager, "customer@rroms.local", "Customer@12345", "Demo Customer", Roles.Customer);

//        // Bookings and orders taken at the door or by phone are filed under one shared account that nobody can sign in to.
//        if (await userManager.FindByEmailAsync(ApplicationUser.WalkInEmail) == null)
//        {
//            var walkIn = new ApplicationUser
//            {
//                UserName = ApplicationUser.WalkInEmail, Email = ApplicationUser.WalkInEmail, EmailConfirmed = true,
//                FullName = "Walk-in guest", IsActive = false, ShowOnLeaderboard = false,
//                NoticeAnnouncements = false, NoticeRewards = false, NoticeOffers = false, NoticeByEmail = false
//            };
//            if ((await userManager.CreateAsync(walkIn)).Succeeded)
//                await userManager.AddToRoleAsync(walkIn, Roles.Customer);
//        }

//        // ---------------- Restaurant setting (single row) ----------------
//        // The restaurant's name is fixed in code (Brand.Name). Nobody can change it from the site; the column only keeps
//        // it in step so reports and exports that read the table show the same name.
//        var setting = await context.RestaurantSettings.FirstOrDefaultAsync();
//        if (setting == null)
//        {
//            context.RestaurantSettings.Add(new RestaurantSetting
//            {
//                RestaurantName = Brand.Name,
//                Address = "[YOUR ADDRESS]",
//                Phone = "[YOUR PHONE]",
//                Email = null,
//                Description = "Grandeur dining, crafted wine and Michelin-rated cuisine.",
//                ReservationDurationMinutes = 105,
//                MaxAdvanceBookingDays = 60,
//                TaxRatePercent = 8.0m,
//                ServiceChargePercent = 10.0m,
//                HoldMinutes = 15
//            });
//            await context.SaveChangesAsync();
//        }
//        else if (setting.RestaurantName != Brand.Name)
//        {
//            setting.RestaurantName = Brand.Name;
//            await context.SaveChangesAsync();
//        }

//        // ---------------- Operating hours ----------------
//        if (!await context.OperatingHours.AnyAsync())
//        {
//            var hours = new List<OperatingHours>();
//            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
//            {
//                var closes = day switch
//                {
//                    DayOfWeek.Friday or DayOfWeek.Saturday => new TimeSpan(23, 30, 0),
//                    DayOfWeek.Sunday => new TimeSpan(21, 30, 0),
//                    _ => new TimeSpan(22, 30, 0)
//                };
//                hours.Add(new OperatingHours
//                {
//                    DayOfWeek = day,
//                    OpenTime = new TimeSpan(12, 0, 0),
//                    CloseTime = closes,
//                    IsClosed = day == DayOfWeek.Monday
//                });
//            }
//            context.OperatingHours.AddRange(hours);
//            await context.SaveChangesAsync();
//        }

//        // ---------------- Dining areas + tables ----------------
//        if (!await context.DiningAreas.AnyAsync())
//        {
//            var hall = new DiningArea { Name = "Dining Room", Description = "The main room, under the chandeliers." };
//            var terrace = new DiningArea { Name = "Terrace", Description = "Open-air seating, weather permitting." };
//            var salon = new DiningArea { Name = "Private Salon", Description = "A private room for larger parties." };
//            context.DiningAreas.AddRange(hall, terrace, salon);
//            await context.SaveChangesAsync();

//            var tables = new List<RestaurantTable>
//            {
//                new() { TableNumber = "T1", DiningAreaId = hall.Id, Capacity = 2 },
//                new() { TableNumber = "T2", DiningAreaId = hall.Id, Capacity = 2 },
//                new() { TableNumber = "T3", DiningAreaId = hall.Id, Capacity = 4 },
//                new() { TableNumber = "T4", DiningAreaId = hall.Id, Capacity = 4 },
//                new() { TableNumber = "T5", DiningAreaId = hall.Id, Capacity = 6 },
//                new() { TableNumber = "T6", DiningAreaId = terrace.Id, Capacity = 4 },
//                new() { TableNumber = "T7", DiningAreaId = terrace.Id, Capacity = 4 },
//                new() { TableNumber = "T8", DiningAreaId = terrace.Id, Capacity = 2 },
//                new() { TableNumber = "T9", DiningAreaId = salon.Id, Capacity = 10 },
//            };
//            context.RestaurantTables.AddRange(tables);
//            await context.SaveChangesAsync();
//        }

//        // ---------------- Menu categories + items ----------------
//        if (!await context.MenuCategories.AnyAsync())
//        {
//            var soups = new MenuCategory { Name = "Soups", DisplayOrder = 1 };
//            var appetizers = new MenuCategory { Name = "Appetizers", DisplayOrder = 2 };
//            var mains = new MenuCategory { Name = "Mains", DisplayOrder = 3 };
//            var desserts = new MenuCategory { Name = "Desserts", DisplayOrder = 4 };
//            var glasses = new MenuCategory { Name = "With set menus", Description = "Drinks poured only as part of a set menu.", DisplayOrder = 5, SetMenuOnly = true };
//            var cellar = new MenuCategory { Name = "Wine", Description = "The cellar: by the glass or by the bottle.", DisplayOrder = 6, IsWine = true };
//            context.MenuCategories.AddRange(soups, appetizers, mains, desserts, glasses, cellar);
//            await context.SaveChangesAsync();

//            MenuItem Dish(string name, string desc, decimal price, MenuCategory cat, string allergens, string? diet = null, bool veg = false, int? kcal = null, bool pick = false) =>
//                new() { Name = name, Description = desc, Price = price, MenuCategoryId = cat.Id, Allergens = allergens, DietaryTags = diet, IsVegetarian = veg, Calories = kcal, IsChefsPick = pick };
//            MenuItem Wine(string name, string note, decimal glass, decimal bottle, string? diet = null) =>
//                new() { Name = name, WineNote = note, Description = note, Price = bottle, GlassPrice = glass, MenuCategoryId = cellar.Id, Allergens = "Sulphites", DietaryTags = diet };

//            var items = new List<MenuItem>
//            {
//                Dish("Velouté de champignons", "Wild mushroom soup with truffle cream.", 38m, soups, "Milk", "Gluten-Free", veg: true, kcal: 260),
//                Dish("Consommé de bœuf", "Clear beef broth with root vegetables.", 36m, soups, "", "Gluten-Free,Dairy-Free", kcal: 120),

//                Dish("Tartare de bœuf", "Hand-cut beef, shallot, cornichon and egg yolk, with toasted brioche.", 58m, appetizers, "Eggs,Gluten", "Dairy-Free", kcal: 340, pick: true),
//                Dish("Salade de chèvre chaud", "Warm goat cheese, walnut and honey.", 46m, appetizers, "Milk,Tree nuts", veg: true, kcal: 380),

//                Dish("Magret de canard", "Duck breast roasted pink, glazed shallots, celeriac and a red wine sauce.", 118m, mains, "Sulphites", "Gluten-Free,Dairy-Free", kcal: 640, pick: true),
//                Dish("Sole meunière", "Pan-fried sole in brown butter with lemon.", 124m, mains, "Fish,Milk,Gluten", kcal: 560),
//                Dish("Gnocchi à la truffe", "Potato gnocchi in a black truffle cream.", 92m, mains, "Gluten,Milk", veg: true, kcal: 610),
//                Dish("Poulet rôti, jus à l'estragon", "Roast chicken with a tarragon jus and pommes purée.", 86m, mains, "Milk", kcal: 690),

//                Dish("Tarte Tatin", "Caramelised apple, crème fraîche.", 42m, desserts, "Gluten,Milk", veg: true, kcal: 450),
//                Dish("Crème brûlée à la vanille", "Vanilla custard under a thin crust of burnt sugar.", 38m, desserts, "Milk,Eggs", veg: true, kcal: 410),

//                Dish("A glass of the house red", "Côtes du Rhône, poured by the glass.", 42m, glasses, "Sulphites", "Vegan"),
//                Dish("A glass of the house white", "Sauvignon blanc from the Loire, poured by the glass.", 42m, glasses, "Sulphites", "Vegan"),
//                Dish("A glass of sparkling cider", "Dry, cold and non-alcoholic: a brut cider pressed in Normandy.", 24m, glasses, "", "Vegan"),

//                Wine("Chablis, Premier Cru", "Burgundy, white", 46m, 218m, "Vegan"),
//                Wine("Pouilly-Fumé", "Loire, white", 38m, 178m, "Vegan"),
//                Wine("Blanc de Blancs, Brut", "Champagne, sparkling", 58m, 340m, "Vegan"),
//                Wine("Saint-Émilion, Grand Cru Classé", "Bordeaux, red", 62m, 298m, "Vegan"),
//                Wine("House red", "Côtes du Rhône, red", 32m, 148m, "Vegan"),
//                Wine("Crémant d'Alsace", "Alsace, sparkling", 36m, 168m, "Vegan"),
//            };
//            context.MenuItems.AddRange(items);
//            await context.SaveChangesAsync();

//            MenuItem By(string name) => items.First(i => i.Name == name);

//            context.Promotions.Add(new Promotion
//            {
//                Title = "Magret du dimanche",
//                Description = "10% off the Magret de canard, Sunday to Thursday.",
//                DiscountPercent = 10,
//                MenuItemId = By("Magret de canard").Id,
//                StartDate = DateTime.UtcNow.Date,
//                EndDate = DateTime.UtcNow.Date.AddDays(30)
//            });
//            await context.SaveChangesAsync();

//            // ---- Set menus ----
//            SetMenuCourse Course(string name, int order, params (string Dish, decimal Extra)[] dishes) => new()
//            {
//                Name = name,
//                Picks = 1,
//                SortOrder = order,
//                Dishes = dishes.Select((d, i) => new SetMenuDish { MenuItemId = By(d.Dish).Id, Supplement = d.Extra, SortOrder = i }).ToList()
//            };
//            context.SetMenus.AddRange(
//                new SetMenu
//                {
//                    Name = "Menu du Soir", Tagline = "Soup, appetiser, main and a glass", Description = "Four courses, one price. Choose one dish in each course.",
//                    PricePerGuest = 238m, Status = SetMenuStatus.Active, DaysMask = SetMenu.MaskOf(new[] { DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Tuesday }),
//                    ServedAtDinner = true, SortOrder = 1,
//                    Courses =
//                    {
//                        Course("Soup", 1, ("Velouté de champignons", 0), ("Consommé de bœuf", 0)),
//                        Course("Appetizer", 2, ("Tartare de bœuf", 0), ("Salade de chèvre chaud", 0)),
//                        Course("Main", 3, ("Magret de canard", 0), ("Sole meunière", 22m)),
//                        Course("Drink", 4, ("A glass of the house red", 0), ("A glass of the house white", 0))
//                    }
//                },
//                new SetMenu
//                {
//                    Name = "Menu Déjeuner", Tagline = "Three courses, served at lunch", Description = "Soup, a main and a glass, weekdays at lunch.",
//                    PricePerGuest = 128m, Status = SetMenuStatus.Active, DaysMask = SetMenu.MaskOf(new[] { DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }),
//                    ServedAtLunch = true, ServedAtDinner = false, SortOrder = 2,
//                    Courses =
//                    {
//                        Course("Soup", 1, ("Consommé de bœuf", 0)),
//                        Course("Main", 2, ("Poulet rôti, jus à l'estragon", 0)),
//                        Course("Drink", 3, ("A glass of the house white", 0))
//                    }
//                },
//                new SetMenu
//                {
//                    Name = "Menu Végétal", Tagline = "Four courses, no meat or fish", Description = "A vegetarian tasting of the season.",
//                    PricePerGuest = 198m, Status = SetMenuStatus.Active, ServedAtLunch = true, ServedAtDinner = true, SortOrder = 3,
//                    Courses =
//                    {
//                        Course("Soup", 1, ("Velouté de champignons", 0)),
//                        Course("Appetizer", 2, ("Salade de chèvre chaud", 0)),
//                        Course("Main", 3, ("Gnocchi à la truffe", 0)),
//                        Course("Drink", 4, ("A glass of sparkling cider", 0))
//                    }
//                },
//                new SetMenu
//                {
//                    Name = "Menu Dégustation", Tagline = "Seven courses with wine pairing", Description = "The kitchen's tasting menu. Draft until the courses are final.",
//                    PricePerGuest = 398m, Status = SetMenuStatus.Draft, SortOrder = 4,
//                    Courses =
//                    {
//                        Course("Soup", 1, ("Consommé de bœuf", 0)),
//                        Course("Appetizer", 2, ("Tartare de bœuf", 0)),
//                        Course("Main", 3, ("Sole meunière", 0)),
//                        Course("Dessert", 4, ("Tarte Tatin", 0))
//                    }
//                },
//                new SetMenu
//                {
//                    Name = "Menu Enfant", Tagline = "Three small courses, no wine", Description = "For guests under twelve.",
//                    PricePerGuest = 68m, Status = SetMenuStatus.Active, ServedAtLunch = true, ServedAtDinner = true, SortOrder = 5,
//                    Courses =
//                    {
//                        Course("Soup", 1, ("Velouté de champignons", 0)),
//                        Course("Main", 2, ("Gnocchi à la truffe", 0)),
//                        Course("Dessert", 3, ("Crème brûlée à la vanille", 0))
//                    }
//                });
//            await context.SaveChangesAsync();
//        }

//        // ---------------- Announcement ----------------
//        if (!await context.Announcements.AnyAsync())
//        {
//            context.Announcements.Add(new Announcement
//            {
//                Title = "Welcome to our new online booking system",
//                Content = "You can now reserve a table, order ahead, and track your visit history all in one place. We look forward to welcoming you.",
//                CreatedAt = DateTime.UtcNow,
//                ExpiresAt = DateTime.UtcNow.AddDays(60),
//                IsActive = true,
//                CreatedByRole = Roles.Administrator,
//                CreatedByAdminId = admin?.Id
//            });
//            await context.SaveChangesAsync();
//        }

//        // ---------------- Collaborations (sample entries so the home page strip is visible out of the box) ----------------
//        // These are placeholders, not real partners: no links, no images, and each says so. Replace or remove them in
//        // Admin > Collaborations. They are added on a fresh install only - the FAQ table below is still empty exactly
//        // once, so an existing site upgrading to this version gets no sample partners, and an administrator who deletes
//        // them all does not see them come back on the next restart.
//        if (!await context.Collaborations.AnyAsync() && !await context.ChatbotFaqs.AnyAsync())
//        {
//            var year = DateTime.UtcNow.Year;
//            context.Collaborations.AddRange(
//                new Collaboration { Name = "Sample partner: local farm", Summary = "Placeholder - replace with a real supplier or partner in Admin > Collaborations.", IsOngoing = true, Year = year, DisplayOrder = 1, IsPublished = true },
//                new Collaboration { Name = "Sample partner: guest chef", Summary = "Placeholder - a past pop-up or guest-chef night would go here.", IsOngoing = false, Year = year - 1, DisplayOrder = 2, IsPublished = true },
//                new Collaboration { Name = "Sample partner: coffee roaster", Summary = "Placeholder - add a logo, website, or an Instagram reel link.", IsOngoing = true, Year = year, DisplayOrder = 3, IsPublished = true },
//                new Collaboration { Name = "Sample partner: charity dinner", Summary = "Placeholder - past community events and fundraisers fit here too.", IsOngoing = false, Year = year - 1, DisplayOrder = 4, IsPublished = true });
//            await context.SaveChangesAsync();
//        }

//        // ---------------- Chat stock answers ----------------
//        if (!await context.ChatbotFaqs.AnyAsync())
//        {
//            context.ChatbotFaqs.AddRange(
//                new ChatbotFaq { Question = "What are your opening hours?", Keywords = "hours,open,close,opening,closing,time", Answer = "Our opening hours are listed at the bottom of every page, and I can tell you today's if you ask. The kitchen takes its last order shortly before closing.", Category = "Hours" },
//                new ChatbotFaq { Question = "How do I book a table?", Keywords = "reservation,book,booking,table,reserve", Answer = "Choose Reserve a table at the top of any page, pick the day, the time and how many of you are coming, and we will show the areas of the room that are free at that moment. A member of the team confirms the booking and you will get a notice when they do.", Category = "Reservations" },
//                new ChatbotFaq { Question = "Can I cancel my booking?", Keywords = "cancel,change,modify,reschedule,edit reservation", Answer = "Yes. Open My reservations and choose Cancel. Closer to the time than the booking page allows, please call us and we will do what we can. To move a booking, cancel it and book the new time.", Category = "Reservations" },
//                new ChatbotFaq { Question = "Do you take walk-ins?", Keywords = "walk-in,walkin,without reservation,no booking", Answer = "When a table is free we are glad to seat you, but booking ahead is the only way to be sure, especially at the weekend.", Category = "Reservations" },
//                new ChatbotFaq { Question = "How do I order food?", Keywords = "order,menu,food,dishes,pre-order", Answer = "Book a table first, then add dishes, wines or a set menu from the Menu and check out for that booking. The kitchen starts when you are seated. You can follow it under My orders.", Category = "Orders" },
//                new ChatbotFaq { Question = "What payment methods do you accept?", Keywords = "pay,payment,card,cash,online payment,fpx,banking", Answer = "At the restaurant you can pay by cash or card. Online you can pay by card or by online banking. Your receipt appears under My orders once the payment is confirmed.", Category = "Payments" },
//                new ChatbotFaq { Question = "Do you have vegetarian dishes?", Keywords = "vegetarian,vegan,plant based,veggie", Answer = "Yes. Vegetarian dishes carry a mark on the Menu. For allergies, please tell us when you book and tell your server: the team always has the last word.", Category = "Menu" },
//                new ChatbotFaq { Question = "How do I leave a review?", Keywords = "feedback,review,rating,complaint,compliment", Answer = "Once an order or visit is finished, open My reviews and choose Write a review. You rate the food, service, cleanliness, wait and value, and you can add a few words and photos. Each order or visit can be reviewed once, and you can change your review for a week.", Category = "Feedback" }
//            );
//            await context.SaveChangesAsync();
//        }

//        // ---------------- Reward tiers + perks ----------------
//        // Created once. After that, Admin > Rewards owns them: the thresholds come from RewardSettings only for this first creation.
//        // In the messages, {points} stands for the tier's own threshold so the wording stays right when an administrator moves it.
//        if (!await context.RewardTiers.AnyAsync())
//        {
//            var silverPoints = Math.Max(1, config.GetValue<int?>("RewardSettings:SilverPoints") ?? 500);
//            var goldPoints = Math.Max(silverPoints + 1, config.GetValue<int?>("RewardSettings:GoldPoints") ?? 2000);
//            var bronze = new RewardTier
//            {
//                Name = "Bronze", MinPoints = 0, Blurb = "Everyone starts here", ColorHex = "#84592B",
//                MessagePassed = "You have passed Bronze. Thank you for the first visits.",
//                MessageCurrent = "Welcome to Bronze. Your first perks are already yours.",
//                MessageLocked = "Everyone starts here."
//            };
//            var silver = new RewardTier
//            {
//                Name = "Silver", MinPoints = silverPoints, Blurb = "A regular", ColorHex = "#5C5636",
//                MessagePassed = "You have passed Silver. A regular, and we know it.",
//                MessageCurrent = "A regular, and we know it. Enjoy the Silver perks.",
//                MessageLocked = "Silver starts at {points} points."
//            };
//            var gold = new RewardTier
//            {
//                Name = "Gold", MinPoints = goldPoints, Blurb = "Our most trusted table", ColorHex = "#743014",
//                MessagePassed = "Our most trusted table. Thank you.",
//                MessageCurrent = "Our most trusted table. Enjoy every perk.",
//                MessageLocked = "Gold starts at {points} points."
//            };
//            context.RewardTiers.AddRange(bronze, silver, gold);
//            await context.SaveChangesAsync();

//            if (!await context.RewardPerks.AnyAsync())
//            {
//                const string welcome = "Welcome treats", every = "Every visit", table = "At the table";
//                context.RewardPerks.AddRange(
//                    new RewardPerk { RewardTierId = bronze.Id, SortOrder = 1, Group = welcome, Title = "Birthday dessert", Description = "A dessert on us in your birthday month.", Icon = "cake", Frequency = PerkFrequency.Yearly },
//                    new RewardPerk { RewardTierId = bronze.Id, SortOrder = 2, Group = welcome, Title = "First look at new set menus", Description = "Before they open to everyone", Icon = "book", Frequency = PerkFrequency.Always },
//                    new RewardPerk { RewardTierId = bronze.Id, SortOrder = 3, Group = every, Title = "Points on every order", Description = "1 point for every RM 1 you spend", Icon = "star", Frequency = PerkFrequency.Always },

//                    new RewardPerk { RewardTierId = silver.Id, SortOrder = 1, Group = welcome, Title = "A welcome glass on each visit", Description = "A glass of the house wine when you sit down.", Icon = "glass", Frequency = PerkFrequency.Monthly },
//                    new RewardPerk { RewardTierId = silver.Id, SortOrder = 2, Group = welcome, Title = "Birthday glass of champagne", Description = "A glass of champagne in your birthday month.", Icon = "cake", Frequency = PerkFrequency.Yearly },
//                    new RewardPerk { RewardTierId = silver.Id, SortOrder = 3, Group = table, Title = "Priority table requests", Description = "We look at yours first", Icon = "clock", Frequency = PerkFrequency.Always },

//                    new RewardPerk { RewardTierId = gold.Id, SortOrder = 1, Group = welcome, Title = "Wine pairing on set menus", Description = "A glass chosen to go with each course.", Icon = "bottle", Frequency = PerkFrequency.Monthly },
//                    new RewardPerk { RewardTierId = gold.Id, SortOrder = 2, Group = table, Title = "Chef's table invitations", Description = "An evening at the pass with the kitchen.", Icon = "crown", Frequency = PerkFrequency.Yearly },
//                    new RewardPerk { RewardTierId = gold.Id, SortOrder = 3, Group = table, Title = "A table held until 30 minutes late", Description = "No need to rush the traffic", Icon = "clock", Frequency = PerkFrequency.Always });
//                await context.SaveChangesAsync();
//            }
//        }

//        // ---------------- Ranking bands ----------------
//        // The kind message a guest sees for where they stand. Edited in Admin > Rewards > Ranking and messages.
//        if (!await context.RankBands.AnyAsync())
//        {
//            context.RankBands.AddRange(RankBandDefaults.Create());
//            await context.SaveChangesAsync();
//        }

//        logger.LogInformation("Database seeding completed.");
//    }

//    private static async Task<ApplicationUser?> EnsureUserAsync(
//        UserManager<ApplicationUser> userManager,
//        string email,
//        string password,
//        string fullName,
//        string role)
//    {
//        var existing = await userManager.FindByEmailAsync(email);
//        if (existing != null)
//            return existing;

//        var user = new ApplicationUser
//        {
//            UserName = email,
//            Email = email,
//            EmailConfirmed = true,
//            FullName = fullName,
//            IsActive = true,
//            DateJoined = DateTime.UtcNow
//        };

//        var result = await userManager.CreateAsync(user, password);
//        if (result.Succeeded)
//        {
//            await userManager.AddToRoleAsync(user, role);
//            return user;
//        }

//        return null;
//    }
//}
