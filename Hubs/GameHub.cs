using Microsoft.AspNetCore.SignalR;
using TinyCityCardGame_online.Services;
using TinyCityCardGame_online.Models;

namespace TinyCityCardGame_online.Hubs
{
    public class GameHub : Hub
    {
        private readonly GameSessionService _sessionService;

        public GameHub(GameSessionService sessionService)
        {
            _sessionService = sessionService;
        }

        // Вызывается в лобби
        public async Task JoinRoom(string roomCode, string userName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
            _sessionService.AddPlayer(roomCode, userName);
            
            var allPlayers = _sessionService.GetPlayers(roomCode);
            await Clients.Group(roomCode).SendAsync("UpdatePlayerList", allPlayers);
        }
        
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            // Находим, в какой комнате был игрок (упрощенно по Context.ConnectionId)
            // В идеале в GameSessionService нужно хранить связь ConnectionId -> RoomCode
            // Но для начала просто отправим уведомление всем, если знаем имя
            await Clients.All.SendAsync("PlayerDisconnected", "Один из поселенцев покинул остров...");
            await base.OnDisconnectedAsync(exception);
        }

        // Вызывается хостом для старта
        public async Task StartGame(string roomCode)
        {
            _sessionService.CreateGame(roomCode); 
            await Clients.Group(roomCode).SendAsync("GameStarted");
        }

        // !!! ТОТ САМЫЙ МЕТОД: Вызывается при загрузке страницы Play.cshtml
        public async Task InitGameView(string roomCode)
        {
            var state = _sessionService.GetGameState(roomCode);
            if (state != null)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
                
                // Отправляем текущее состояние стола
                await Clients.Group(roomCode).SendAsync("UpdateTable", new {
                    activeColor = state.ActiveColor.ToString(),
                    market = state.Market,
                    currentPlayer = state.TurnOrder[state.CurrentTurnIndex],
                    players = state.Players
                });
            }
        }
        
        // Метод для активации карты из инвентаря
        public async Task ActivateCard(string roomCode, int cardId)
        {
            var state = _sessionService.GetGameState(roomCode);
            if (state == null) return;

            var playerName = state.TurnOrder[state.CurrentTurnIndex];
            var player = state.Players.First(p => p.Name == playerName);
    
            // ВАЖНО: Ищем карту именно по Id в инвентаре текущего игрока
            var card = player.Inventory.FirstOrDefault(c => c.Id == cardId);

            if (card != null && card.Color == state.ActiveColor && !card.IsUsed)
            {
                player.Coins += card.Reward;
                card.IsUsed = true; 

                // Проверка на победу
                if (player.Coins >= 100) {
                    await Clients.Group(roomCode).SendAsync("GameOver", player.Name);
                }

                await BroadcastUpdate(roomCode, state);
            }
            else {
                // Если карта не найдена или условия не совпали
                System.Diagnostics.Debug.WriteLine($"Ошибка активации: CardID {cardId}, Found: {card != null}");
            }
        }

        public async Task EndTurn(string roomCode)
        {
            var state = _sessionService.GetGameState(roomCode);
            var currentPlayer = state.Players.First(p => p.Name == state.TurnOrder[state.CurrentTurnIndex]);
    
            // Сброс флагов текущего игрока
            currentPlayer.HasBoughtThisTurn = false;
            currentPlayer.Inventory.ForEach(c => c.IsUsed = false);

            state.CurrentTurnIndex = (state.CurrentTurnIndex + 1) % state.TurnOrder.Count;

            // ЕСЛИ КРУГ ЗАВЕРШИЛСЯ (все походили)
            if (state.CurrentTurnIndex == 0)
            {
                state.RoundNumber++;
                state.ActiveColor = (CardColor)new Random().Next(0, 4);
        
                // ЗАПОЛНЯЕМ ПУСТЫЕ МЕСТА НА РЫНКЕ
                int targetSize = state.Players.Count + 1;
                while (state.Market.Count < targetSize && state.Deck.Any())
                {
                    state.Market.Add(state.Deck[0]);
                    state.Deck.RemoveAt(0);
                }
            }

            await BroadcastUpdate(roomCode, state);
        }

        // Вспомогательный метод, чтобы не дублировать код рассылки
        private async Task BroadcastUpdate(string roomCode, GameState state)
        {
            await Clients.Group(roomCode).SendAsync("UpdateTable", new {
                activeColor = state.ActiveColor.ToString(),
                market = state.Market,
                currentPlayer = state.TurnOrder[state.CurrentTurnIndex],
                players = state.Players
            });
        }

        // Вызывается при клике на карту
        public async Task PlayerClickCard(string roomCode, int cardId)
        {
            var state = _sessionService.GetGameState(roomCode);
            var player = state.Players.First(p => p.Name == state.TurnOrder[state.CurrentTurnIndex]);
    
            // ПРОВЕРКА: Игрок еще не покупал в этом ходу и у него хватает денег
            if (player.HasBoughtThisTurn) return; 

            var card = state.Market.FirstOrDefault(c => c.Id == cardId);
            if (card != null && player.Coins >= card.Cost)
            {
                player.Coins -= card.Cost;
                player.HasBoughtThisTurn = true; // Блокируем дальнейшие покупки
                player.Inventory.Add(card);
                state.Market.Remove(card); // На рынке остается "дырка"

                await BroadcastUpdate(roomCode, state);
            }
        }
    }
}
