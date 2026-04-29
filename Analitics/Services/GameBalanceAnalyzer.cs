using Microsoft.Extensions.Options;
using TinyCityCardGame_online.Models;
using TinyCityCardGame_online.Services;

namespace TinyCityCardGame_online.Analitics.Services;

/// <summary>
/// Продвинутый анализатор баланса игры методом Монте-Карло.
/// Симулирует полные игровые сессии с учётом механики «Любимый цвет»,
/// конфронтации и прогрессии контента.
/// </summary>
public class GameBalanceAnalyzer
{
    private readonly List<Card> _baseCards;
    private readonly GameSettings _settings;
    private readonly ILogger<GameBalanceAnalyzer> _logger;
    private readonly Random _rng;

    // Конфигурация симуляции
    private const int DefaultGameSimulations = 1000;
    private const int MaxRounds = 20;
    private const int DefaultPlayers = 4;
    private const int WinTarget = 100;

    /// <summary>
    /// Результаты симуляции одной игры.
    /// </summary>
    public record GameSimulationResult
    {
        public int GameId { get; init; }
        public int RoundsPlayed { get; init; }
        public string Winner { get; init; } = string.Empty;
        public Dictionary<string, PlayerEndStats> PlayerStats { get; init; } = new();
        public List<FavoriteColorEvent> FavoriteColorEvents { get; init; } = new();
        public List<InteractionEvent> InteractionEvents { get; init; } = new();
        public CardActivationStats CardActivations { get; init; } = new();
    }

    /// <summary>
    /// Статистика игрока в конце игры.
    /// </summary>
    public record PlayerEndStats
    {
        public string Name { get; init; } = string.Empty;
        public int FinalCoins { get; init; }
        public int CardsOwned { get; init; }
        public CardColor? FinalFavoriteColor { get; init; }
        public int TimesFavoriteColorChanged { get; init; }
        public int IncomeFromBlue { get; init; }
        public int IncomeFromGold { get; init; }
        public int StolenByRed { get; init; }
        public int StolenFromOthers { get; init; }
        public int CardsStolen { get; init; }
        public int CardsLost { get; init; }
        public double WinProbabilityContribution { get; init; }
    }

    /// <summary>
    /// Событие изменения любимого цвета.
    /// </summary>
    public record FavoriteColorEvent
    {
        public int Round { get; init; }
        public string PlayerName { get; init; } = string.Empty;
        public CardColor? OldColor { get; init; }
        public CardColor NewColor { get; init; }
        public string Trigger { get; init; } = string.Empty; // "card_purchase", "card_activation", "tie_break"
    }

    /// <summary>
    /// Событие взаимодействия между игроками.
    /// </summary>
    public record InteractionEvent
    {
        public int Round { get; init; }
        public string Attacker { get; init; } = string.Empty;
        public string? Victim { get; init; }
        public InteractionType Type { get; init; }
        public int Amount { get; init; }
        public CardColor? AttackerFavorite { get; init; }
        public CardColor? VictimFavorite { get; init; }
        public string CardName { get; init; } = string.Empty;
    }

    public enum InteractionType
    {
        StealMoney,
        StealCard,
        BlueIncomeShared,
        GoldIncomePersonal,
        PurpleSpecial
    }

    /// <summary>
    /// Статистика активаций карт.
    /// </summary>
    public record CardActivationStats
    {
        public Dictionary<int, CardActivationData> ByCardId { get; init; } = new();
        
        public void AddActivation(int cardId, string cardName, CardColor color, int cost, int reward, bool wasEffective)
        {
            if (!ByCardId.ContainsKey(cardId))
                ByCardId[cardId] = new CardActivationData { CardName = cardName, CardColor = color, Cost = cost, BaseReward = reward };
            ByCardId[cardId].ActivationCount++;
            if (wasEffective) ByCardId[cardId].EffectiveCount++;
        }
    }

    public record CardActivationData
    {
        public string CardName { get; init; } = string.Empty;
        public CardColor CardColor { get; init; }
        public int Cost { get; init; }
        public int BaseReward { get; init; }
        public int ActivationCount { get; set; }
        public int EffectiveCount { get; set; }
        
        public double ActivationRate => ActivationCount > 0 ? (double)EffectiveCount / ActivationCount : 0;
        public double ExpectedValue => BaseReward * ActivationRate;
        public double PaybackRounds => ExpectedValue > 0 ? Cost / ExpectedValue : double.MaxValue;
    }

    /// <summary>
    /// Инициализирует новый экземпляр анализатора.
    /// </summary>
    public GameBalanceAnalyzer(
        CardLoader loader,
        IOptions<GameSettings> settings,
        ILogger<GameBalanceAnalyzer> logger)
    {
        _baseCards = loader.LoadCardsFromExcel("Cards.xlsx");
        _settings = settings.Value;
        _logger = logger;
        _rng = new Random(42); // Фиксированный seed для воспроизводимости
    }

