using TinyCityCardGame_online.Models;

namespace TinyCityCardGame_online.Services;

public class GameSessionService
{
    // Словарь: КодКомнаты -> Список Имен
    private readonly Dictionary<string, List<string>> _rooms = new();
    private readonly Dictionary<string, GameState> _activeGames = new();

    public void AddPlayer(string roomCode, string userName)
    {
        if (!_rooms.ContainsKey(roomCode)) _rooms[roomCode] = new List<string>();
        if (!_rooms[roomCode].Contains(userName)) _rooms[roomCode].Add(userName);
    }

    public List<string> GetPlayers(string roomCode) => 
        _rooms.ContainsKey(roomCode) ? _rooms[roomCode] : new List<string>();
    
    public GameState CreateGame(string roomCode)
    {
        var state = new GameState { RoomCode = roomCode };
    
        // Генерируем "пачку" карт (например, по 10 штук каждого типа)
        var baseCards = new List<Card> {
            new Card { Id = 1, Name = "Пшеница", Color = CardColor.Blue, Cost = 1, Reward = 1, Icon = "🌾" },
            new Card { Id = 2, Name = "Лес", Color = CardColor.Green, Cost = 2, Reward = 2, Icon = "🌲" },
            new Card { Id = 3, Name = "Рынок", Color = CardColor.Red, Cost = 3, Reward = 3, Icon = "⚖️" },
            new Card { Id = 4, Name = "Шахта", Color = CardColor.Purple, Cost = 6, Reward = 5, Icon = "⛏️" }
        };

        foreach(var card in baseCards) {
            for(int i = 0; i < 10; i++) { 
                state.Deck.Add(new Card { 
                    Id = Guid.NewGuid().GetHashCode(), // Уникальный ID для каждой копии
                    Name = card.Name, Color = card.Color, Cost = card.Cost, Reward = card.Reward, Icon = card.Icon 
                });
            }
        }

        var rng = new Random();
        state.Deck = state.Deck.OrderBy(x => rng.Next()).ToList();

        // Безопасное взятие карт на рынок (N+1, где N - кол-во игроков)
        int playerCount = GetPlayers(roomCode).Count;
        int marketSize = playerCount + 1; 

        state.Market = state.Deck.Take(marketSize).ToList();
        state.Deck.RemoveRange(0, marketSize); // Теперь тут точно хватит карт
    
        state.ActiveColor = (CardColor)rng.Next(0, 4);
    
        // Не забудь добавить поле Dictionary<string, GameState> _activeGames в класс сервиса!
        _activeGames[roomCode] = state; 
        return state;
    }
}

public class GameState
{
    public string RoomCode { get; set; }
    public List<Player> Players { get; set; } = new();
    public List<Card> Market { get; set; } = new();
    public List<Card> Deck { get; set; } = new();
    public CardColor ActiveColor { get; set; }
    public int CurrentPlayerIndex { get; set; } = 0;
}

public class Player
{
    public string Name { get; set; }
    public string ConnectionId { get; set; }
    public int Coins { get; set; } = 3;
    public List<Card> Inventory { get; set; } = new();
}
