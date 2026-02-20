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
    
            var card = player.Inventory.FirstOrDefault(c => c.Id == cardId);

            // Добавляем проверку: карта НЕ должна быть использована (IsUsed == false)
            if (card != null && card.Color == state.ActiveColor && !card.IsUsed)
            {
                player.Coins += card.Reward;
                card.IsUsed = true; // ПОМЕЧАЕМ КАК ИСПОЛЬЗОВАННУЮ

                // Проверка победы (если нужно сразу)
                if (player.Coins >= 100) {
                    await Clients.Group(roomCode).SendAsync("GameOver", player.Name);
                }

                await BroadcastUpdate(roomCode, state);
            }
        }

        public async Task EndTurn(string roomCode)
        {
            var state = _sessionService.GetGameState(roomCode);
            if (state == null) return;

            // ПЕРЕД переходом хода сбрасываем флаги активации у ВСЕХ игроков 
            // (или только у текущего, если правила позволяют активировать только в свой ход)
            foreach (var p in state.Players)
            {
                p.Inventory.ForEach(c => c.IsUsed = false);
            }

            // Смена хода
            state.CurrentTurnIndex = (state.CurrentTurnIndex + 1) % state.TurnOrder.Count;

            // Если круг замкнулся — меняем фазу
            if (state.CurrentTurnIndex == 0)
            {
                state.ActiveColor = (CardColor)new Random().Next(0, 4);
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
            var currentPlayerName = state.TurnOrder[state.CurrentTurnIndex];
            var player = state.Players.First(p => p.Name == currentPlayerName);
            var card = state.Market.FirstOrDefault(c => c.Id == cardId);

            // 1. ПРОВЕРКА ДЕНЕГ: Покупка происходит ТОЛЬКО если хватает золота
            if (card != null && player.Coins >= card.Cost)
            {
                player.Coins -= card.Cost;
                player.Inventory.Add(card);
                state.Market.Remove(card);

                if (state.Deck.Any())
                {
                    var nextCard = state.Deck[0];
                    state.Market.Add(nextCard);
                    state.Deck.RemoveAt(0);
                }
        
                // Рассылаем обновление сразу после удачной покупки
                await BroadcastUpdate(roomCode, state);
            }
            // Если денег не хватает — просто ничего не делаем, игрок может нажать другую карту или завершить ход
        }
    }
}
