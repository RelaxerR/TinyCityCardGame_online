using Microsoft.Extensions.Options;
using TinyCityCardGame_online.Models;

namespace TinyCityCardGame_online.Services;

public class GameSessionService
{
    private readonly Dictionary<string, List<string>> _rooms = new();
    private readonly Dictionary<string, GameState> _activeGames = new();
    
    private readonly GameSettings _settings;
    private readonly List<Card> _baseCards;

    public GameSessionService(IOptions<GameSettings> settings, CardLoader loader) {
        _settings = settings.Value;
        // Загружаем эталонный список карт один раз при старте
        _baseCards = loader.LoadCardsFromExcel("cards.xlsx");
    }
    
    public void AddPlayer(string roomCode, string userName)
    {
        if (!_rooms.ContainsKey(roomCode)) _rooms[roomCode] = new List<string>();
        if (!_rooms[roomCode].Contains(userName)) _rooms[roomCode].Add(userName);
    }

    public List<string> GetPlayers(string roomCode) => 
        _rooms.ContainsKey(roomCode) ? _rooms[roomCode] : new List<string>();

    public GameState GetGameState(string roomCode) =>
        _activeGames.ContainsKey(roomCode) ? _activeGames[roomCode] : null;
    
    public bool RoomExists(string code) => _rooms.ContainsKey(code);

    public GameState CreateGame(string roomCode)
    {
        var state = new GameState { RoomCode = roomCode };
        var rng = new Random();

        // 1. ИНИЦИАЛИЗАЦИЯ ИГРОКОВ (из настроек JSON)
        var playerNames = GetPlayers(roomCode);
        foreach (var name in playerNames)
        {
            state.Players.Add(new Player 
            { 
                Name = name, 
                Coins = _settings.StartCoins, // Значение из appsettings.json
                HasBoughtThisTurn = false,
                Inventory = new List<Card>()
            });
        }

        // Порядок хода: от бедных к богатым
        state.TurnOrder = state.Players
            .OrderBy(p => p.Coins)
            .Select(p => p.Name)
            .ToList();

        // 2. ГЕНЕРАЦИЯ КОЛОДЫ (Взвешенный рандом 1-100)
        int targetDeckSize = 100; 
        int totalWeight = _baseCards.Sum(c => c.Weight);

        // Защита: если в Excel забыли проставить веса или файл пуст
        if (totalWeight <= 0 || !_baseCards.Any())
        {
            Console.WriteLine("[CRITICAL] Список базовых карт пуст или веса равны 0! Проверьте Cards.xlsx.");
            // Добавим хоть одну техническую карту, чтобы сервер не упал
            _baseCards.Add(new Card { Name = "Ошибка Excel", Color = CardColor.Blue, Weight = 1, Effect = "GET 1" });
            totalWeight = 1;
        }

        for (int i = 0; i < targetDeckSize; i++)
        {
            int roll = rng.Next(0, totalWeight);
            int currentSum = 0;

            foreach (var bc in _baseCards)
            {
                currentSum += bc.Weight;
                if (roll < currentSum)
                {
                    // ГЛУБОКОЕ КОПИРОВАНИЕ (Создаем новый объект, а не ссылку)
                    state.Deck.Add(new Card 
                    {
                        Id = Guid.NewGuid().GetHashCode(), 
                        Name = bc.Name,
                        Color = bc.Color,
                        Effect = bc.Effect,
                        Cost = bc.Cost,
                        Reward = bc.Reward,
                        Icon = bc.Icon,
                        Description = bc.Description,
                        Weight = bc.Weight,
                        IsUsed = false
                    });
                    break;
                }
            }
        }

        // Перемешиваем колоду после генерации
        state.Deck = state.Deck.OrderBy(x => rng.Next()).ToList();

        // 3. БЕЗОПАСНОЕ ФОРМИРОВАНИЕ РЫНКА (N + 1)
        // Учитываем лимит из конфига, если он задан
        int targetMarketSize = state.Players.Count + 1;
        if (_settings.MaxMarketSize > 0 && targetMarketSize > _settings.MaxMarketSize)
        {
            targetMarketSize = _settings.MaxMarketSize;
        }

        // Вместо RemoveRange используем безопасный цикл, чтобы не выйти за границы List
        while (state.Market.Count < targetMarketSize && state.Deck.Any())
        {
            var firstCard = state.Deck[0];
            state.Market.Add(firstCard);
            state.Deck.RemoveAt(0);
        }

        // 4. СТАРТОВЫЕ ПАРАМЕТРЫ СЕССИИ
        state.ActiveColor = (CardColor)rng.Next(0, 4);
        state.RoundNumber = 1;
        state.CurrentTurnIndex = 0;

        // Сохраняем готовую игру в словарь активных сессий
        _activeGames[roomCode] = state;
        
        Console.WriteLine($"[GAME] Создана комната {roomCode}: {state.Players.Count} игр., {state.Market.Count} карт на рынке, {state.Deck.Count} в колоде.");
        
        return state;
    }
}
