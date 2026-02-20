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
            if (state == null) {
                Console.WriteLine($"[ERR] Комната {roomCode} не найдена");
                return;
            }

            var playerName = state.TurnOrder[state.CurrentTurnIndex];
            var player = state.Players.FirstOrDefault(p => p.Name == playerName);
    
            // Ищем карту. ВАЖНО: используем ID из аргумента
            var card = player?.Inventory.FirstOrDefault(c => c.Id == cardId);

            // ЛОГИРОВАНИЕ ДЛЯ ПРОВЕРКИ (Увидишь в терминале Rider)
            Console.WriteLine($"--- Активация карты ---");
            Console.WriteLine($"Игрок: {playerName}");
            Console.WriteLine($"Карта найдена: {card?.Name ?? "НЕТ"} (ID: {cardId})");
            if (card != null) {
                Console.WriteLine($"Цвет карты: {card.Color} | Активный цвет: {state.ActiveColor}");
                Console.WriteLine($"Использована: {card.IsUsed}");
            }

            // Проверка условий
            if (card == null || card.Color != state.ActiveColor || card.IsUsed) {
                Console.WriteLine("[WARN] Условия активации не соблюдены");
                return;
            }

            try 
            {
                Console.WriteLine($"Пытаюсь запустить эффект: '{card.Effect}' для карты {card.Name}");
                ExecuteEffect(card.Effect, player, state); // Выносим парсер в отдельный метод ниже
                card.IsUsed = true;
        
                if (player.Coins >= 100) {
                    await Clients.Group(roomCode).SendAsync("GameOver", player.Name);
                } else {
                    await BroadcastUpdate(roomCode, state);
                }
                Console.WriteLine("[OK] Эффект выполнен успешно");
            }
            catch (Exception ex) {
                Console.WriteLine($"[CRIT] Ошибка DSL: {ex.Message}");
            }
        }
        
        private void ExecuteEffect(string effect, Player player, GameState state)
        {
            if (string.IsNullOrEmpty(effect)) return;
            
            var parts = effect.Split(' ');
            var cmd = parts[0].ToUpper();
            var random = new Random();

            Console.WriteLine($"--- Лог эффекта [{cmd}] ---");
            Console.WriteLine($"Игрок {player.Name} (Баланс ДО: {player.Coins}💰)");

            try 
            {
                switch (cmd)
                {
                    case "GET": // GET 5
                        int getAmount = int.Parse(parts[1]);
                        player.Coins += getAmount;
                        Console.WriteLine($"[GET] Добавлено: +{getAmount}. Итог: {player.Coins}");
                        break;

                    case "GETALL": // GETALL 2
                        int allAmount = int.Parse(parts[1]);
                        foreach (var p in state.Players) {
                            p.Coins += allAmount;
                            Console.WriteLine($"[GETALL] Игрок {p.Name}: +{allAmount} (Итог: {p.Coins})");
                        }
                        break;

                    case "STEAL_MONEY": // STEAL_MONEY ALL 2
                        string target = parts[1].ToUpper();
                        int stealAmount = int.Parse(parts[2]);
                        var victims = target == "ALL" 
                            ? state.Players.Where(p => p.Name != player.Name).ToList()
                            : state.Players.Where(p => p.Name != player.Name).OrderBy(x => random.Next()).Take(1).ToList();

                        foreach (var v in victims) {
                            int actuallyStolen = Math.Min(v.Coins, stealAmount);
                            v.Coins -= actuallyStolen;
                            player.Coins += actuallyStolen;
                            Console.WriteLine($"[STEAL] У {v.Name} украдено {actuallyStolen}. У {player.Name} теперь {player.Coins}");
                        }
                        break;

                    case "STEAL_CARD": // STEAL_CARD RANDOM
                        var targets = state.Players.Where(p => p.Name != player.Name && p.Inventory.Any()).ToList();
                        if (targets.Any()) {
                            var victim = targets[random.Next(targets.Count)];
                            var stolen = victim.Inventory[random.Next(victim.Inventory.Count)];
                            victim.Inventory.Remove(stolen);
                            player.Inventory.Add(stolen);
                            Console.WriteLine($"[STEAL_CARD] {player.Name} украл '{stolen.Name}' у {victim.Name}");
                        } else {
                            Console.WriteLine("[STEAL_CARD] Не у кого красть карты.");
                        }
                        break;

                    case "GETBY": // GETBY Blue 2
                        var colorToMatch = Enum.Parse<CardColor>(parts[1], true);
                        int multiplier = int.Parse(parts[2]);
                        int count = player.Inventory.Count(c => c.Color == colorToMatch);
                        int totalBy = count * multiplier;
                        player.Coins += totalBy;
                        Console.WriteLine($"[GETBY] Найдено {count} карт цвета {colorToMatch}. Добавлено: {totalBy} (Итог: {player.Coins})");
                        break;
                        
                    default:
                        Console.WriteLine($"[WARN] Неизвестная команда: {cmd}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ошибка выполнения DSL '{effect}': {ex.Message}");
            }
            
            Console.WriteLine("-------------------------");
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
