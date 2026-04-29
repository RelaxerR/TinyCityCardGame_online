using System.Text.Json;
using TinyCityCardGame_online.Models;

namespace TinyCityCardGame_online.Services;

/// <summary>
/// Сервис для управления мета-прогрессией игроков (сохранение в JSON).
/// </summary>
public class MetaService
{
    private readonly Dictionary<string, MetaProfile> _profiles = new();
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly ILogger<MetaService> _logger;

    /// <summary>
    /// Инициализирует сервис.
    /// </summary>
    /// <param name="env">Окружение для определения путей.</param>
    /// <param name="logger">Логгер.</param>
    public MetaService(IWebHostEnvironment env, ILogger<MetaService> logger)
    {
        _logger = logger;
        // Путь к файлу: <КореньПроекта>/Data/meta_profiles.json
        var dataDir = Path.Combine(env.ContentRootPath, "Data");
        if (!Directory.Exists(dataDir))
            Directory.CreateDirectory(dataDir);
            
        _filePath = Path.Combine(dataDir, "meta_profiles.json");
        LoadProfiles();
    }

    /// <summary>
    /// Загружает профили из файла при запуске.
    /// </summary>
    private void LoadProfiles()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<List<MetaProfile>>(json);
                if (list != null)
                {
                    foreach (var p in list)
                    {
                        _profiles[p.PlayerName] = p;
                    }
                    _logger.LogInformation("Загружено {Count} мета-профилей", _profiles.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке мета-профилей");
            }
        }
        else
        {
            _logger.LogInformation("Файл мета-профилей не найден, создается новый.");
        }
    }

    /// <summary>
    /// Сохраняет все профили в файл.
    /// </summary>
    private async Task SaveProfilesAsync()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_profiles.Values, options);
            await File.WriteAllTextAsync(_filePath, json);
            _logger.LogDebug("Мета-профили сохранены в {Path}", _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка сохранения мета-профилей");
        }
    }

    /// <summary>
    /// Получает профиль игрока. Если нет — создает новый.
    /// </summary>
    public MetaProfile GetProfile(string playerName)
    {
        lock (_lock)
        {
            if (!_profiles.ContainsKey(playerName))
            {
                _profiles[playerName] = new MetaProfile { PlayerName = playerName };
            }
            return _profiles[playerName];
        }
    }

    /// <summary>
    /// Начисляет репутацию по итогам матча.
    /// </summary>
    /// <param name="name">Имя игрока.</param>
    /// <param name="position">Место (1, 2, 3...).</param>
    /// <param name="colorSets">Количество собранных сетов цветов (доп. бонус).</param>
    public async Task AwardReputation(string name, int position, int colorSets)
    {
        int reputationGain = 0;

        // Базовая награда за место
        switch (position)
        {
            case 1: reputationGain += 50; break;
            case 2: reputationGain += 30; break;
            case 3: reputationGain += 15; break;
            default: reputationGain += 5; break; // 4-е место и далее
        }

        // Бонус за сеты (например, +5 за каждый сет)
        reputationGain += colorSets * 5;

        if (reputationGain > 0)
        {
            var profile = GetProfile(name);
            
            lock (_lock)
            {
                profile.Reputation += reputationGain;
                profile.GamesPlayed++;
                _logger.LogInformation("[META] {Name} получил +{Gain} репутации (Итого: {Total})", 
                    name, reputationGain, profile.Reputation);
            }

            await SaveProfilesAsync();
        }
    }

    /// <summary>
    /// Применяет пассивные бонусы к игроку при инициализации матча.
    /// Вызывается внутри GameSessionService при создании Player.
    /// </summary>
    public void ApplyBonusesToPlayer(Player player)
    {
        var profile = GetProfile(player.Name);
        int bonusCoins = profile.StartCoinBonus;

        if (bonusCoins > 0)
        {
            player.Coins += bonusCoins;
            _logger.LogDebug("[META] Применен бонус старта для {Name}: +{Coins} монет", player.Name, bonusCoins);
        }
    }
}