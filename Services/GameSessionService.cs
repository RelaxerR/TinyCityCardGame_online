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

        // 1. Игроки и стартовые монеты из конфига
        var playerNames = GetPlayers(roomCode);
        foreach (var name in playerNames)
        {
            state.Players.Add(new Player { 
                Name = name, 
                Coins = _settings.StartCoins // Берем 5 или 10 из JSON
            });
        }

        state.TurnOrder = state.Players.OrderBy(p => p.Coins).Select(p => p.Name).ToList();

        // 2. Наполнение колоды (КЛОНИРОВАНИЕ)
        // Если в Excel 10 видов карт, сделаем по 5 копий каждой
        foreach (var bc in _baseCards) 
        {
            for (int i = 0; i < 5; i++) 
            {
                state.Deck.Add(new Card {
                    Id = Guid.NewGuid().GetHashCode(), // Уникальный ID!
                    Name = bc.Name,
                    Color = bc.Color,
                    Effect = bc.Effect,
                    Cost = bc.Cost,
                    Reward = bc.Reward,
                    Icon = bc.Icon,
                    Description = bc.Description,
                    IsUsed = false
                });
            }
        }

        // Перемешиваем
        state.Deck = state.Deck.OrderBy(x => rng.Next()).ToList();

        // 3. Рынок: N + 1 (или из конфига, если там задано жестко)
        int marketSize = state.Players.Count + 1; 
        // Если хочешь использовать MaxMarketSize из JSON:
        // int marketSize = _settings.MaxMarketSize;

        state.Market = state.Deck.Take(marketSize).ToList();
        state.Deck.RemoveRange(0, marketSize);

        state.ActiveColor = (CardColor)rng.Next(0, 4);
        state.RoundNumber = 1;

        _activeGames[roomCode] = state;
        return state;
    }
}
