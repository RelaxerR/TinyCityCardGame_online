namespace TinyCityCardGame_online.Models;

public enum CardColor { Blue, Green, Red, Purple }

public class Card
{
    public int Id { get; set; }
    public string Name { get; set; }
    public CardColor Color { get; set; }
    public int Cost { get; set; }
    public int Reward { get; set; }
    public string Icon { get; set; } // Например, "🌾", "🌲", "⚒️"
    public bool IsUsed { get; set; } = false; 
}
