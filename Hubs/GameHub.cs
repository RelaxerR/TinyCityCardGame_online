using Microsoft.AspNetCore.SignalR;
using TinyCityCardGame_online.Services;

namespace TinyCityCardGame_online.Hubs;

public class GameHub : Hub
{
    private readonly GameSessionService _sessionService;
    public GameHub(GameSessionService sessionService) => _sessionService = sessionService;

    public async Task JoinRoom(string roomCode, string userName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
        
        // Сохраняем игрока
        _sessionService.AddPlayer(roomCode, userName);
        
        // Получаем актуальный список всех игроков в этой комнате
        var allPlayers = _sessionService.GetPlayers(roomCode);
        
        // Рассылаем ВСЕМ в комнате обновленный список
        await Clients.Group(roomCode).SendAsync("UpdatePlayerList", allPlayers);
    }
}
