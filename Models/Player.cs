using Microsoft.Extensions.Logging;

namespace TinyCityCardGame_online.Models;

/// <summary>
/// Представляет игрока в карточной игре Color Engine.
/// Содержит информацию о состоянии игрока, инвентаре и ресурсах.
/// </summary>
public class Player
{
    private readonly ILogger<Player>? _logger;
    private const int DefaultStartingCoins = 3;

    /// <summary>
    /// Уникальное имя игрока.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Идентификатор соединения SignalR для real-time коммуникации.
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// Текущее количество монет (корма) у игрока.
    /// </summary>
    public int Coins { get; set; } = DefaultStartingCoins;

    /// <summary>
    /// Список карт в инвентаре игрока.
    /// </summary>
    public List<Card?> Inventory { get; set; } = new();

    /// <summary>
    /// Флаг, указывающий, совершал ли игрок покупку в текущий ход.
    /// </summary>
    public bool HasBoughtThisTurn { get; set; } = false;

    /// <summary>
    /// Инициализирует новый экземпляр класса Player.
    /// </summary>
    /// <param name="name">Имя игрока.</param>
    /// <param name="connectionId">Идентификатор соединения.</param>
    /// <param name="logger">Логгер для записи событий игрока.</param>
    public Player(string name, string connectionId, ILogger<Player>? logger = null)
    {
        Name = name;
        ConnectionId = connectionId;
        _logger = logger;
    }

    /// <summary>
    /// Пустой конструктор для сериализации.
    /// </summary>
    public Player() { }

    /// <summary>
    /// Добавляет указанное количество монет игроку.
    /// </summary>
    /// <param name="amount">Количество монет для добавления.</param>
    /// <returns>Флаг успешности операции.</returns>
    public bool AddCoins(int amount)
    {
        if (amount <= 0)
        {
            _logger?.LogWarning("Попытка добавить неположительное количество монет: {Amount}", amount);
            return false;
        }

        Coins += amount;
        _logger?.LogInformation("Игрок {PlayerName} получил {Amount} монет. Всего: {Total}", Name, amount, Coins);
        return true;
    }

    /// <summary>
    /// Списывает указанное количество монет у игрока.
    /// </summary>
    /// <param name="amount">Количество монет для списания.</param>
    /// <returns>Флаг успешности операции.</returns>
    public bool SpendCoins(int amount)
    {
        if (amount <= 0)
        {
            _logger?.LogWarning("Попытка списать неположительное количество монет: {Amount}", amount);
            return false;
        }

        if (Coins < amount)
        {
            _logger?.LogWarning("Игрок {PlayerName} не имеет достаточно монет. Требуется: {Required}, Доступно: {Available}", 
                Name, amount, Coins);
            return false;
        }

        Coins -= amount;
        _logger?.LogInformation("Игрок {PlayerName} потратил {Amount} монет. Остаток: {Total}", Name, amount, Coins);
        return true;
    }

    /// <summary>
    /// Проверяет, может ли игрок позволить себе покупку карты.
    /// </summary>
    /// <param name="cardCost">Стоимость карты.</param>
    /// <returns>True если достаточно монет, иначе False.</returns>
    public bool CanAfford(int cardCost) => Coins >= cardCost;

    /// <summary>
    /// Добавляет карту в инвентарь игрока.
    /// </summary>
    /// <param name="card">Карта для добавления.</param>
    /// <returns>Флаг успешности операции.</returns>
    public bool AddCardToInventory(Card? card)
    {
        if (card == null)
        {
            _logger?.LogWarning("Попытка добавить null карту игроку {PlayerName}", Name);
            return false;
        }

        Inventory.Add(card);
        _logger?.LogInformation("Игрок {PlayerName} получил карту: {CardName}", Name, card.Name);
        return true;
    }

    /// <summary>
    /// Удаляет карту из инвентаря игрока по ID.
    /// </summary>
    /// <param name="cardId">Идентификатор карты.</param>
    /// <returns>Удаленная карта или null если не найдена.</returns>
    public Card? RemoveCardFromInventory(int cardId)
    {
        var card = Inventory.FirstOrDefault(c => c.Id == cardId);
        if (card == null)
        {
            _logger?.LogWarning("Карта с ID {CardId} не найдена у игрока {PlayerName}", cardId, Name);
            return null;
        }

        Inventory.Remove(card);
        _logger?.LogInformation("Карта {CardName} удалена из инвентаря игрока {PlayerName}", card.Name, Name);
        return card;
    }

    /// <summary>
    /// Получает все карты указанного цвета из инвентаря.
    /// </summary>
    /// <param name="color">Цвет карт для фильтрации.</param>
    /// <returns>Список карт заданного цвета.</returns>
    public List<Card?> GetCardsByColor(CardColor color) => 
        Inventory.Where(c => c.Color == color).ToList();

    /// <summary>
    /// Подсчитывает количество карт указанного цвета.
    /// </summary>
    /// <param name="color">Цвет для подсчета.</param>
    /// <returns>Количество карт.</returns>
    public int CountCardsByColor(CardColor color) => 
        Inventory.Count(c => c.Color == color);

    /// <summary>
    /// Сбрасывает флаг покупки для нового хода.
    /// </summary>
    public void ResetTurnState()
    {
        HasBoughtThisTurn = false;
        _logger?.LogDebug("Состояние хода игрока {PlayerName} сброшено", Name);
    }

    /// <summary>
    /// Проверяет, достиг ли игрок цели победы.
    /// </summary>
    /// <param name="winTarget">Целевое количество монет для победы.</param>
    /// <returns>True если цель достигнута, иначе False.</returns>
    public bool HasWon(int winTarget) => Coins >= winTarget;

    /// <summary>
    /// Возвращает строковое представление игрока.
    /// </summary>
    /// <returns>Информация об игроке.</returns>
    public override string ToString() => 
        $"{Name}: {Coins} монет, карт: {Inventory.Count}";
}