    /// <summary>
    /// Запускает полный анализ баланса с симуляцией игр.
    /// </summary>
    public async Task<BalanceAnalysisReport> RunFullAnalysis(int gameSimulations = DefaultGameSimulations)
    {
        _logger.LogInformation("🎲 Запуск полного анализа баланса: {Simulations} симуляций", gameSimulations);
        
        var results = new List<GameSimulationResult>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            for (int i = 0; i < gameSimulations; i++)
            {
                results.Add(SimulateFullGame(i));
                
                // show visible progress (Information level)
                if (i % 100 == 0 && i > 0)
                    _logger.LogInformation("Прогресс: {Completed}/{Total} игр", i, gameSimulations);

                // yield periodically so the scheduler/logs can flush and other work can run
                if (i % 10 == 0)
                    await Task.Yield();
            }

            stopwatch.Stop();
            
            var report = CompileReport(results, stopwatch.Elapsed);
            PrintReport(report);
            
            _logger.LogInformation("✅ Анализ завершён за {ElapsedMs} мс", stopwatch.ElapsedMilliseconds);
            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при выполнении анализа баланса");
            throw;
        }
    }

    /// <summary>
    /// Симулирует одну полную игру.
    /// </summary>
    private GameSimulationResult SimulateFullGame(int gameId)
    {
        var players = InitializePlayers();
        var market = new List<Card>();
        var deck = new List<Card>(_baseCards);
        var favoriteColorEvents = new List<FavoriteColorEvent>();
        var interactionEvents = new List<InteractionEvent>();
        var cardActivations = new CardActivationStats();
        
        int round = 1;
        string? winner = null;

        while (round <= MaxRounds && winner == null)
        {
            // 1. Генерация активного цвета
            var activeColor = SelectActiveColor();
            
            // 2. Пополнение рынка (раунд-зависимое)
            ReplenishMarket(market, deck, round);
            
            // 3. Ходы игроков
            foreach (var player in players.OrderBy(p => p.Coins)) // Лидеры ходят позже
            {
                if (winner != null) break;
                
                // Фаза покупки (1 карта за ход)
                if (!player.HasBoughtThisTurn)
                {
                    AttemptPurchase(player, market, round);
                }
                
                // Фаза активации карт нужного цвета
                ActivatePlayerCards(player, activeColor, cardActivations, players, 
                    interactionEvents, favoriteColorEvents, round, gameId);
                
                // Проверка победы
                if (player.Coins >= WinTarget)
                {
                    winner = player.Name;
                    break;
                }
                
                // Ежедневный доход
                player.Coins += _settings.DailyIncome;
                player.ResetTurnState();
            }
            
            // Обновление любимых цветов после раунда
            foreach (var player in players)
            {
                var oldColor = player.FavoriteColor;
                player.UpdateFavoriteColor();
                
                if (player.FavoriteColor != oldColor && player.FavoriteColor.HasValue)
                {
                    favoriteColorEvents.Add(new FavoriteColorEvent
                    {
                        Round = round,
                        PlayerName = player.Name,
                        OldColor = oldColor,
                        NewColor = player.FavoriteColor.Value,
                        Trigger = "round_end_recalc"
                    });
                }
            }
            
            round++;
        }

        return new GameSimulationResult
        {
            GameId = gameId,
            RoundsPlayed = round - 1,
            Winner = winner ?? players.OrderByDescending(p => p.Coins).First().Name,
            PlayerStats = players.ToDictionary(
                p => p.Name, 
                p => ExtractPlayerStats(p, players)),
            FavoriteColorEvents = favoriteColorEvents,
            InteractionEvents = interactionEvents,
            CardActivations = cardActivations
        };
    }

    /// <summary>
    /// Инициализирует игроков для симуляции.
    /// </summary>
    private List<SimPlayer> InitializePlayers()
    {
        var players = new List<SimPlayer>();
        for (int i = 0; i < DefaultPlayers; i++)
        {
            players.Add(new SimPlayer
            {
                Name = $"Player_{i + 1}",
                Coins = _settings.GenerateStartingCoins(),
                Inventory = new List<Card>(),
                HasBoughtThisTurn = false,
                FavoriteColor = null,
                LastBoughtColor = null
            });
        }
        return players;
    }

    /// <summary>
    /// Пополняет рынок картами.
    /// </summary>
    private void ReplenishMarket(List<Card> market, List<Card> deck, int roundNumber)
    {
        var targetSize = _settings.CalculateMarketSize(DefaultPlayers);
        var availableCards = deck.Where(c => c.Cost <= roundNumber).ToList();
        
        while (market.Count < targetSize && availableCards.Count > 0)
        {
            var selected = SelectWeightedCard(availableCards);
            if (selected != null)
            {
                market.Add(CloneCard(selected));
                availableCards.Remove(selected);
            }
        }
    }

    /// <summary>
    /// Пытается совершить покупку карты.
    /// </summary>
    private void AttemptPurchase(SimPlayer player, List<Card> market, int round)
    {
        var affordable = market.Where(c => c.Cost <= player.Coins).ToList();
        if (affordable.Count == 0) return;

        // Простая стратегия: покупает карту с лучшим соотношением reward/cost для своего любимого цвета
        var preferred = affordable
            .OrderByDescending(c => 
            {
                var baseRatio = (double)c.Reward / c.Cost;
                if (player.FavoriteColor == c.Color) return baseRatio * 1.5;
                return baseRatio;
            })
            .FirstOrDefault();

        if (preferred != null && player.SpendCoins(preferred.Cost))
        {
            player.Inventory.Add(CloneCard(preferred));
            player.HasBoughtThisTurn = true;
            player.LastBoughtColor = preferred.Color;
            market.Remove(preferred);
            
            // Проверяем изменение любимого цвета после покупки
            var oldFav = player.FavoriteColor;
            player.UpdateFavoriteColor();
            
            if (player.FavoriteColor != oldFav && player.FavoriteColor.HasValue)
            {
                // Событие изменения любимого цвета будет зафиксировано в конце раунда
            }
        }
    }

    /// <summary>
    /// Активирует карты игрока нужного цвета.
    /// </summary>
    private void ActivatePlayerCards(
        SimPlayer player, 
        CardColor activeColor,
        CardActivationStats cardActivations,
        List<SimPlayer> allPlayers,
        List<InteractionEvent> interactions,
        List<FavoriteColorEvent> favEvents,
        int round,
        int gameId)
    {
        // Take a snapshot of matching cards to avoid collection-modified exceptions
        foreach (var card in player.Inventory.Where(c => c.Color == activeColor && !c.IsUsed).ToList())
        {
            card.IsUsed = true;
            cardActivations.AddActivation(card.Id, card.Name, card.Color, card.Cost, card.Reward, true);
            
            var (command, parameters) = ParseEffect(card.Effect);
            
            switch (command)
            {
                case "GET":
                    ExecuteGet(card, parameters, player, interactions, round);
                    break;
                case "GETALL":
                    ExecuteGetAll(card, parameters, player, allPlayers, interactions, round);
                    break;
                case "STEAL_MONEY":
                    ExecuteStealMoney(card, parameters, player, allPlayers, interactions, round);
                    break;
                case "STEAL_CARD":
                    ExecuteStealCard(card, parameters, player, allPlayers, interactions, round);
                    break;
                case "GETBY":
                    ExecuteGetBy(card, parameters, player, interactions, round);
                    break;
            }
        }
    }

    /// <summary>
    /// Эффект GET: личный доход.
    /// </summary>
    private void ExecuteGet(Card card, string[] parameters, SimPlayer player, 
        List<InteractionEvent> interactions, int round)
    {
        if (parameters.Length < 1 || !int.TryParse(parameters[0], out var amount)) return;
        
        int finalAmount = ApplyFavoriteModifiers(card.Color, amount, player, isReceiver: true);
        player.Coins += finalAmount;
        
        interactions.Add(new InteractionEvent
        {
            Round = round,
            Attacker = player.Name,
            Type = InteractionType.GoldIncomePersonal,
            Amount = finalAmount,
            AttackerFavorite = player.FavoriteColor,
            CardName = card.Name
        });
    }

    /// <summary>
    /// Эффект GETALL: общий доход.
    /// </summary>
    private void ExecuteGetAll(Card card, string[] parameters, SimPlayer activator,
        List<SimPlayer> allPlayers, List<InteractionEvent> interactions, int round)
    {
        if (parameters.Length < 1 || !int.TryParse(parameters[0], out var baseAmount)) return;
        
        foreach (var recipient in allPlayers)
        {
            // Логика синих карт: доход всем, кроме изоляции фиолетового
            if (card.Color == CardColor.Blue)
            {
                if (recipient.FavoriteColor == CardColor.Purple && recipient != activator)
                    continue; // Purple isolation: не получает от чужих синих
                
                // Red monopoly: если активатор - красный любимец, получает только он
                if (activator.FavoriteColor == CardColor.Red && recipient != activator)
                    continue;
            }
            
            int amount = ApplyFavoriteModifiers(card.Color, baseAmount, recipient, isReceiver: true);
            recipient.Coins += amount;
            
            if (recipient != activator)
            {
                interactions.Add(new InteractionEvent
                {
                    Round = round,
                    Attacker = activator.Name,
                    Victim = recipient.Name,
                    Type = InteractionType.BlueIncomeShared,
                    Amount = amount,
                    AttackerFavorite = activator.FavoriteColor,
                    VictimFavorite = recipient.FavoriteColor,
                    CardName = card.Name
                });
            }
        }
    }

    /// <summary>
    /// Эффект STEAL_MONEY: кража монет.
    /// </summary>
    private void ExecuteStealMoney(Card card, string[] parameters, SimPlayer attacker,
        List<SimPlayer> allPlayers, List<InteractionEvent> interactions, int round)
    {
        if (parameters.Length < 2 || !int.TryParse(parameters[1], out var baseAmount)) return;
        
        var targetMode = parameters[0].ToUpper();
        var victims = SelectVictims(attacker, allPlayers, targetMode);
        
        foreach (var victim in victims)
        {
            int stolen = ApplyTheftModifiers(baseAmount, attacker, victim);
            stolen = Math.Min(stolen, victim.Coins);
            
            if (stolen > 0)
            {
                victim.Coins -= stolen;
                attacker.Coins += stolen;
                
                interactions.Add(new InteractionEvent
                {
                    Round = round,
                    Attacker = attacker.Name,
                    Victim = victim.Name,
                    Type = InteractionType.StealMoney,
                    Amount = stolen,
                    AttackerFavorite = attacker.FavoriteColor,
                    VictimFavorite = victim.FavoriteColor,
                    CardName = card.Name
                });
            }
        }
    }

    /// <summary>
    /// Эффект STEAL_CARD: кража карт.
    /// </summary>
    private void ExecuteStealCard(Card card, string[] parameters, SimPlayer attacker,
        List<SimPlayer> allPlayers, List<InteractionEvent> interactions, int round)
    {
        var targetMode = parameters.Length > 0 ? parameters[0].ToUpper() : "RANDOM";
        var victims = SelectVictimsWithPriority(attacker, allPlayers, targetMode);
        
        foreach (var victim in victims)
        {
            // Blue protection: фиолетовые не крадут у синих
            if (attacker.FavoriteColor == CardColor.Purple && victim.FavoriteColor == CardColor.Blue)
                continue;
            
            int cardsToSteal = (attacker.FavoriteColor == CardColor.Purple && victim.FavoriteColor == CardColor.Gold) ? 2 : 1;
            cardsToSteal = Math.Min(cardsToSteal, victim.Inventory.Count);
            
            for (int i = 0; i < cardsToSteal; i++)
            {
                if (victim.Inventory.Count == 0) break;
                
                var idx = _rng.Next(victim.Inventory.Count);
                var stolen = victim.Inventory[idx];
                victim.Inventory.RemoveAt(idx);
                attacker.Inventory.Add(stolen);
                
                interactions.Add(new InteractionEvent
                {
                    Round = round,
                    Attacker = attacker.Name,
                    Victim = victim.Name,
                    Type = InteractionType.StealCard,
                    Amount = 1,
                    AttackerFavorite = attacker.FavoriteColor,
                    VictimFavorite = victim.FavoriteColor,
                    CardName = card.Name
                });
            }
        }
    }

    /// <summary>
    /// Эффект GETBY: доход за карты цвета.
    /// </summary>
    private void ExecuteGetBy(Card card, string[] parameters, SimPlayer player,
        List<InteractionEvent> interactions, int round)
    {
        if (parameters.Length < 2) return;
        
        if (!Enum.TryParse<CardColor>(parameters[0], true, out var targetColor)) return;
        if (!int.TryParse(parameters[1], out var multiplier)) return;
        
        var count = player.Inventory.Count(c => c.Color == targetColor);
        var earnings = count * multiplier;
        
        // Применяем модификаторы золотого дохода
        if (targetColor == CardColor.Gold)
            earnings = ApplyFavoriteModifiers(CardColor.Gold, earnings, player, isReceiver: true);
        
        player.Coins += earnings;
        
        interactions.Add(new InteractionEvent
        {
            Round = round,
            Attacker = player.Name,
            Type = InteractionType.GoldIncomePersonal,
            Amount = earnings,
            AttackerFavorite = player.FavoriteColor,
            CardName = card.Name
        });
    }

    /// <summary>
    /// Применяет модификаторы любимого цвета к доходу.
    /// </summary>
    private int ApplyFavoriteModifiers(CardColor cardColor, int baseAmount, SimPlayer player, bool isReceiver)
    {
        if (player.FavoriteColor == null) return baseAmount;
        
        // Purple: +50% к золотому доходу
        if (cardColor == CardColor.Gold && player.FavoriteColor == CardColor.Purple && isReceiver)
            return (int)Math.Ceiling(baseAmount * 1.5);
        
        // Red: -50% к золотому доходу
        if (cardColor == CardColor.Gold && player.FavoriteColor == CardColor.Red && isReceiver)
            return (int)Math.Floor(baseAmount * 0.5);
        
        return baseAmount;
    }

    /// <summary>
    /// Применяет модификаторы к краже.
    /// </summary>
    private int ApplyTheftModifiers(int baseAmount, SimPlayer attacker, SimPlayer victim)
    {
        int amount = baseAmount;
        
        // Gold protection: блокирует 50% кражи от красных
        if (victim.FavoriteColor == CardColor.Gold)
            amount = (int)Math.Floor(amount * 0.5);
        
        // Red favorite vs Blue victim: кража x2
        if (attacker.FavoriteColor == CardColor.Red && victim.FavoriteColor == CardColor.Blue)
            amount *= 2;
        
        return Math.Max(0, amount);
    }

    /// <summary>
    /// Выбирает жертв с приоритетом для фиолетового охотника.
    /// </summary>
    private List<SimPlayer> SelectVictimsWithPriority(SimPlayer attacker, List<SimPlayer> allPlayers, string targetMode)
    {
        var others = allPlayers.Where(p => p.Name != attacker.Name).ToList();
        
        // Purple hunter: 70% приоритет золотым
        if (attacker.FavoriteColor == CardColor.Purple)
        {
            var goldTargets = others.Where(p => p.FavoriteColor == CardColor.Gold).ToList();
            if (goldTargets.Count > 0 && _rng.Next(100) < 70)
                return targetMode == "ALL" ? goldTargets : new List<SimPlayer> { goldTargets[_rng.Next(goldTargets.Count)] };
        }
        
        return SelectVictims(attacker, allPlayers, targetMode);
    }

    /// <summary>
    /// Стандартный выбор жертв.
    /// </summary>
    private List<SimPlayer> SelectVictims(SimPlayer attacker, List<SimPlayer> allPlayers, string targetMode)
    {
        var others = allPlayers.Where(p => p.Name != attacker.Name).ToList();
        
        return targetMode switch
        {
            "ALL" => others,
            "RANDOM" when others.Count > 0 => new List<SimPlayer> { others[_rng.Next(others.Count)] },
            _ => new List<SimPlayer>()
        };
    }

    /// <summary>
    /// Парсит эффект карты.
    /// </summary>
    private (string Command, string[] Parameters) ParseEffect(string effect)
    {
        if (string.IsNullOrWhiteSpace(effect)) return (string.Empty, Array.Empty<string>());
        
        var parts = effect.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0].ToUpper(), parts.Skip(1).ToArray());
    }

    /// <summary>
    /// Выбирает карту с учётом веса.
    /// </summary>
    private Card? SelectWeightedCard(List<Card> pool)
    {
        if (pool.Count == 0) return null;
        
        var totalWeight = pool.Sum(c => c.Weight);
        if (totalWeight <= 0) return pool[_rng.Next(pool.Count)];
        
        var roll = _rng.Next(0, totalWeight);
        var current = 0;
        
        foreach (var card in pool)
        {
            current += card.Weight;
            if (roll < current) return card;
        }
        
        return pool[_rng.Next(pool.Count)];
    }

    /// <summary>
    /// Создаёт копию карты.
    /// </summary>
    private static Card CloneCard(Card source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Color = source.Color,
        Effect = source.Effect,
        Cost = source.Cost,
        Reward = source.Reward,
        Icon = source.Icon,
        Description = source.Description,
        Narrative = source.Narrative,
        Weight = source.Weight
    };

    /// <summary>
    /// Выбирает активный цвет.
    /// </summary>
    private CardColor SelectActiveColor()
    {
        var total = _settings.ColorChanceBlue + _settings.ColorChanceGold + 
                   _settings.ColorChanceRed + _settings.ColorChancePurple;
        var roll = _rng.Next(0, total);
        
        if (roll < _settings.ColorChanceBlue) return CardColor.Blue;
        roll -= _settings.ColorChanceBlue;
        if (roll < _settings.ColorChanceGold) return CardColor.Gold;
        roll -= _settings.ColorChanceGold;
        if (roll < _settings.ColorChanceRed) return CardColor.Red;
        return CardColor.Purple;
    }

    /// <summary>
    /// Извлекает статистику игрока.
    /// </summary>
    private PlayerEndStats ExtractPlayerStats(SimPlayer player, List<SimPlayer> allPlayers)
    {
        return new PlayerEndStats
        {
            Name = player.Name,
            FinalCoins = player.Coins,
            CardsOwned = player.Inventory.Count,
            FinalFavoriteColor = player.FavoriteColor,
            TimesFavoriteColorChanged = 0, // TODO: track changes
            IncomeFromBlue = 0, // TODO: track
            IncomeFromGold = 0,
            StolenByRed = 0,
            StolenFromOthers = 0,
            CardsStolen = 0,
            CardsLost = 0,
            WinProbabilityContribution = 0
        };
    }

    /// <summary>
    /// Компилирует итоговый отчёт.
    /// </summary>
    private BalanceAnalysisReport CompileReport(List<GameSimulationResult> results, TimeSpan elapsed)
    {
        return new BalanceAnalysisReport
        {
            SimulationCount = results.Count,
            ElapsedTime = elapsed,
            AverageRounds = results.Average(r => r.RoundsPlayed),
            WinDistribution = results.GroupBy(r => r.Winner)
                .ToDictionary(g => g.Key, g => (double)g.Count() / results.Count * 100),
            FavoriteColorStats = AnalyzeFavoriteColors(results),
            CardBalanceStats = AnalyzeCardBalance(results),
            InteractionStats = AnalyzeInteractions(results),
            Recommendations = GenerateRecommendations(results)
        };
    }

    /// <summary>
    /// Анализирует статистику любимых цветов.
    /// </summary>
    private Dictionary<CardColor, ColorAnalysisData> AnalyzeFavoriteColors(List<GameSimulationResult> results)
    {
        var data = new Dictionary<CardColor, ColorAnalysisData>();
        
        foreach (CardColor color in Enum.GetValues(typeof(CardColor)))
        {
            var gamesWithColor = results
                .SelectMany(r => r.PlayerStats.Values)
                .Count(p => p.FinalFavoriteColor == color);
            
            var totalPlayers = results.Count * DefaultPlayers;
            
            data[color] = new ColorAnalysisData
            {
                Frequency = (double)gamesWithColor / totalPlayers * 100,
                AvgWinRate = results
                    .Where(r => r.PlayerStats.Values.Any(p => p.FinalFavoriteColor == color && p.Name == r.Winner))
                    .Count() / (double)results.Count * 100,
                AvgFinalCoins = results
                    .SelectMany(r => r.PlayerStats.Values)
                    .Where(p => p.FinalFavoriteColor == color)
                    .Average(p => p.FinalCoins)
            };
        }
        
        return data;
    }

    /// <summary>
    /// Анализирует баланс карт.
    /// </summary>
    private Dictionary<string, CardBalanceData> AnalyzeCardBalance(List<GameSimulationResult> results)
    {
        var stats = new Dictionary<string, CardBalanceData>();
        
        foreach (var gameResult in results)
        {
            foreach (var cardData in gameResult.CardActivations.ByCardId.Values)
            {
                var key = cardData.CardName;
                
                if (!stats.ContainsKey(key))
                {
                    stats[key] = new CardBalanceData
                    {
                        CardName = cardData.CardName,
                        Color = cardData.CardColor,
                        Cost = cardData.Cost,
                        BaseReward = cardData.BaseReward,
                        TotalActivations = 0,
                        EffectiveActivations = 0,
                        AvgPayback = 0
                    };
                }
                
                stats[key].TotalActivations += cardData.ActivationCount;
                stats[key].EffectiveActivations += cardData.EffectiveCount;
            }
        }
        
        foreach (var data in stats.Values)
        {
            var rate = data.TotalActivations > 0 
                ? (double)data.EffectiveActivations / data.TotalActivations 
                : 0;
            data.AvgPayback = rate > 0 && data.BaseReward > 0 
                ? data.Cost / (data.BaseReward * rate) 
                : double.MaxValue;
        }
        
        return stats;
    }

    /// <summary>
    /// Анализирует взаимодействия.
    /// </summary>
    private InteractionAnalysisData AnalyzeInteractions(List<GameSimulationResult> results)
    {
        var interactions = results.SelectMany(r => r.InteractionEvents).ToList();
        
        return new InteractionAnalysisData
        {
            TotalInteractions = interactions.Count,
            ByType = interactions.GroupBy(i => i.Type)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByColorMatch = interactions
                .Where(i => i.AttackerFavorite.HasValue && i.VictimFavorite.HasValue)
                .GroupBy(i => $"{i.AttackerFavorite}->{i.VictimFavorite}")
                .ToDictionary(g => g.Key, g => g.Count()),
            AvgTheftAmount = interactions
                .Where(i => i.Type == InteractionType.StealMoney)
                .Average(i => i.Amount)
        };
    }

    /// <summary>
    /// Генерирует рекомендации по балансу.
    /// </summary>
    private List<BalanceRecommendation> GenerateRecommendations(List<GameSimulationResult> results)
    {
        var recommendations = new List<BalanceRecommendation>();
        
        // Анализ длительности игр
        var avgRounds = results.Average(r => r.RoundsPlayed);
        if (avgRounds > 15)
            recommendations.Add(new BalanceRecommendation
            {
                Priority = Priority.High,
                Category = "GamePace",
                Issue = $"Средняя длительность игры ({avgRounds:F1} раундов) превышает целевую (≤12)",
                Suggestion = "Увеличить базовый доход или снизить порог победы"
            });
        
        // Анализ карт с плохой окупаемостью
        var cardStats = AnalyzeCardBalance(results);
        var weakCards = cardStats.Values
            .Where(c => c.AvgPayback > 10 && c.TotalActivations > 5)
            .ToList();
        
        if (weakCards.Any())
            recommendations.Add(new BalanceRecommendation
            {
                Priority = Priority.Medium,
                Category = "CardBalance",
                Issue = $"{weakCards.Count} карт имеют окупаемость >10 ходов",
                Suggestion = "Пересмотреть стоимость или эффект слабых карт"
            });
        
        // Анализ дисбаланса любимых цветов
        var favStats = AnalyzeFavoriteColors(results);
        var purpleData = favStats.GetValueOrDefault(CardColor.Purple);
        if (purpleData?.Frequency < 1)
            recommendations.Add(new BalanceRecommendation
            {
                Priority = Priority.Low,
                Category = "FavoriteColor",
                Issue = "Фиолетовый любимый цвет активируется слишком редко",
                Suggestion = "Это ожидаемо для 'легендарной' стратегии, но можно добавить визуальную компенсацию"
            });
        
        // Анализ конфронтации
        var interactionStats = AnalyzeInteractions(results);
        if (interactionStats.AvgTheftAmount > 3)
            recommendations.Add(new BalanceRecommendation
            {
                Priority = Priority.Medium,
                Category = "PvP",
                Issue = $"Средняя кража ({interactionStats.AvgTheftAmount:F1}) может быть слишком болезненной",
                Suggestion = "Рассмотреть ограничение максимального урона от кражи"
            });
        
        return recommendations;
    }

    /// <summary>
    /// Выводит отчёт в консоль.
    /// </summary>
    private void PrintReport(BalanceAnalysisReport report)
    {
        Console.WriteLine();
        Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  📊 ОТЧЁТ АНАЛИЗА БАЛАНСА — ПОЛНАЯ СИМУЛЯЦИЯ                  ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        
        Console.WriteLine($"🎮 Симуляций: {report.SimulationCount} | Время: {report.ElapsedTime.TotalSeconds:F1}с");
        Console.WriteLine($"📈 Среднее количество раундов: {report.AverageRounds:F2} (цель: ≤12)");
        Console.WriteLine();
        
        Console.WriteLine("🏆 Распределение побед:");
        foreach (var (player, rate) in report.WinDistribution.OrderByDescending(x => x.Value).Take(5))
            Console.WriteLine($"   {player,-12} {rate,5:0.0}%");
        Console.WriteLine();
        
        Console.WriteLine("💜 Статистика любимых цветов:");
        Console.WriteLine($"   {"Цвет",-10} {"Частота",-10} {"WinRate",-10} {"Сред. монеты"}");
        foreach (var (color, data) in report.FavoriteColorStats)
        {
            var icon = GetColorIcon(color);
            Console.WriteLine($"   {icon} {color,-8} {data.Frequency,9:0.0}% {data.AvgWinRate,9:0.0}% {data.AvgFinalCoins,12:F1}");
        }
        Console.WriteLine();
        
        Console.WriteLine("🃏 Баланс карт (топ по активациям):");
        var topCards = report.CardBalanceStats.Values
            .OrderByDescending(c => c.TotalActivations)
            .Take(8)
            .ToList();

        Console.WriteLine($"   {"Название",-32} {"Актив.",-8} {"Эффект.",-8} {"Окупаемость"}");
        foreach (var card in topCards)
        {
            var icon = GetColorIcon(card.Color);
            var paybackStr = card.AvgPayback == double.MaxValue ? "∞" : $"{card.AvgPayback:F1}";
            // Формируем строку с реальным именем + иконкой
            var displayName = $"{icon} {card.CardName}".PadRight(32);
            Console.WriteLine($"   {displayName} {card.TotalActivations,-8} {card.EffectiveActivations,-8} {paybackStr}");
        }
        Console.WriteLine();
        
        Console.WriteLine("⚔️ Взаимодействия:");
        Console.WriteLine($"   Всего событий: {report.InteractionStats.TotalInteractions}");
        foreach (var (type, count) in report.InteractionStats.ByType.OrderByDescending(x => x.Value))
            Console.WriteLine($"   {type,-20} {count}");
        Console.WriteLine($"   Средний размер кражи: {report.InteractionStats.AvgTheftAmount:F2} 💰");
        Console.WriteLine();
        
        if (report.Recommendations.Any())
        {
            Console.WriteLine("💡 Рекомендации:");
            foreach (var rec in report.Recommendations.OrderBy(r => r.Priority))
            {
                var priorityIcon = rec.Priority switch
                {
                    Priority.High => "🔴",
                    Priority.Medium => "🟡",
                    Priority.Low => "🟢",
                    _ => "⚪"
                };
                Console.WriteLine($"   {priorityIcon} [{rec.Category}] {rec.Issue}");
                Console.WriteLine($"      → {rec.Suggestion}");
            }
        }
        Console.WriteLine();
    }

    private static string GetColorIcon(CardColor color) => color switch
    {
        CardColor.Blue => "🔵",
        CardColor.Gold => "🟡",
        CardColor.Red => "🔴",
        CardColor.Purple => "🟣",
        _ => "⚪"
    };
}

