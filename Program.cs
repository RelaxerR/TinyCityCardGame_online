using TinyCityCardGame_online.Hubs;
using TinyCityCardGame_online.Models;
using TinyCityCardGame_online.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddSingleton<GameSessionService>();
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<GameSessionService>();
builder.Services.Configure<GameSettings>(builder.Configuration.GetSection("GameBalance"));
builder.Services.AddSingleton<CardLoader>(); 
builder.Services.AddSingleton<GameSessionService>();

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options => {
        // Превращает Enum в строку (например, 0 -> "Blue")
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// Для SignalR тоже нужно добавить (если используешь System.Text.Json)
builder.Services.AddSignalR()
    .AddJsonProtocol(options => {
        options.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<GameHub>("/gameHub");

app.Run();
