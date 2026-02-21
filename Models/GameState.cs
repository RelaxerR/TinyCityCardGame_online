namespace TinyCityCardGame_online.Models;

/// <summary>
/// Представляет состояние игровой сессии (комнаты) в Color Engine.
/// Управляет игроками, рынком, колодой и очередностью ходов.
/// </summary>
public class GameState
{
    private readonly ILogger<GameState>? _logger;
    private const int DefaultMarketSize = 5;

    /// <summary>
    /// Уникальный код комнаты для подключения игроков.
    /// </summary>
    public string RoomCode { get; init; } = string.Empty;

    /// <summary>
    /// Список игроков в текущей сессии.
    /// </summary>
    public List<Player> Players { get; set; } = [];

    /// <summary>
    /// Карты, доступные для покупки на рынке.
    /// </summary>
    public List<Card> Market { get; set; } = [];

    /// <summary>
    /// Колода карт для пополнения рынка.
    /// </summary>
    public List<Card> Deck { get; set; } = [];

    /// <summary>
    /// Активный цвет текущего раунда, определяющий активируемые карты.
    /// </summary>
    public CardColor ActiveColor { get; set; }

    /// <summary>
    /// Индекс текущего игрока в списке TurnOrder.
    /// </summary>
    public int CurrentPlayerIndex { get; set; } = 0;

    /// <summary>
    /// Порядок ходов игроков (список имен).
    /// </summary>
    public List<string> TurnOrder { get; set; } = [];

    /// <summary>
    /// Индекс текущего хода в пределах раунда.
    /// </summary>
    public int CurrentTurnIndex { get; set; } = 0;

    /// <summary>
    /// Номер текущего раунда игры.
    /// </summary>
    public int RoundNumber { get; set; } = 1;

    /// <summary>
    /// Инициализирует новый экземпляр класса GameState.
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    /// <param name="logger">Логгер для записи событий игры.</param>
    public GameState(string roomCode, ILogger<GameState>? logger = null)
    {
        RoomCode = roomCode;
        _logger = logger;
    }

    /// <summary>
    /// Пустой конструктор для сериализации.
    /// </summary>
    public GameState() { }

    /// <summary>
    /// Добавляет игрока в сессию.
    /// </summary>
    /// <param name="player">Игрок для добавления.</param>
    /// <returns>True если игрок добавлен успешно.</returns>
    public bool AddPlayer(Player? player)
    {
        if (player == null)
        {
            _logger?.LogWarning("Попытка добавить null игрока в комнату {RoomCode}", RoomCode);
            return false;
        }

        if (Players.Any(p => p.Name == player.Name))
        {
            _logger?.LogWarning("Игрок с именем {PlayerName} уже существует в комнате {RoomCode}", 
                player.Name, RoomCode);
            return false;
        }

        Players.Add(player);
        UpdateTurnOrder();
        _logger?.LogInformation("Игрок {PlayerName} добавлен в комнату {RoomCode}. Всего игроков: {Count}", 
            player.Name, RoomCode, Players.Count);
        return true;
    }

    /// <summary>
    /// Удаляет игрока из сессии по ConnectionId.
    /// </summary>
    /// <param name="connectionId">Идентификатор соединения игрока.</param>
    /// <returns>Удаленный игрок или null если не найден.</returns>
    public Player? RemovePlayer(string connectionId)
    {
        var player = Players.FirstOrDefault(p => p.ConnectionId == connectionId);
        if (player == null)
        {
            _logger?.LogWarning("Игрок с ConnectionId {ConnectionId} не найден в комнате {RoomCode}", 
                connectionId, RoomCode);
            return null;
        }

        Players.Remove(player);
        TurnOrder.Remove(player.Name);
        UpdateCurrentPlayerIndex();
        _logger?.LogInformation("Игрок {PlayerName} удален из комнаты {RoomCode}", player.Name, RoomCode);
        return player;
    }

    /// <summary>
    /// Получает текущего игрока по индексу очередности.
    /// </summary>
    /// <returns>Текущий игрок или null если список пуст.</returns>
    public Player? GetCurrentPlayer()
    {
        return string.IsNullOrEmpty(TurnOrder[CurrentPlayerIndex]) ?
            null :
            Players.FirstOrDefault(p => p.Name == TurnOrder[CurrentPlayerIndex]);
    }

