namespace TinyCityCardGame_online.Services;

public class GameSessionService
{
    // Словарь: КодКомнаты -> Список Имен
    private readonly Dictionary<string, List<string>> _rooms = new();

    public void AddPlayer(string roomCode, string userName)
    {
        if (!_rooms.ContainsKey(roomCode)) _rooms[roomCode] = new List<string>();
        if (!_rooms[roomCode].Contains(userName)) _rooms[roomCode].Add(userName);
    }

    public List<string> GetPlayers(string roomCode) => 
        _rooms.ContainsKey(roomCode) ? _rooms[roomCode] : new List<string>();
}
