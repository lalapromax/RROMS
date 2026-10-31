using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Runtime.CompilerServices;
using QuestPDF.Infrastructure;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// ---------------- Your private settings (API keys, passwords) ----------------
// appsettings.Local.json is an optional file that sits next to appsettings.json.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// ---------------- Database ----------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found in appsettings.json.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// ---------------- Identity ----------------
// Passwords are hashed with BCrypt. Hashes made by Identity's previous PBKDF2 hasher are still accepted and are
// re-saved as BCrypt the next time that person signs in (see BCryptPasswordHasher).
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, BCryptPasswordHasher>();

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // at least one uppercase, one
    // lowercase, one digit, one symbol, minimum 8 characters. Enforced here
    // means Identity's UserManager checks it on every password creation or
    // change everywhere in the app (registration, profile "change password",
    // and the admin "create staff/admin account" temporary password) - one
    // backend rule, not re-implemented per form.
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireDigit = true;
    options.SignIn.RequireConfirmedAccount = false;
    options.User.RequireUniqueEmail = true;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
});

// When an administrator changes someone's role or deactivates an account, their security stamp changes. Re-checking
// the stamp every minute (the default is 30) means a demoted user's old login stops working within about a minute
// instead of half an hour.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(1);
});

// ---------------- Razor Pages + role-based folder authorization ----------------
// Three work areas, three separate doors. A role only opens its own door:
//   /Admin   -> Administrator only
//   /Staff   -> Staff only            (an administrator is NOT silently let in; they have /Admin)
//   customer workflow (/Reservations, /Orders, /Feedback, /Rewards) -> Customer only
// Hiding a link in the navigation is never the protection - these rules are checked on the server for
// every request, and the individual POST handlers repeat the check where it matters.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin", "RequireAdministratorRole");
    options.Conventions.AuthorizeFolder("/Staff", "RequireStaffRole");

    options.Conventions.AuthorizeFolder("/Reservations", "RequireCustomerRole");
    options.Conventions.AuthorizeFolder("/Feedback", "RequireCustomerRole");
    options.Conventions.AuthorizeFolder("/Rewards", "RequireCustomerRole");
    options.Conventions.AuthorizePage("/Orders/Index", "RequireCustomerRole");
    options.Conventions.AuthorizePage("/Orders/Cart", "RequireCustomerRole");
    // Order receipts are the one shared page: the owner sees their own, staff and admins can open any
    // (the page model refuses everyone else). So it only needs a signed-in user here.
    options.Conventions.AuthorizePage("/Orders/Details");

    options.Conventions.AuthorizeFolder("/Notifications");
    options.Conventions.AuthorizePage("/Account/Profile");
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdministratorRole", policy => policy.RequireRole(Roles.Administrator));
    options.AddPolicy("RequireStaffRole", policy => policy.RequireRole(Roles.Staff));
    options.AddPolicy("RequireCustomerRole", policy => policy.RequireRole(Roles.Customer));
});

// ---------------- Session (used for the shopping cart) ----------------
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(3);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddHttpContextAccessor();

// ---------------- Application services ----------------
builder.Services.AddScoped<IReservationService, ReservationService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IPromotionService, PromotionService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IChatbotService, ChatbotService>();
builder.Services.AddScoped<IRecommendationService, RecommendationService>();
builder.Services.AddScoped<IStripePaymentService, StripePaymentService>();
builder.Services.AddScoped<IRewardService, RewardService>();
builder.Services.AddScoped<IProfileImageService, ProfileImageService>();
builder.Services.AddScoped<IGoogleAuthService, GoogleAuthService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ILlmChatClient, LlmChatClient>();
builder.Services.AddScoped<IAnnouncementService, AnnouncementService>();
builder.Services.AddScoped<IRewardPerkService, RewardPerkService>();
builder.Services.AddMemoryCache();   // caches local-AI recommendation results
builder.Services.AddHttpClient();    // used by the optional local-AI (Ollama) recommender
builder.Services.AddScoped<ILocalAiRecommender, OllamaRecommender>();
builder.Services.AddScoped<IAdminImageService, AdminImageService>();
builder.Services.AddScoped<IReviewImageService, ReviewImageService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<IChromeService, ChromeService>();
builder.Services.AddScoped<ISetMenuService, SetMenuService>();
builder.Services.AddHostedService<AnnouncementDispatcher>();   // tells guests about scheduled announcements when they go live

var app = builder.Build();

// ---------------- Middleware pipeline ----------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// A missing page or a refused one gets the same designed card as a crash instead of the browser's blank screen.
// The JSON endpoints under /api answer with their own body, so they are left alone.
app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/Error", "?code={0}"));

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

// ---------------- Chatbot minimal API ----------------
// A tiny anonymous endpoint used by the floating chat widget in Pages/Shared/_Layout.cshtml.
// Because it is open to everyone and may call a paid language model, it is rate limited per visitor
// address (15 questions a minute) and the question length is capped inside ChatbotService.
app.MapPost("/api/chatbot/ask", async (ChatRequest request, HttpContext http, IChatbotService chatbotService, IMemoryCache cache) =>
{
    var visitor = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var key = $"chat-rate:{visitor}";
    var used = cache.GetOrCreate(key, entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
        return new StrongBox<int>(0);
    })!;
    if (Interlocked.Increment(ref used.Value) > 15)
        return Results.Json(new { answer = "You're sending messages very quickly - please wait a moment and try again.", mode = "rules" }, statusCode: 429);

    var userId = http.User.Identity?.IsAuthenticated == true
        ? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        : null;
    // The conversation is kept in the visitor's session, so follow-ups ("yes", "for four") are read in context.
    var answer = await chatbotService.GetAnswerAsync(request.Message ?? string.Empty, userId, http.Session, request.Reset);
    return Results.Ok(new { answer = answer.Text, mode = answer.Mode, action = answer.Action });
}).AllowAnonymous();

// ---------------- Apply migrations + seed reference/demo data ----------------
using (var scope = app.Services.CreateScope())
{
    //await SeedData.InitializeAsync(scope.ServiceProvider);
}

app.Run();

/// Reset is true when the widget starts a new conversation, so the server forgets earlier turns.
record ChatRequest(string? Message, bool Reset = false);