    /// <summary>
    /// Переходит к следующему игроку в очередности.
    /// </summary>
    /// <returns>Имя следующего игрока.</returns>
    public string NextPlayer()
    {
        if (TurnOrder.Count == 0)
        {
            _logger?.LogWarning("Попытка перехода к следующему игроку при пустом списке очереди");
            return string.Empty;
        }

        CurrentPlayerIndex = (CurrentPlayerIndex + 1) % TurnOrder.Count;
        CurrentTurnIndex++;
        
        _logger?.LogDebug("Ход передан игроку {PlayerName}. Ход #{TurnIndex}", 
            TurnOrder[CurrentPlayerIndex], CurrentTurnIndex);
        
        return TurnOrder[CurrentPlayerIndex];
    }

    /// <summary>
    /// Обновляет порядок ходов на основе количества монет (балансировка лидеров).
    /// </summary>
    public void UpdateTurnOrder()
    {
        TurnOrder = Players
            .OrderBy(p => p.Coins)
            .ThenBy(p => p.Name)
            .Select(p => p.Name)
            .ToList();

        _logger?.LogInformation("Порядок ходов обновлен в комнате {RoomCode}: {Order}", 
            RoomCode, string.Join(" → ", TurnOrder));
    }

    /// <summary>
    /// Обновляет индекс текущего игрока после изменения состава.
    /// </summary>
    private void UpdateCurrentPlayerIndex()
    {
        if (CurrentPlayerIndex >= TurnOrder.Count)
            CurrentPlayerIndex = Math.Max(0, TurnOrder.Count - 1);
    }

    /// <summary>
    /// Начинает новый раунд игры.
    /// </summary>
    public void StartNewRound()
    {
        RoundNumber++;
        CurrentTurnIndex = 0;
        CurrentPlayerIndex = 0;
        
        // Сброс состояния покупки для всех игроков
        foreach (var player in Players)
        {
            player?.ResetTurnState();
        }

        _logger?.LogInformation("Начат раунд #{RoundNumber} в комнате {RoomCode}", RoundNumber, RoomCode);
    }

    /// <summary>
    /// Добавляет карты на рынок из колоды.
    /// </summary>
    /// <param name="targetSize">Целевое количество карт на рынке.</param>
    public void ReplenishMarket(int targetSize)
    {
        while (Market.Count < targetSize && Deck.Count > 0)
        {
            var card = Deck.FirstOrDefault();
            if (card == null)
                continue;
            
            Deck.Remove(card);
            Market.Add(card);
            _logger?.LogDebug("Карта {CardName} добавлена на рынок", card.Name);
        }

        _logger?.LogInformation("Рынок пополнен. На рынке {MarketCount} карт из {TargetSize}", 
            Market.Count, targetSize);
    }

    /// <summary>
    /// Удаляет карту с рынка по ID.
    /// </summary>
    /// <param name="cardId">Идентификатор карты.</param>
    /// <returns>Удаленная карта или null если не найдена.</returns>
    public Card? RemoveCardFromMarket(int cardId)
    {
        var card = Market.FirstOrDefault(c => c.Id == cardId);
        if (card == null)
        {
            _logger?.LogWarning("Карта с ID {CardId} не найдена на рынке комнаты {RoomCode}", cardId, RoomCode);
            return null;
        }

        Market.Remove(card);
        _logger?.LogInformation("Карта {CardName} удалена с рынка", card.Name);
        return card;
    }

    /// <summary>
    /// Проверяет, есть ли победитель в текущей сессии.
    /// </summary>
    /// <param name="winTarget">Целевое количество монет для победы.</param>
    /// <returns>Имя победителя или null если победителя нет.</returns>
    public string? CheckWinner(int winTarget)
    {
        var winner = Players.FirstOrDefault(p => p.HasWon(winTarget));
        if (winner == null)
            return null;
        
        _logger?.LogInformation("Победитель определен: {PlayerName} ({Coins} монет)", 
            winner.Name, winner.Coins);
        
        return winner.Name;

    }

    /// <summary>
    /// Получает количество игроков в сессии.
    /// </summary>
    /// <returns>Количество игроков.</returns>
    public int GetPlayerCount() => Players.Count;

    /// <summary>
    /// Рассчитывает размер рынка на основе количества игроков.
    /// </summary>
    /// <param name="formula">Формула расчета (например, "{players_count} + 1").</param>
    /// <returns>Размер рынка.</returns>
    public int CalculateMarketSize(string formula = "{players_count} + 1")
    {
        var playerCount = GetPlayerCount();
        return formula.Replace("{players_count}", playerCount.ToString())
            .Split('+')
            .Sum(part => int.TryParse(part.Trim(), out var value) ? value : 0);
    }

    /// <summary>
    /// Возвращает строковое представление состояния игры.
    /// </summary>
    /// <returns>Информация о состоянии сессии.</returns>
    public override string ToString() => 
        $"Room: {RoomCode}, Round: {RoundNumber}, Players: {GetPlayerCount()}, ActiveColor: {ActiveColor}";
}