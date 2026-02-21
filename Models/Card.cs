namespace TinyCityCardGame_online.Models;

/// <summary>
/// Перечисление цветов карт, определяющих сектор и механику активации.
/// </summary>
public enum CardColor 
{ 
    /// <summary>Производственный сектор - доход всем игрокам с такой картой.</summary>
    Blue, 
    /// <summary>Коммерческий сектор - личный доход из банка.</summary>
    Gold, 
    /// <summary>Теневой сектор - кража/перераспределение монет.</summary>
    Red, 
    /// <summary>Спецпроекты - манипуляция правилами и рынком.</summary>
    Purple 
}

/// <summary>
/// Представляет карту в игре Color Engine с эффектами и параметрами баланса.
/// </summary>
public class Card
{
    private readonly ILogger<Card>? _logger;

    /// <summary>
    /// Уникальный идентификатор карты.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Название карты (уникальный идентификатор).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Цвет/сектор карты, определяющий механику активации.
    /// </summary>
    public CardColor Color { get; init; }

    /// <summary>
    /// DSL-команда эффекта карты (например, "GET 5" или "STEAL_MONEY ALL 2").
    /// </summary>
    public string Effect { get; init; } = string.Empty;

    /// <summary>
    /// Стоимость покупки карты с рынка.
    /// </summary>
    public int Cost { get; init; }

    /// <summary>
    /// Базовое значение награды для эффектов типа GET.
    /// </summary>
    public int Reward { get; init; }

    /// <summary>
    /// Имя файла иконки карты в папке wwwroot/images/cards/.
    /// </summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// Текстовое описание эффекта карты.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Нарративное описание карты в сеттинге игры.
    /// </summary>
    public string Narrative { get; init; } = string.Empty;

    /// <summary>
    /// Флаг использования карты в текущем ходу.
    /// </summary>
    public bool IsUsed { get; set; } = false;

    /// <summary>
    /// Вес вероятности появления карты на рынке (1-100).
    /// </summary>
    public int Weight { get; set; } = 50;

    /// <summary>
    /// Инициализирует новый экземпляр класса Card.
    /// </summary>
    /// <param name="logger">Логгер для записи событий карты.</param>
    public Card(ILogger<Card>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Пустой конструктор для сериализации.
    /// </summary>
    public Card() { }

    /// <summary>
    /// Проверяет валидность данных карты.
    /// </summary>
    /// <returns>Список ошибок валидации.</returns>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Название карты не указано");

        if (string.IsNullOrWhiteSpace(Effect))
            errors.Add("Эффект карты не указан");

        if (Cost < 0)
            errors.Add("Стоимость карты не может быть отрицательной");

        if (Weight is < 1 or > 100)
            errors.Add("Вес карты должен быть в диапазоне 1-100");

        if (errors.Count > 0)
            _logger?.LogWarning("Валидация карты {CardName} не пройдена: {Errors}", Name, string.Join("; ", errors));

        return errors;
    }

    /// <summary>
    /// Активирует эффект карты.
    /// </summary>
    /// <returns>True если активация успешна, иначе False.</returns>
    public bool Activate()
    {
        if (string.IsNullOrWhiteSpace(Effect))
        {
            _logger?.LogWarning("Попытка активации карты {CardName} без эффекта", Name);
            return false;
        }

        IsUsed = true;
        _logger?.LogInformation("Карта {CardName} активирована с эффектом: {Effect}", Name, Effect);
        return true;
    }

    /// <summary>
    /// Сбрасывает состояние использования карты.
    /// </summary>
    public void Reset()
    {
        IsUsed = false;
        _logger?.LogDebug("Состояние карты {CardName} сброшено", Name);
    }

    /// <summary>
    /// Парсит DSL-команду эффекта и извлекает параметры.
    /// </summary>
    /// <returns>Кортеж с командой и параметрами.</returns>
    public (string Command, string[] Parameters) ParseEffect()
    {
        if (string.IsNullOrWhiteSpace(Effect))
            return (string.Empty, []);

        var parts = Effect.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts.FirstOrDefault()?.ToUpper() ?? string.Empty;
        var parameters = parts.Skip(1).ToArray();

        _logger?.LogDebug("Парсинг эффекта карты {CardName}: Команда={Command}, Параметры={Params}", 
            Name, command, string.Join(", ", parameters));

        return (command, parameters);
    }

    /// <summary>
    /// Проверяет, является ли карта производственного сектора (Blue).
    /// </summary>
    /// <returns>True если карта синего цвета.</returns>
    public bool IsProductionCard() => Color == CardColor.Blue;

    /// <summary>
    /// Проверяет, является ли карта коммерческого сектора (Gold).
    /// </summary>
    /// <returns>True если карта золотого цвета.</returns>
    public bool IsCommercialCard() => Color == CardColor.Gold;

    /// <summary>
    /// Проверяет, является ли карта теневого сектора (Red).
    /// </summary>
    /// <returns>True если карта красного цвета.</returns>
    public bool IsShadowCard() => Color == CardColor.Red;

    /// <summary>
    /// Проверяет, является ли карта спецпроектом (Purple).
    /// </summary>
    /// <returns>True если карта фиолетового цвета.</returns>
    public bool IsSpecialCard() => Color == CardColor.Purple;

    /// <summary>
    /// Рассчитывает ожидаемый период окупаемости карты.
    /// </summary>
    /// <param name="activationProbability">Вероятность активации (0.0-1.0).</param>
    /// <returns>Количество ходов до окупаемости.</returns>
    public double CalculatePaybackPeriod(double activationProbability)
    {
        if (activationProbability is <= 0 or > 1)
        {
            _logger?.LogWarning("Некорректная вероятность активации: {Probability}", activationProbability);
            return double.MaxValue;
        }

        if (Reward <= 0)
            return double.MaxValue;

        var payback = Cost / (Reward * activationProbability);
        _logger?.LogDebug("Период окупаемости карты {CardName}: {Payback} ходов", Name, payback);
        return payback;
    }

    /// <summary>
    /// Возвращает строковое представление карты.
    /// </summary>
    /// <returns>Информация о карте.</returns>
    public override string ToString() => 
        $"{Name} [{Color}] - Cost: {Cost}, Reward: {Reward}, Weight: {Weight}";
}