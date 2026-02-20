using TinyCityCardGame_online.Models;

namespace TinyCityCardGame_online.Services;

public class GameSessionService
{
    private readonly Dictionary<string, List<string>> _rooms = new();
    private readonly Dictionary<string, GameState> _activeGames = new();

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

        // 1. Создаем игроков с рандомными монетами (5-10)
        var playerNames = GetPlayers(roomCode);
        foreach (var name in playerNames)
        {
            state.Players.Add(new Player 
            { 
                Name = name, 
                Coins = rng.Next(5, 11) // От 5 до 10 монет
            });
        }

        // 2. Устанавливаем порядок хода: от самого бедного к самому богатому
        state.TurnOrder = state.Players
            .OrderBy(p => p.Coins)
            .Select(p => p.Name)
            .ToList();

        // 3. Наполняем колоду (по 10 карт каждого типа)
        var baseCards = new List<Card> {
            new Card { Name = "Пшеница", Color = CardColor.Blue, Cost = 1, Reward = 1, Icon = "🌾" },
            new Card { Name = "Лес", Color = CardColor.Gold, Cost = 2, Reward = 2, Icon = "🌲" },
            new Card { Name = "Рынок", Color = CardColor.Red, Cost = 3, Reward = 3, Icon = "⚖️" },
            new Card { Name = "Шахта", Color = CardColor.Purple, Cost = 6, Reward = 5, Icon = "⛏️" }
        };

        foreach(var bc in baseCards) {
            for(int i = 0; i < 10; i++) { 
                state.Deck.Add(new Card { 
                    Id = Guid.NewGuid().GetHashCode(), 
                    Name = bc.Name, Color = bc.Color, Cost = bc.Cost, Reward = bc.Reward, Icon = bc.Icon 
                });
            }
        }

        // Перемешиваем колоду
        state.Deck = state.Deck.OrderBy(x => rng.Next()).ToList();

        // 4. Формируем рынок (N+1 карт)
        int marketSize = state.Players.Count + 1; 
        state.Market = state.Deck.Take(marketSize).ToList();
        state.Deck.RemoveRange(0, marketSize);
        
        // 5. Начальный цвет и индекс игрока
        state.ActiveColor = (CardColor)rng.Next(0, 4);
        state.CurrentTurnIndex = 0;

        _activeGames[roomCode] = state; 
        return state;
    }
}
