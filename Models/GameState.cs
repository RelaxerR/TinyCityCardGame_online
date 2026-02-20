namespace TinyCityCardGame_online.Models;

public class GameState
{
    public string RoomCode { get; set; }
    public List<Player> Players { get; set; } = new();
    public List<Card> Market { get; set; } = new();
    public List<Card> Deck { get; set; } = new();
    public CardColor ActiveColor { get; set; }
    public int CurrentPlayerIndex { get; set; } = 0;
    
    public List<string> TurnOrder { get; set; } = new(); // Список имен по порядку хода
    public int CurrentTurnIndex { get; set; } = 0;
    public int RoundNumber { get; set; } = 1;
}
