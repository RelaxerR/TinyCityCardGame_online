namespace TinyCityCardGame_online.Models;

public class Player
{
    public string Name { get; set; }
    public string ConnectionId { get; set; }
    public int Coins { get; set; } = 3;
    public List<Card> Inventory { get; set; } = new();
    public bool HasBoughtThisTurn { get; set; } = false;
}