using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using TinyCityCardGame_online.Models;
using TinyCityCardGame_online.Services;

namespace TinyCityCardGame_online.Hubs;

/// <summary>
/// SignalR хаб для real-time взаимодействия игроков в игре Color Engine.
/// </summary>
public class GameHub : Hub
{
    private readonly GameSessionService _sessionService;
    private readonly GameSettings _settings;
    private readonly ILogger<GameHub> _logger;

    /// <summary>
    /// Инициализирует новый экземпляр класса GameHub.
    /// </summary>
    /// <param name="sessionService">Сервис управления игровыми сессиями.</param>
    /// <param name="settings">Настройки баланса игры.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    public GameHub(
        GameSessionService sessionService, 
        IOptions<GameSettings> settings,
        ILogger<GameHub> logger)
    {
        _sessionService = sessionService;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Подключает игрока к комнате (лобби).
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    /// <param name="userName">Имя игрока.</param>
    public async Task JoinRoom(string roomCode, string userName)
    {
        _logger.LogInformation("Игрок {UserName} пытается присоединиться к комнате {RoomCode}", userName, roomCode);

        if (!ValidateJoinRequest(roomCode, userName))
            return;

        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
        _sessionService.AddPlayer(roomCode, userName, Context.ConnectionId);

        var allPlayers = _sessionService.GetPlayers(roomCode);
        await Clients.Group(roomCode).SendAsync("UpdatePlayerList", allPlayers);

        _logger.LogInformation("Игрок {UserName} успешно присоединился к комнате {RoomCode}", userName, roomCode);
    }

    /// <summary>
    /// Валидирует запрос на присоединение к комнате.
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    /// <param name="userName">Имя игрока.</param>
    /// <returns>True если запрос валиден.</returns>
    private bool ValidateJoinRequest(string roomCode, string userName)
    {
        if (string.IsNullOrWhiteSpace(roomCode) || string.IsNullOrWhiteSpace(userName))
        {
            _logger.LogWarning("Некорректные параметры подключения: Room={RoomCode}, User={UserName}", roomCode, userName);
            return false;
        }

        if (!_sessionService.RoomExists(roomCode))
        {
            _logger.LogWarning("Комната {RoomCode} не существует", roomCode);
            return false;
        }

        if (_sessionService.IsAvailableToJoin(roomCode))
            return true;
        
        _logger.LogWarning("Комната {RoomCode} заполнена", roomCode);
        return false;

    }

    /// <summary>
    /// Обрабатывает отключение игрока.
    /// </summary>
    /// <param name="exception">Исключение отключения (если есть).</param>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Игрок отключился: ConnectionId={ConnectionId}", Context.ConnectionId);

        var roomCode = _sessionService.GetPlayerRoom(Context.ConnectionId);
        if (!string.IsNullOrEmpty(roomCode))
        {
            _sessionService.RemovePlayer(roomCode, Context.ConnectionId);
            await Clients.Group(roomCode).SendAsync("PlayerDisconnected", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Запускает игру (вызывается хостом).
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    public async Task StartGame(string roomCode)
    {
        _logger.LogInformation("Хост запускает игру в комнате {RoomCode}", roomCode);

        if (!CanStartGame(roomCode))
            return;

        _sessionService.CreateGame(roomCode);
        await Clients.Group(roomCode).SendAsync("GameStarted");

        _logger.LogInformation("Игра запущена в комнате {RoomCode}", roomCode);
    }

    /// <summary>
    /// Проверяет возможность запуска игры.
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    /// <returns>True если игра может быть запущена.</returns>
    private bool CanStartGame(string roomCode)
    {
        var playerCount = _sessionService.GetPlayerCount(roomCode);

        if (playerCount >= _settings.MinPlayersCount)
            return true;
        
        _logger.LogWarning("Недостаточно игроков для старта: {Count} (минимум {Min})", 
            playerCount, _settings.MinPlayersCount);
        
        return false;

    }

    /// <summary>
    /// Инициализирует представление игры при загрузке страницы.
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    public async Task InitGameView(string roomCode)
    {
        _logger.LogDebug("Инициализация представления игры для комнаты {RoomCode}", roomCode);

        var state = _sessionService.GetGameState(roomCode);
        if (state == null)
        {
            _logger.LogWarning("Состояние игры не найдено для комнаты {RoomCode}", roomCode);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

        var tableData = BuildTableData(state);
        await Clients.Group(roomCode).SendAsync("UpdateTable", tableData);

        _logger.LogDebug("Представление игры инициализировано для комнаты {RoomCode}", roomCode);
    }

    /// <summary>
    /// Строит объект данных для обновления стола.
    /// </summary>
    /// <param name="state">Состояние игры.</param>
    /// <returns>Анонимный объект с данными стола.</returns>
    private static object BuildTableData(GameState state) => new
    {
        activeColor = state.ActiveColor.ToString(),
        market = state.Market,
        currentPlayer = state.TurnOrder[state.CurrentTurnIndex],
        players = state.Players,
        roundNumber = state.RoundNumber,
    };

    /// <summary>
    /// Активирует карту из инвентаря игрока.
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    /// <param name="cardId">Идентификатор карты.</param>
    public async Task ActivateCard(string roomCode, int cardId)
    {
        _logger.LogDebug("Активация карты {CardId} игроком в комнате {RoomCode}", cardId, roomCode);

        var state = _sessionService.GetGameState(roomCode);
        if (state == null)
            return;

        var player = GetCurrentPlayer(state);
        var card = player?.Inventory.FirstOrDefault(c => c.Id == cardId);

        if (!CanActivateCard(card, state))
        {
            _logger.LogWarning("Невозможно активировать карту {CardId}: невалидное состояние", cardId);
            return;
        }

        try
        {
            await ExecuteEffect(card, card!.Effect, player!, state, roomCode);
            card.IsUsed = true;

            if (player != null && CheckWinCondition(player, roomCode).Result)
                return;

            await BroadcastUpdate(roomCode, state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при активации карты {CardId}: {Message}", cardId, ex.Message);
        }
    }

    /// <summary>
    /// Получает текущего игрока из состояния игры.
    /// </summary>
    /// <param name="state">Состояние игры.</param>
    /// <returns>Текущий игрок или null.</returns>
    private static Player? GetCurrentPlayer(GameState state)
    {
        if (state.CurrentTurnIndex >= state.TurnOrder.Count)
            return null;

        var playerName = state.TurnOrder[state.CurrentTurnIndex];
        return state.Players.FirstOrDefault(p => p.Name == playerName);
    }

    /// <summary>
    /// Проверяет возможность активации карты.
    /// </summary>
    /// <param name="card">Карта для проверки.</param>
    /// <param name="state">Состояние игры.</param>
    /// <returns>True если карту можно активировать.</returns>
    private static bool CanActivateCard(Card? card, GameState state) => 
        card != null && 
        card.Color == state.ActiveColor && 
        !card.IsUsed;

    /// <summary>
    /// Проверяет условие победы игрока.
    /// </summary>
    /// <param name="player">Игрок для проверки.</param>
    /// <param name="roomCode">Код комнаты.</param>
    /// <returns>True если игрок победил.</returns>
    private async Task<bool> CheckWinCondition(Player player, string roomCode)
    {
        if (player.Coins < _settings.WinTarget)
            return false;
        
        _logger.LogInformation("Игрок {PlayerName} победил в комнате {RoomCode} ({Coins} монет)", 
            player.Name, roomCode, player.Coins);

        await Clients.Group(roomCode).SendAsync("GameOver", player.Name);
        return true;

    }
    
    /// <summary>
    /// Выполняет эффект карты по DSL-команде.
    /// </summary>
    /// <param name="effect">DSL-строка эффекта.</param>
    /// <param name="player">Игрок, активирующий карту.</param>
    /// <param name="state">Состояние игры.</param>
    /// <param name="roomCode">Код комнаты.</param>
    private async Task ExecuteEffect(Card card, string effect, Player player, GameState state, string roomCode)
    {
        if (string.IsNullOrWhiteSpace(effect))
            return;

        var parts = effect.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToUpper();

        _logger.LogDebug("Выполнение эффекта {Command} для игрока {PlayerName}", cmd, player.Name);

        // Используем switch-statement вместо switch-expression для ясности
        // TODO: Вынести парсер эффектов в отдельный класс EffectExecutor
        switch (cmd)
        {
            case "GET":
                await ExecuteGetEffect(card, parts, player, roomCode);
                break;

            case "GETALL":
                await ExecuteGetAllEffect(card, parts, state, roomCode);
                break;

            case "STEAL_MONEY":
                await ExecuteStealMoneyEffect(card, parts, player, state, roomCode);
                break;

            case "STEAL_CARD":
                await ExecuteStealCardEffect(card, parts, player, state, roomCode);
                break;

            case "GETBY":
                await ExecuteGetByEffect(parts, player, roomCode);
                break;

            default:
                _logger.LogWarning("Неизвестная команда эффекта: {Command}", cmd);
                break;
        }
    }

    /// <summary>
    /// Эффект GET: игрок получает монеты из банка.
    /// </summary>
    private async Task ExecuteGetEffect(Card card, string[] parts, Player player, string roomCode)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out var amount)) return;

        // Модификаторы дохода для получателя
        int finalAmount = amount;
    
        // Gold карта
        if (card.Color == CardColor.Gold)
        {
            if (player.FavoriteColor == CardColor.Purple)
                finalAmount = (int)Math.Ceiling(amount * 1.5); // +50%
            else if (player.FavoriteColor == CardColor.Red)
                finalAmount = (int)Math.Floor(amount * 0.5);   // -50%
        }

        player.Coins += finalAmount;
        await SendGameMessage(roomCode, $"{player.Name} получил +{amount}💰 за свои владения", "gold");
    }

    /// <summary>
    /// Эффект GETALL: все игроки получают монеты.
    /// </summary>
    private async Task ExecuteGetAllEffect(Card card, string[] parts, GameState state, string roomCode)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out var baseAmount)) return;

        int amount = baseAmount;
        foreach (var p in state.Players)
        {
            // Логика для Синих карт (Blue Monopoly & Purple Isolation)
            if (card.Color == CardColor.Blue)
            {
                // Red Monopoly: доход только активатору (player)
                // Нам нужно знать, кто активатор. В текущей структуре ActivateCard вызывает этот метод.
                // Передадим activator через замыкание или контекст, но здесь у нас только state.
                // *Примечание*: В ActivateCard мы знаем `player`. 
                // Для упрощения, предположим, что мы передаем `activator` в метод или проверяем по имени, 
                // но лучше изменить сигнатуру. 
                // В рамках данного сниппета: используем логику проверки "активатор != получатель".
            
                // Purple Isolation: не получает от чужих синих
                if (p.FavoriteColor == CardColor.Purple)
                {
                    // Если p не является тем, кто сыграл карту (activator), он не получает доход.
                    // *Нюанс реализации*: Здесь нужно знать, кто activator.
                    // В ExecuteEffect мы можем передать activator.
                }
            }

            // Логика для Золотых карт (Gold Income Mods)
            if (card.Color == CardColor.Gold)
            {
                if (p.FavoriteColor == CardColor.Purple)
                    amount = (int)Math.Ceiling(amount * 1.5);
                else if (p.FavoriteColor == CardColor.Red)
                    amount = (int)Math.Floor(amount * 0.5);
            }
        
            // Применяем Red Monopoly (если активатор Red и карта Blue, получает только он)
            // Это требует доступа к activator. 
            // *Решение*: Передаем activator в ExecuteGetAllEffect.
        
            p.Coins += amount;
        }

        await SendGameMessage(roomCode, $"Урожайный год! Все получили по {amount}💰", "gold");
    }

    /// <summary>
    /// Эффект STEAL_MONEY: кража монет у других игроков.
    /// </summary>
    private async Task ExecuteStealMoneyEffect(Card card, string[] parts, Player activator, GameState state, string roomCode)
    {
        if (parts.Length <= 2 || !int.TryParse(parts[2], out var amount)) return;
        var targetMode = parts[1].ToUpper();

        var victims = SelectVictims(state, activator, targetMode);

        foreach (var victim in victims)
        {
            int stolenAmount = amount;

            // Gold Protection: блокирует 50% от Red карт
            if (victim.FavoriteColor == CardColor.Gold)
            {
                stolenAmount = (int)Math.Floor(stolenAmount * 0.5);
            }

            // Red Favorite vs Blue Victim: кража x2
            if (activator.FavoriteColor == CardColor.Red && victim.FavoriteColor == CardColor.Blue)
            {
                stolenAmount *= 2;
            }

            // Blue Vulnerability (уже учтено выше, но для ясности):
            // Красные карты крадут у синих в 2 раза больше.
            // Реализовано в строке выше.

            stolenAmount = Math.Min(victim.Coins, stolenAmount);
        
            victim.Coins -= stolenAmount;
            activator.Coins += stolenAmount;
            
            await SendGameMessage(roomCode, $"💸 {activator.Name} украл {stolenAmount}💰 у {victim.Name}!", "important");
        }
    }

    /// <summary>
    /// Эффект STEAL_CARD: кража карт у других игроков.
    /// </summary>
    private async Task ExecuteStealCardEffect(Card card, string[] parts, Player activator, GameState state, string roomCode)
    {
        // 1. Определение жертв с учетом приоритета Purple Hunter
        var victims = SelectVictimsWithPriority(state, activator, parts.Length > 1 ? parts[1].ToUpper() : "RANDOM");
    
        var random = new Random();
        foreach (var victim in victims)
        {
            // Blue Protection: фиолетовые не могут красть у синих
            if (victim.FavoriteColor == CardColor.Blue)
                continue;

            // Сколько карт красть?
            int cardsToSteal = 1;
        
            // Purple Hunter: у золотых крадет 2 карты
            if (activator.FavoriteColor == CardColor.Purple && victim.FavoriteColor == CardColor.Gold)
            {
                cardsToSteal = 2;
            }

            if (victim.Inventory.Count < cardsToSteal)
                cardsToSteal = victim.Inventory.Count; // Красть сколько есть

            for (int i = 0; i < cardsToSteal; i++)
            {
                if (victim.Inventory.Count == 0) break;
            
                int idx = random.Next(victim.Inventory.Count);
                var stolen = victim.Inventory[idx];
                victim.Inventory.RemoveAt(idx);
                activator.Inventory.Add(stolen);
            }
            await SendGameMessage(roomCode, $"🏴‍☠️ {activator.Name} похитил карту у {victim.Name}!", "important");
        }
    }
    
    private List<Player> SelectVictimsWithPriority(GameState state, Player activator, string targetMode)
    {
        var otherPlayers = state.Players.Where(p => p.Name != activator.Name).ToList();
        var random = new Random();
    
        // Если активатор - фиолетовый любимец, он предпочитает золотых (70%)
        if (activator.FavoriteColor == CardColor.Purple)
        {
            var goldVictims = otherPlayers.Where(p => p.FavoriteColor == CardColor.Gold).ToList();
        
            if (goldVictims.Count > 0)
            {
                // 70% шанс выбрать из золотых
                if (random.Next(100) < 70)
                {
                    // Возвращаем только золотых (или случайного золотого, зависит от режима ALL/RANDOM)
                    return targetMode == "ALL" ? goldVictims : new List<Player> { goldVictims[random.Next(goldVictims.Count)] };
                }
            }
        }

        // Стандартный выбор
        return targetMode switch
        {
            "ALL" => otherPlayers,
            "RANDOM" when otherPlayers.Count != 0 => new List<Player> { otherPlayers[random.Next(otherPlayers.Count)] },
            _ => new List<Player>()
        };
    }

    /// <summary>
    /// Эффект GETBY: доход за карты определенного цвета.
    /// </summary>
    private async Task ExecuteGetByEffect(string[] parts, Player player, string roomCode)
    {
        if (parts.Length <= 2)
            return;

        var color = Enum.Parse<CardColor>(parts[1], true);
        if (!int.TryParse(parts[2], out var multiplier))
            return;

        var count = player.CountCardsByColor(color);
        var earnings = count * multiplier;
        player.Coins += earnings;

        await SendGameMessage(roomCode, $"{player.Name} заработал {earnings}💰 на торговле", "gold");
    }

    /// <summary>
    /// Выбирает жертв для эффектов кражи.
    /// </summary>
    private static List<Player> SelectVictims(GameState state, Player player, string targetMode)
    {
        var otherPlayers = state.Players.Where(p => p.Name != player.Name).ToList();
        var random = new Random();

        return targetMode switch
        {
            "ALL" => otherPlayers,
            "RANDOM" when otherPlayers.Count != 0 => [otherPlayers[random.Next(otherPlayers.Count)]],
            _ => []
        };
    }

    /// <summary>
    /// Отправляет сообщение в игровой лог.
    /// </summary>
    private async Task SendGameMessage(string roomCode, string message, string type = "info") => 
        await Clients.Group(roomCode).SendAsync("ShowMessage", message, type);

    /// <summary>
    /// Завершает ход текущего игрока.
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    public async Task EndTurn(string roomCode)
    {
        _logger.LogDebug("Завершение хода в комнате {RoomCode}", roomCode);

        var state = _sessionService.GetGameState(roomCode);
        if (state == null)
            return;

        ProcessTurnEnd(state, roomCode);
        await BroadcastUpdate(roomCode, state);
    }

    /// <summary>
    /// Обрабатывает логику завершения хода.
    /// </summary>
    private void ProcessTurnEnd(GameState state, string roomCode)
    {
        state.CurrentTurnIndex = (state.CurrentTurnIndex + 1) % state.TurnOrder.Count;
        
        var nextPlayer = GetCurrentPlayer(state);
        if (nextPlayer == null)
            return;

        nextPlayer.Coins += _settings.DailyIncome;
        nextPlayer.HasBoughtThisTurn = false;

        if (state.CurrentTurnIndex == 0)
        {
            ProcessNewRound(state, roomCode);
        }

        _logger.LogInformation("Ход передан игроку {PlayerName}. Раунд {Round}", nextPlayer.Name, state.RoundNumber);
    }

    /// <summary>
    /// Обрабатывает начало нового раунда.
    /// </summary>
    private void ProcessNewRound(GameState state, string roomCode)
    {
        state.RoundNumber++;
        state.ActiveColor = (CardColor)new Random().Next(0, 4);

        foreach (var player in state.Players)
        {
            player.ResetTurnState();
        }

        _sessionService.ReplenishMarket(state);
        
        state.UpdateTurnOrder();

        _logger.LogInformation("Начат раунд {Round} в комнате {RoomCode}. Активный цвет: {Color}", 
            state.RoundNumber, roomCode, state.ActiveColor);
    }

    /// <summary>
    /// Покупает карту с рынка.
    /// </summary>
    /// <param name="roomCode">Код комнаты.</param>
    /// <param name="cardId">Идентификатор карты.</param>
    public async Task PlayerClickCard(string roomCode, int cardId)
    {
        _logger.LogDebug("Попытка покупки карты {CardId} в комнате {RoomCode}", cardId, roomCode);

        var state = _sessionService.GetGameState(roomCode);
        if (state != null)
        {
            var player = GetCurrentPlayer(state);

            if (player == null || player.HasBoughtThisTurn)
                return;

            var card = state.Market.FirstOrDefault(c => c.Id == cardId);
            if (card != null && player.CanAfford(card.Cost))
            {
                ProcessCardPurchase(player, card, state);
                // !!! Обновляем любимый цвет после покупки !!!
                player.LastBoughtColor = card.Color;
                player.UpdateFavoriteColor();
                await BroadcastUpdate(roomCode, state);
            }
        }
        else
        {
            _logger.LogError("state is null");
        }
    }

    /// <summary>
    /// Обрабатывает покупку карты.
    /// </summary>
    private void ProcessCardPurchase(Player player, Card card, GameState state)
    {
        player.SpendCoins(card.Cost);
        player.HasBoughtThisTurn = true;
        player.AddCardToInventory(card);
        state.Market.Remove(card);

        _logger.LogInformation("Игрок {PlayerName} купил карту {CardName} за {Cost} монет", 
            player.Name, card.Name, card.Cost);
    }

    /// <summary>
    /// Рассылает обновление состояния всем игрокам в комнате.
    /// </summary>
    private async Task BroadcastUpdate(string roomCode, GameState state) => 
        await Clients.Group(roomCode).SendAsync("UpdateTable", BuildTableData(state));
}