using Microsoft.Extensions.Options;
using TinyCityCardGame_online.Models;

namespace TinyCityCardGame_online.Services;

/// <summary>
/// Сервис управления игровыми сессиями и комнатами.
/// </summary>
public class GameSessionService
{
    private readonly Dictionary<string, List<string>> _rooms = new();
    private readonly Dictionary<string, string> _playerRooms = new(); // ConnectionId -> RoomCode
    private readonly Dictionary<string, GameState> _activeGames = new();
    private readonly GameSettings _settings;
    private readonly List<Card> _baseCards;
    private readonly ILogger<GameSessionService> _logger;

    /// <summary>
    /// Инициализирует новый экземпляр класса GameSessionService.
    /// </summary>
    /// <param name="settings">Настройки баланса игры.</param>
    /// <param name="loader">Загрузчик карт из Excel.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    public GameSessionService(
        IOptions<GameSettings> settings, 
        CardLoader loader,
        ILogger<GameSessionService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _baseCards = loader.LoadCardsFromExcel("Cards.xlsx");
        
        _logger.LogInformation("Сервис сессий инициализирован. Загружено {Count} базовых карт", _baseCards.Count);
    }

    /// <summary>
    /// Создает новую комнату для игры.
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    public void CreateRoom(string roomCode)
    {
        if (_rooms.ContainsKey(roomCode))
            return;
        
        _rooms[roomCode] = [];
        _logger.LogInformation("[SYSTEM] Комната {RoomCode} создана. Ожидание игроков...", roomCode);
        _logger.LogInformation("Создана новая комната {RoomCode}", roomCode);
    }

    /// <summary>
    /// Добавляет игрока в комнату.
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    /// <param name="userName">Имя игрока.</param>
    /// <param name="connectionId">SignalR ConnectionId.</param>
    public void AddPlayer(string roomCode, string userName, string connectionId)
    {
        CreateRoom(roomCode);

        if (_rooms[roomCode].Contains(userName))
            return;
        
        _rooms[roomCode].Add(userName);
        _playerRooms[connectionId] = roomCode;
            
        _logger.LogInformation("Игрок {UserName} добавлен в комнату {RoomCode}", userName, roomCode);
    }

    /// <summary>
    /// Удаляет игрока из комнаты.
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    /// <param name="connectionId">SignalR ConnectionId.</param>
    public void RemovePlayer(string roomCode, string connectionId)
    {
        if (!_rooms.TryGetValue(roomCode, out var value))
            return;
        
        var player = GetPlayerNameByConnectionId();
        if (string.IsNullOrEmpty(player))
            return;
        
        value.Remove(player);
        _playerRooms.Remove(connectionId);
                
        _logger.LogInformation("Игрок {PlayerName} удален из комнаты {RoomCode}", player, roomCode);
    }

    /// <summary>
    /// Получает имя игрока по ConnectionId.
    /// </summary>
    /// <returns>Имя игрока или null.</returns>
    private static string? GetPlayerNameByConnectionId()
    {
        // TODO: Хранить связь ConnectionId -> PlayerName в отдельной структуре
        return null;
    }

    /// <summary>
    /// Получает комнату игрока по ConnectionId.
    /// </summary>
    /// <param name="connectionId">SignalR ConnectionId.</param>
    /// <returns>Код комнаты или null.</returns>
    public string? GetPlayerRoom(string connectionId) => 
        _playerRooms.GetValueOrDefault(connectionId);

    /// <summary>
    /// Получает список игроков в комнате.
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    /// <returns>Список имен игроков.</returns>
    public List<string> GetPlayers(string roomCode) => 
        _rooms.TryGetValue(roomCode, out var players) ? players : [];

    /// <summary>
    /// Проверяет, можно ли присоединиться к комнате.
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    /// <returns>True если есть места.</returns>
    public bool IsAvailableToJoin(string roomCode) => 
        _rooms.TryGetValue(roomCode, out var players) && 
        players.Count < _settings.MaxPlayersCount;

    /// <summary>
    /// Проверяет существование комнаты.
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    /// <returns>True если комната существует.</returns>
    public bool RoomExists(string roomCode) => _rooms.ContainsKey(roomCode);

    /// <summary>
    /// Проверяет, находится ли игрок в комнате.
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    /// <param name="playerName">Имя игрока.</param>
    /// <returns>True если игрок в комнате.</returns>
    public bool IsPlayerInRoom(string roomCode, string playerName) => 
        _rooms.TryGetValue(roomCode, out var players) && 
        players.Any(p => p.Equals(playerName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Получает количество игроков в комнате.
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    /// <returns>Количество игроков.</returns>
    public int GetPlayerCount(string roomCode) => 
        _rooms.TryGetValue(roomCode, out var players) ? players.Count : 0;

    /// <summary>
    /// Получает состояние игры для комнаты.
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    /// <returns>Состояние игры или null.</returns>
    public GameState? GetGameState(string roomCode) => 
        _activeGames.GetValueOrDefault(roomCode);

    /// <summary>
    /// Создает новую игровую сессию.
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    /// <returns>Созданное состояние игры.</returns>
    public GameState CreateGame(string roomCode)
    {
        _logger.LogInformation("Создание игры в комнате {RoomCode}", roomCode);

        var state = InitializeGameState(roomCode);
        InitializePlayers(state);
        InitializeMarket(state);
        InitializeRound(state);

        _activeGames[roomCode] = state;

        _logger.LogInformation(
            "Игра создана: {RoomCode}, {Players} игр., {Market} карт на рынке",
            roomCode, state.Players.Count, state.Market.Count);

        return state;
    }

    /// <summary>
    /// Инициализирует базовое состояние игры.
    /// </summary>
    private static GameState InitializeGameState(string roomCode) => new()
    {
        RoomCode = roomCode,
        Players = [],
        Market = [],
        TurnOrder = []
    };

    /// <summary>
    /// Инициализирует игроков в игре.
    /// </summary>
    private void InitializePlayers(GameState state)
    {
        var playerNames = GetPlayers(state.RoomCode);

        foreach (var player in playerNames.Select(name => new Player
                 {
                     Name = name,
                     Coins = _settings.GenerateStartingCoins(),
                     HasBoughtThisTurn = false,
                     Inventory = []
                 }))
        {
            state.Players.Add(player);
            _logger.LogDebug("[SYSTEM] Игрок {Name} инициализирован. Старт: {Coins}💰", player.Name, player.Coins);
        }

        state.Players = state.Players.OrderBy(p => p.Coins).ToList();
        state.TurnOrder = state.Players.Select(p => p.Name).ToList();

        _logger.LogInformation("Инициализировано {Count} игроков", state.Players.Count);
    }

    private int GetTotalWeight()
    {
        var totalWeight = _baseCards.Sum(c => c.Weight);

        if (totalWeight > 0)
            return totalWeight;
        
        _logger.LogError("Сумма весов карт равна 0. Используется равномерное распределение.");
        totalWeight = _baseCards.Count;
        foreach (var card in _baseCards) card.Weight = 1;
        return totalWeight;
    }
    
    /// <summary>
    /// Выбирает карту с учетом весов.
    /// </summary>
    private Card? SelectWeightedCard(Random rng, int roundNumber)
    {
        if (roundNumber <= 0) roundNumber = 1;
        var roll = rng.Next(0, GetTotalWeight());
        var currentSum = 0;

        var allowed = _baseCards.Where(x => x.Cost <= roundNumber).ToList();

        foreach (var card in allowed)
        {
            currentSum += card.Weight;
            if (roll < currentSum)
                return card;
        }
        var randomCard = rng.Next(0, allowed.Count);
        
        return allowed[randomCard];
    }

    /// <summary>
    /// Создает глубокую копию карты.
    /// </summary>
    private static Card CloneCard(Card source) => new()
    {
        Id = Guid.NewGuid().GetHashCode(),
        Name = source.Name,
        Color = source.Color,
        Effect = source.Effect,
        Cost = source.Cost,
        Reward = source.Reward,
        Icon = source.Icon,
        Description = source.Description,
        Narrative = source.Narrative,
        Weight = source.Weight,
        IsUsed = false
    };

    /// <summary>
    /// Инициализирует рынок карт.
    /// </summary>
    private void InitializeMarket(GameState state)
    {
        var rnd = new Random();
        var targetSize = _settings.CalculateMarketSize(state.Players.Count);

        while (state.Market.Count < targetSize)
        {
            var card = SelectWeightedCard(rnd, state.RoundNumber);
            _logger.LogDebug("[SYSTEM] На рынок добавлена: {CardName} (Вес: {Weight}, Раунд: {Round})", card.Name, card.Weight, state.RoundNumber);
            state.Market.Add(card);
        }

        _logger.LogInformation("Рынок заполнен: {Count} карт (цель: {Target})", state.Market.Count, targetSize);
    }

    /// <summary>
    /// Инициализирует параметры раунда.
    /// </summary>
    private void InitializeRound(GameState state)
    {
        state.ActiveColor = GetRandomColor();
        state.RoundNumber = 1;
        state.CurrentTurnIndex = 0;
        
        state.Players = state.Players.OrderBy(p => p.Coins).ToList();
        state.TurnOrder = state.Players.Select(p => p.Name).ToList();

        _logger.LogInformation("Начальный активный цвет: {Color}", state.ActiveColor);
    }

    private CardColor GetRandomColor()
    {
        var rng = new Random();

        // 1. Считаем общую сумму всех шансов
        var totalWeight = _settings.ColorChanceBlue + _settings.ColorChanceGold + 
                          _settings.ColorChanceRed + _settings.ColorChancePurple;

        // 2. Бросаем кубик
        var roll = rng.Next(0, totalWeight);

        // 3. Определяем, в какой диапазон попало число
        if (roll < _settings.ColorChanceBlue) 
            return CardColor.Blue;
        if (roll < _settings.ColorChanceBlue + _settings.ColorChanceGold) 
            return CardColor.Gold;
        if (roll < _settings.ColorChanceBlue + _settings.ColorChanceGold + _settings.ColorChanceRed) 
            return CardColor.Red;
        else 
            return CardColor.Purple;


    }

    /// <summary>
    /// Пополняет рынок картами из колоды.
    /// </summary>
    /// <param name="state">Состояние игры.</param>
    public void ReplenishMarket(GameState state)
    {
        var rnd = new Random();
        var targetSize = _settings.CalculateMarketSize(state.Players.Count);

        while (state.Market.Count < targetSize)
        {
            var card = SelectWeightedCard(rnd, state.RoundNumber);
            _logger.LogDebug("[SYSTEM] На рынок добавлена: {CardName} (Вес: {Weight}, Раунд: {Round})", card.Name, card.Weight, state.RoundNumber);
            state.Market.Add(card);
        }

        _logger.LogDebug("Рынок пополнен до {Count} карт", state.Market.Count);
    }
}