/// <summary>
/// Вспомогательный класс игрока для симуляции.
/// </summary>
public class SimPlayer
{
    public string Name { get; set; } = string.Empty;
    public int Coins { get; set; }
    public List<Card> Inventory { get; set; } = new();
    public bool HasBoughtThisTurn { get; set; }
    public CardColor? FavoriteColor { get; set; }
    public CardColor? LastBoughtColor { get; set; }

    public bool SpendCoins(int amount)
    {
        if (Coins >= amount)
        {
            Coins -= amount;
            return true;
        }
        return false;
    }

    public void ResetTurnState() => HasBoughtThisTurn = false;

    public void UpdateFavoriteColor()
    {
        if (Inventory.Count == 0)
        {
            FavoriteColor = null;
            return;
        }

        var counts = Inventory
            .GroupBy(c => c.Color)
            .ToDictionary(g => g.Key, g => g.Count());

        int maxCount = counts.Values.Max();
        var dominant = counts.Where(x => x.Value == maxCount).Select(x => x.Key).ToList();

        // Специальное правило для Purple
        if (counts.ContainsKey(CardColor.Purple) && counts[CardColor.Purple] >= 1)
        {
            var others = new[] { CardColor.Blue, CardColor.Gold, CardColor.Red };
            if (others.All(c => counts.ContainsKey(c) && counts[c] > 0) &&
                others.Select(c => counts[c]).Distinct().Count() == 1)
            {
                FavoriteColor = CardColor.Purple;
                return;
            }
        }

        // Один доминирующий цвет
        if (dominant.Count == 1)
        {
            FavoriteColor = dominant[0];
            return;
        }

        // Ничья — последняя купленная карта
        FavoriteColor = LastBoughtColor ?? dominant[_rng.Next(dominant.Count)];
    }

