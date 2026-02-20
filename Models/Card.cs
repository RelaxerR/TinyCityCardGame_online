namespace TinyCityCardGame_online.Models;

public enum CardColor { Blue, Gold, Red, Purple }

public class Card
{
    public int Id { get; set; }
    public string Name { get; set; }
    public CardColor Color { get; set; } // Синий (Цепная), Золотой (Доход), Красный (Кража монет), Фиолетовый (Кража карт)
    public int Cost { get; set; }
    public int Reward { get; set; }
    public string Icon { get; set; }
    public string Description { get; set; }
    public bool IsUsed { get; set; } = false;
}