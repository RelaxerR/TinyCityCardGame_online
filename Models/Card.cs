namespace TinyCityCardGame_online.Models;

public enum CardColor { Blue, Gold, Red, Purple }

public class Card
{
    public int Id { get; set; }
    public string Name { get; set; }
    public CardColor Color { get; set; }
    public string Effect { get; set; } // Пример: "STEAL_MONEY ALL 2" или "GET 5"
    public int Cost { get; set; }
    public int Reward { get; set; } // Можно оставить как базовое значение для GET
    public string Icon { get; set; }
    public string Description { get; set; }
    public bool IsUsed { get; set; } = false;
    public int Weight { get; set; }
}
