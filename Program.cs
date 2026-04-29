using System.Text.Json;
using TinyCityCardGame_online.Hubs;
using TinyCityCardGame_online.Models;
using TinyCityCardGame_online.Services;
using System.Text.Json.Serialization;
using TinyCityCardGame_online.Analitics.Services;

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// 📦 Регистрация сервисов
// ============================================================================

// Добавляем контроллеры с поддержкой Views
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        // Конвертирует Enum в строку (например, 0 -> "Blue")
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

// Регистрируем SignalR для real-time взаимодействия
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

// Регистрируем сервисы игры
builder.Services.AddSingleton<CardLoader>();
builder.Services.AddSingleton<GameSessionService>();
builder.Services.Configure<GameSettings>(builder.Configuration.GetSection("GameBalance"));
builder.Services.AddSingleton<MetaService>();

// Регистрируем сервисы симуляции
builder.Services.AddSingleton<CardProbabilityCalculator>();
builder.Services.AddSingleton<GameBalanceAnalyzer>();

// Регистрируем логгер для отладки игровых событий
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// ============================================================================
// 🏗️ Построение приложения
// ============================================================================

var app = builder.Build();

// ============================================================================
// ⚙️ Конфигурация HTTP конвейера
// ============================================================================

// Обработка ошибок в production
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Перенаправление на HTTPS
app.UseHttpsRedirection();

// Статические файлы (CSS, JS, изображения)
app.UseStaticFiles();

// Маршрутизация
app.UseRouting();

// Авторизация (если потребуется в будущем)
app.UseAuthorization();

// ============================================================================
// 🗺️ Регистрация эндпоинтов
// ============================================================================

// Маршрут по умолчанию для контроллеров
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// SignalR хаб для real-time коммуникации
app.MapHub<GameHub>("/gameHub");

// ============================================================================
// 🚀 Запуск расчета вероятностей после старта
// ============================================================================

var analyzer = app.Services.GetRequiredService<GameBalanceAnalyzer>();

// Run in background and capture exceptions; use smaller count for quick debug (change back to 1000 after verification)
_ = Task.Run(async () =>
{
    try
    {
        await analyzer.RunFullAnalysis(10000); // use 100 for testing; set to 1000 for full runs
    }
    catch (Exception ex)
    {
        var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
        startupLogger.LogError(ex, "❌ Balance analyzer background task failed");
    }
});

// var probabilityCalculator = app.Services.GetRequiredService<CardProbabilityCalculator>();
// _ = Task.Run(async () => await probabilityCalculator.CalculateAndPrintProbabilities());

// ============================================================================
// 📝 Логирование запуска
// ============================================================================

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("🎮 Color Engine сервер запускается...");
logger.LogInformation("📍 URL: {Url}", app.Urls.FirstOrDefault() ?? "http://localhost:5090");
logger.LogInformation("🎯 Режим: {Environment}", app.Environment.EnvironmentName);

try
{
    app.Run();
}
catch (Exception ex)
{
    logger.LogCritical(ex, "❌ Критическая ошибка при запуске сервера");
    throw;
}