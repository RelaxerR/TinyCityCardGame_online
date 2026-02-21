namespace TinyCityCardGame_online.Models;

public class GameSettings {
    public int StartCoinsMin { get; set; }
    public int StartCoinsMax { get; set; }
    public int WinTarget { get; set; }
    public int DailyIncome { get; set; }
    public int MinPlayersCount { get; set; }
    public int MaxPlayersCount { get; set; }
    public string MarketSizeFormula { get; set; } = "{players_count} + 1";
    
    public bool IsValidPlayerCount(int count) => 
        count >= MinPlayersCount && count <= MaxPlayersCount;
}
