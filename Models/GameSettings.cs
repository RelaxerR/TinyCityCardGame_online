namespace TinyCityCardGame_online.Models;

/// <summary>
/// Конфигурация баланса игры Color Engine.
/// Загружается из appsettings.json и управляет параметрами сессии.
/// </summary>
public class GameSettings 
{
    private readonly ILogger<GameSettings>? _logger;
    private const int DefaultWinTarget = 100;
    private const int DefaultDailyIncome = 1;
    private const int MinPlayers = 2;
    private const int MaxPlayers = 4;

    /// <summary>
    /// Минимальное количество стартовых монет (рандомизация).
    /// </summary>
    public int StartCoinsMin { get; set; } = 5;

    /// <summary>
    /// Максимальное количество стартовых монет (рандомизация).
    /// </summary>
    public int StartCoinsMax { get; set; } = 10;

    /// <summary>
    /// Порог победы (целевое количество монет).
    /// </summary>
    public int WinTarget { get; set; } = DefaultWinTarget;

    /// <summary>
    /// Гарантированный доход в конце каждого хода.
    /// </summary>
    public int DailyIncome { get; set; } = DefaultDailyIncome;

    /// <summary>
    /// Минимальное количество игроков в сессии.
    /// </summary>
    public int MinPlayersCount { get; set; } = MinPlayers;

    /// <summary>
    /// Максимальное количество игроков в сессии.
    /// </summary>
    public int MaxPlayersCount { get; set; } = MaxPlayers;

    /// <summary>
    /// Формула расчета размера рынка.
    /// </summary>
    public string MarketSizeFormula { get; set; } = "{players_count} + 1";

    /// <summary>
    /// Инициализирует новый экземпляр класса GameSettings.
    /// </summary>
    /// <param name="logger">Логгер для записи событий конфигурации.</param>
    public GameSettings(ILogger<GameSettings>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Пустой конструктор для сериализации.
    /// </summary>
    public GameSettings() { }

    /// <summary>
    /// Проверяет валидность конфигурации баланса.
    /// </summary>
    /// <returns>Список ошибок валидации.</returns>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (StartCoinsMin < 0)
            errors.Add("Минимальное количество стартовых монет не может быть отрицательным");

        if (StartCoinsMax < StartCoinsMin)
            errors.Add("Максимальное количество стартовых монет должно быть больше минимального");

        if (WinTarget <= 0)
            errors.Add("Цель победы должна быть положительным числом");

        if (DailyIncome < 0)
            errors.Add("Ежедневный доход не может быть отрицательным");

        if (MinPlayersCount < MinPlayers)
            errors.Add($"Минимальное количество игроков должно быть не менее {MinPlayers}");

        if (MaxPlayersCount > MaxPlayers)
            errors.Add($"Максимальное количество игроков должно быть не более {MaxPlayers}");

        if (MinPlayersCount > MaxPlayersCount)
            errors.Add("Минимальное количество игроков не может превышать максимальное");

        if (errors.Count > 0)
            _logger?.LogWarning("Валидация настроек игры не пройдена: {Errors}", string.Join("; ", errors));
        else
            _logger?.LogInformation("Настройки игры успешно валидированы");

        return errors;
    }

    /// <summary>
    /// Проверяет допустимое количество игроков.
    /// </summary>
    /// <param name="count">Количество игроков для проверки.</param>
    /// <returns>True если количество в допустимом диапазоне.</returns>
    public bool IsValidPlayerCount(int count) => 
        count >= MinPlayersCount && count <= MaxPlayersCount;

    /// <summary>
    /// Генерирует случайное количество стартовых монет в заданном диапазоне.
    /// </summary>
    /// <returns>Количество стартовых монет.</returns>
    public int GenerateStartingCoins()
    {
        var random = new Random();
        var coins = random.Next(StartCoinsMin, StartCoinsMax + 1);
        _logger?.LogDebug("Сгенерировано стартовых монет: {Coins}", coins);
        return coins;
    }

    /// <summary>
    /// Рассчитывает размер рынка по формуле.
    /// </summary>
    /// <param name="playerCount">Количество игроков.</param>
    /// <returns>Размер рынка.</returns>
    public int CalculateMarketSize(int playerCount)
    {
        var formula = MarketSizeFormula.Replace("{players_count}", playerCount.ToString());
        
        try
        {
            // Простой парсер для формулы вида "N + 1"
            var parts = formula.Split('+').Select(p => p.Trim());
            var result = parts.Sum(p => int.TryParse(p, out var value) ? value : 0);
            
            _logger?.LogDebug("Рассчитан размер рынка: {Size} (игроков: {Count})", result, playerCount);
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при расчете размера рынка по формуле: {Formula}", formula);
            return playerCount + 1; // Fallback
        }
    }

    /// <summary>
    /// Применяет настройки по умолчанию для некорректных значений.
    /// </summary>
    public void ApplyDefaults()
    {
        if (StartCoinsMin < 0) StartCoinsMin = 5;
        if (StartCoinsMax < StartCoinsMin) StartCoinsMax = StartCoinsMin + 5;
        if (WinTarget <= 0) WinTarget = DefaultWinTarget;
        if (DailyIncome < 0) DailyIncome = DefaultDailyIncome;
        if (MinPlayersCount < MinPlayers) MinPlayersCount = MinPlayers;
        if (MaxPlayersCount > MaxPlayers) MaxPlayersCount = MaxPlayers;
        if (MinPlayersCount > MaxPlayersCount) MinPlayersCount = MaxPlayersCount;

        _logger?.LogInformation("Применены настройки по умолчанию для некорректных значений");
    }

    /// <summary>
    /// Возвращает строковое представление настроек.
    /// </summary>
    /// <returns>Информация о конфигурации.</returns>
    public override string ToString() => 
        $"WinTarget: {WinTarget}, DailyIncome: {DailyIncome}, " +
        $"Players: {MinPlayersCount}-{MaxPlayersCount}, StartCoins: {StartCoinsMin}-{StartCoinsMax}";
}