    private static readonly Random _rng = new(42);
}

/// <summary>
/// Отчёт анализа баланса.
/// </summary>
public record BalanceAnalysisReport
{
    public int SimulationCount { get; init; }
    public TimeSpan ElapsedTime { get; init; }
    public double AverageRounds { get; init; }
    public Dictionary<string, double> WinDistribution { get; init; } = new();
    public Dictionary<CardColor, ColorAnalysisData> FavoriteColorStats { get; init; } = new();
    public Dictionary<string, CardBalanceData> CardBalanceStats { get; init; } = new();
    public InteractionAnalysisData InteractionStats { get; init; } = new();
    public List<BalanceRecommendation> Recommendations { get; init; } = new();
}

public record ColorAnalysisData
{
    public double Frequency { get; init; }
    public double AvgWinRate { get; init; }
    public double AvgFinalCoins { get; init; }
}

public record CardBalanceData
{
    public string CardName { get; init; } = string.Empty;
    public CardColor Color { get; init; }
    public int Cost { get; init; }
    public int BaseReward { get; init; }
    public int TotalActivations { get; set; }
    public int EffectiveActivations { get; set; }
    public double AvgPayback { get; set; }
}

public record InteractionAnalysisData
{
    public int TotalInteractions { get; init; }
    public Dictionary<GameBalanceAnalyzer.InteractionType, int> ByType { get; init; } = new();
    public Dictionary<string, int> ByColorMatch { get; init; } = new();
    public double AvgTheftAmount { get; init; }
}

public record BalanceRecommendation
{
    public Priority Priority { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Issue { get; init; } = string.Empty;
    public string Suggestion { get; init; } = string.Empty;
}

public enum Priority { Low, Medium, High }