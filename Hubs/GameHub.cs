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
            var player = state.Players.FirstOrDefault(p => p.Name == playerName);
            var card = player?.Inventory.FirstOrDefault(c => c.Id == cardId);

            if (card == null || card.Color != state.ActiveColor || card.IsUsed) return;

            try 
            {
                // ПЕРЕДАЕМ roomCode ТРЕТЬИМ АРГУМЕНТОМ
                await ExecuteEffect(card.Effect, player, state, roomCode); 
        
                card.IsUsed = true;
        
                if (player.Coins >= 100) {
                    await Clients.Group(roomCode).SendAsync("GameOver", player.Name);
                } else {
                    await BroadcastUpdate(roomCode, state);
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }
        }
        
        private async Task ExecuteEffect(string effect, Player player, GameState state, string roomCode)
        {
            if (string.IsNullOrWhiteSpace(effect)) return;
            
            var parts = effect.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToUpper();
            var random = new Random();

            switch (cmd)
            {
                case "GET":
                    int amt = int.Parse(parts[1]);
                    player.Coins += amt;
                    // Отправляем в лог
                    await Clients.Group(roomCode).SendAsync("ShowMessage", $"{player.Name} получил +{amt}💰 за свои владения", "gold");
                    break;

                case "GETALL":
                    int bonus = int.Parse(parts[1]);
                    foreach (var p in state.Players) p.Coins += bonus;
                    await Clients.Group(roomCode).SendAsync("ShowMessage", $"Урожайный год! Все получили по {bonus}💰", "gold");
                    break;

                case "STEAL_MONEY":
                    int sAmt = int.Parse(parts[2]);
                    var victims = state.Players.Where(p => p.Name != player.Name).ToList();
                    foreach (var v in victims) {
                        int stolen = Math.Min(v.Coins, sAmt);
                        v.Coins -= stolen; player.Coins += stolen;
                    }
                    await Clients.Group(roomCode).SendAsync("ShowMessage", $"⚔️ {player.Name} собрал дань с соседей по {sAmt}💰!", "important");
                    break;

                case "STEAL_CARD":
                    var targets = state.Players.Where(p => p.Name != player.Name && p.Inventory.Any()).ToList();
                    if (targets.Any()) {
                        var victim = targets[random.Next(targets.Count)];
                        var stolen = victim.Inventory[random.Next(victim.Inventory.Count)];
                        victim.Inventory.Remove(stolen);
                        player.Inventory.Add(stolen);
                        await Clients.Group(roomCode).SendAsync("ShowMessage", $"🏴‍☠️ {player.Name} похитил '{stolen.Name}' у {victim.Name}!", "important");
                    }
                    break;

                case "GETBY":
                    var color = Enum.Parse<CardColor>(parts[1], true);
                    int mult = int.Parse(parts[2]);
                    int count = player.Inventory.Count(c => c.Color == color);
                    player.Coins += count * mult;
                    await Clients.Group(roomCode).SendAsync("ShowMessage", $"{player.Name} заработал {count * mult}💰 на торговле", "gold");
                    break;
            }
        }
        
        public async Task EndTurn(string roomCode)
        {
            var state = _sessionService.GetGameState(roomCode);
            if (state == null) return;

            // 1. Смена игрока
            state.CurrentTurnIndex = (state.CurrentTurnIndex + 1) % state.TurnOrder.Count;
    
            // 2. Начисление монеты за начало хода
            var nextPlayer = state.Players.First(p => p.Name == state.TurnOrder[state.CurrentTurnIndex]);
            nextPlayer.Coins += 1;

            // 3. ЕСЛИ НАЧАЛСЯ НОВЫЙ КРУГ (вернулись к первому игроку)
            if (state.CurrentTurnIndex == 0)
            {
                state.RoundNumber++; // Обновляем номер раунда
                state.ActiveColor = (CardColor)new Random().Next(0, 4); // Новый цвет

                // СБРОС: Все карты всех игроков снова готовы к активации
                foreach (var p in state.Players)
                {
                    p.Inventory.ForEach(c => c.IsUsed = false);
                }

                // Пополнение рынка (если были покупки)
                int targetSize = state.Players.Count + 1;
                while (state.Market.Count < targetSize && state.Deck.Any())
                {
                    state.Market.Add(state.Deck[0]);
                    state.Deck.RemoveAt(0);
                }
            }
    
            // Сброс флага покупки только для того, КТО СЕЙЧАС БУДЕТ ХОДИТЬ
            nextPlayer.HasBoughtThisTurn = false;
            
            await Clients.Group(roomCode).SendAsync("ShowMessage", $"{nextPlayer.Name} получает 1💰 на развитие поселения.");
            await BroadcastUpdate(roomCode, state);
        }

        // Вспомогательный метод, чтобы не дублировать код рассылки
        private async Task BroadcastUpdate(string roomCode, GameState state)
        {
            // Отправляем анонимный объект со всеми данными стола
            await Clients.Group(roomCode).SendAsync("UpdateTable", new {
                activeColor = state.ActiveColor.ToString(),
                market = state.Market,
                currentPlayer = state.TurnOrder[state.CurrentTurnIndex],
                players = state.Players, // <--- САМОЕ ВАЖНОЕ: тут новые балансы!
                roundNumber = state.RoundNumber
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
