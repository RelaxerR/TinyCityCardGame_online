using Microsoft.Extensions.Options;
using TinyCityCardGame_online.Models;
using TinyCityCardGame_online.Services;

namespace TinyCityCardGame_online.Analitics.Services;

/// <summary>
/// Сервис расчета вероятностей карт методом Монте-Карло.
/// Запускается автоматически при старте сервера и выводит статистику в консоль.
/// </summary>
public class CardProbabilityCalculator
{
    private readonly List<Card> _baseCards;
    private readonly GameSettings _settings;
    private readonly ILogger<CardProbabilityCalculator> _logger;
    private readonly Random _random;
    
    // Конфигурация симуляции
    private const int DefaultSimulations = 10000;
    private const int MaxRounds = 20;
    private const int DefaultPlayers = 4;

    /// <summary>
    /// Результаты симуляции для карты.
    /// </summary>
    public record CardSimulationResult
    {
        public int CardId { get; init; }
        public string CardName { get; init; } = string.Empty;
        public CardColor Color { get; init; }
        public int Cost { get; init; }
        public int Reward { get; init; }
        public int Weight { get; init; }
        public double AppearanceProbability { get; init; }
        public double ActivationProbability { get; init; }
        public double ExpectedPayback { get; init; }
        public double Roi10Rounds { get; init; }
        public string BalanceStatus { get; init; } = string.Empty;
    }

    /// <summary>
    /// Инициализирует новый экземпляр класса CardProbabilityCalculator.
    /// </summary>
    public CardProbabilityCalculator(
        CardLoader loader,
        IOptions<GameSettings> settings,
        ILogger<CardProbabilityCalculator> logger)
    {
        _baseCards = loader.LoadCardsFromExcel("Cards.xlsx");
        _settings = settings.Value;
        _logger = logger;
        _random = new Random(42); // Фиксированный seed для воспроизводимости
    }

    /// <summary>
    /// Запускает расчет вероятностей при старте сервера.
    /// </summary>
    public async Task CalculateAndPrintProbabilities()
    {
        await Task.Delay(2000); // Ждем загрузки всех сервисов
        
        _logger.LogInformation("🎲 Запуск расчета вероятностей методом Монте-Карло...");
        Console.WriteLine();
        Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  📊 CALCULATION OF CARD PROBABILITIES (MONTE CARLO)           ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // 1. Вывод настроек
        PrintColorProbabilities();
        
        // 2. Расчет для каждого раунда
        var allResults = new List<CardSimulationResult>();
        
        for (int round = 1; round <= MaxRounds; round++)
        {
            Console.WriteLine($"┌────────────────────────────────────────────────────────────────┐");
            Console.WriteLine($"│  📍 РАУНД {round,2}                                              │");
            Console.WriteLine($"└────────────────────────────────────────────────────────────────┘");
            
            var results = SimulateRound(round, DefaultSimulations);
            allResults.AddRange(results);
            
            PrintRoundResults(results, round);
            Console.WriteLine();
        }

        // 3. Итоговая статистика
        PrintSummaryStatistics(allResults);
        
        _logger.LogInformation("✅ Расчет вероятностей завершен");
    }

    /// <summary>
    /// Выводит вероятности выпадения цветов.
    /// </summary>
    private void PrintColorProbabilities()
    {
        var total = _settings.ColorChanceBlue + _settings.ColorChanceRed + 
                   _settings.ColorChancePurple + _settings.ColorChanceGold;

        Console.WriteLine("🎨 ВЕРОЯТНОСТИ АКТИВНЫХ ЦВЕТОВ:");
        Console.WriteLine($"   🔵 Blue:   {_settings.ColorChanceBlue,3} / {total} = {(_settings.ColorChanceBlue * 100.0 / total),5:0.00}%");
        Console.WriteLine($"   🔴 Red:    {_settings.ColorChanceRed,3} / {total} = {(_settings.ColorChanceRed * 100.0 / total),5:0.00}%");
        Console.WriteLine($"   🟣 Purple: {_settings.ColorChancePurple,3} / {total} = {(_settings.ColorChancePurple * 100.0 / total),5:0.00}%");
        Console.WriteLine($"   🟡 Gold:   {_settings.ColorChanceGold,3} / {total} = {(_settings.ColorChanceGold * 100.0 / total),5:0.00}%");
        Console.WriteLine();
    }

    /// <summary>
    /// Симулирует один раунд игры методом Монте-Карло.
    /// </summary>
    /// <param name="roundNumber">Номер раунда.</param>
    /// <param name="simulations">Количество симуляций.</param>
    /// <returns>Результаты симуляции для всех карт.</returns>
    public List<CardSimulationResult> SimulateRound(int roundNumber, int simulations = DefaultSimulations)
    {
        // Получаем доступные карты для текущего раунда (cost <= round)
        var availableCards = GetAvailableCardsForRound(roundNumber);
        
        // Считаем общий вес доступных карт для нормализации вероятностей
        var totalWeight = CalculateTotalWeight(availableCards);
        
        var cardStats = new Dictionary<int, CardStatistics>();
        
        foreach (var card in availableCards)
        {
            cardStats[card.Id] = new CardStatistics();
        }

        // Запуск симуляций
        for (int i = 0; i < simulations; i++)
        {
            SimulateSingleTurn(roundNumber, availableCards, totalWeight, cardStats);
        }

        // Расчет итоговых вероятностей (нормализованных)
        var results = new List<CardSimulationResult>();
        
        foreach (var card in availableCards)
        {
            var stats = cardStats[card.Id];
            // Нормализованная вероятность появления (сумма = 100% для всех карт на рынке)
            var appearanceProb = stats.AppearanceCount / (double)simulations;
            var activationProb = stats.ActivationCount / (double)simulations;
            
            var result = new CardSimulationResult
            {
                CardId = card.Id,
                CardName = card.Name,
                Color = card.Color,
                Cost = card.Cost,
                Reward = card.Reward,
                Weight = card.Weight,
                AppearanceProbability = appearanceProb,
                ActivationProbability = activationProb,
                ExpectedPayback = CalculatePayback(card, activationProb),
                Roi10Rounds = CalculateRoi(card, activationProb, 10),
                BalanceStatus = GetBalanceStatus(card, activationProb)
            };
            
            results.Add(result);
        }

        return results.OrderBy(r => r.Cost).ThenBy(r => r.CardName).ToList();
    }

    /// <summary>
    /// Симулирует один ход игры.
    /// </summary>
    private void SimulateSingleTurn(
        int roundNumber, 
        List<Card> availableCards, 
        int totalWeight,
        Dictionary<int, CardStatistics> cardStats)
    {
        // 1. Выбираем активный цвет
        var activeColor = SelectActiveColor();
        
        // 2. Выбираем карты на рынок (размер рынка = игроки + 1)
        var marketCards = SelectMarketCards(availableCards, totalWeight, DefaultPlayers + 1);
        
        // 3. Отмечаем появления и активации
        foreach (var card in marketCards)
        {
            if (cardStats.ContainsKey(card.Id))
            {
                cardStats[card.Id].AppearanceCount++;
                
                if (card.Color == activeColor)
                {
                    cardStats[card.Id].ActivationCount++;
                }
            }
        }
    }

    /// <summary>
    /// Выбирает активный цвет на основе настроек.
    /// </summary>
    public CardColor SelectActiveColor()
    {
        var total = _settings.ColorChanceBlue + _settings.ColorChanceRed + 
                   _settings.ColorChancePurple + _settings.ColorChanceGold;
        
        var roll = _random.Next(0, total);
        
        if (roll < _settings.ColorChanceBlue)
            return CardColor.Blue;
        
        roll -= _settings.ColorChanceBlue;
        if (roll < _settings.ColorChanceGold)
            return CardColor.Gold;
        
        roll -= _settings.ColorChanceGold;
        if (roll < _settings.ColorChanceRed)
            return CardColor.Red;
        
        return CardColor.Purple;
    }

    /// <summary>
    /// Получает доступные карты для текущего раунда (cost <= round).
    /// </summary>
    public List<Card> GetAvailableCardsForRound(int roundNumber)
    {
        return _baseCards.Where(c => c.Cost <= roundNumber).ToList();
    }

    /// <summary>
    /// Считает общий вес доступных карт.
    /// </summary>
    public int CalculateTotalWeight(List<Card> cards)
    {
        return cards.Sum(c => c.Weight);
    }

    /// <summary>
    /// Выбирает карты на рынок с учетом весов.
    /// </summary>
    public List<Card> SelectMarketCards(List<Card> availableCards, int totalWeight, int marketSize)
    {
        var market = new List<Card>();
        var pool = new List<Card>(availableCards);
        var currentWeight = totalWeight;
        
        for (int i = 0; i < marketSize && pool.Count > 0; i++)
        {
            var selectedCard = SelectWeightedCard(pool, currentWeight);
            if (selectedCard != null)
            {
                market.Add(selectedCard);
                pool.Remove(selectedCard);
                // Пересчитываем вес после удаления карты
                currentWeight = CalculateTotalWeight(pool);
            }
        }
        
        return market;
    }

    /// <summary>
    /// Выбирает одну карту с учетом весов.
    /// </summary>
    public Card? SelectWeightedCard(List<Card> pool, int totalWeight)
    {
        if (pool.Count == 0 || totalWeight <= 0)
            return null;
        
        var roll = _random.Next(0, totalWeight);
        var currentSum = 0;
        
        foreach (var card in pool)
        {
            currentSum += card.Weight;
            if (roll < currentSum)
                return card;
        }
        
        return pool[_random.Next(pool.Count)];
    }

    /// <summary>
    /// Рассчитывает период окупаемости карты.
    /// </summary>
    public double CalculatePayback(Card card, double activationProbability)
    {
        if (activationProbability <= 0 || card.Reward <= 0)
            return double.MaxValue;
        
        return card.Cost / (card.Reward * activationProbability);
    }

    /// <summary>
    /// Рассчитывает ROI за N ходов.
    /// </summary>
    public double CalculateRoi(Card card, double activationProbability, int turns)
    {
        var expectedIncome = card.Reward * activationProbability * turns;
        return expectedIncome - card.Cost;
    }

    /// <summary>
    /// Определяет статус баланса карты.
    /// </summary>
    public string GetBalanceStatus(Card card, double activationProbability)
    {
        var payback = CalculatePayback(card, activationProbability);
        
        if (payback <= 2)
            return "⚠️ Strong";
        if (payback <= 5)
            return "✅ OK";
        if (payback <= 8)
            return "⚡ Average";
        return "⚠️ Weak";
    }

    /// <summary>
    /// Выводит результаты симуляции раунда в консоль.
    /// Вероятности появления нормализованы (сумма = 100%).
    /// </summary>
    private void PrintRoundResults(List<CardSimulationResult> results, int round)
    {
        // Считаем сумму всех вероятностей для нормализации
        var totalRawProb = results.Sum(r => r.AppearanceProbability);
        var normalizationFactor = totalRawProb > 0 ? 1.0 / totalRawProb : 1.0;
        
        Console.WriteLine($"┌─────┬────────────────────────────┬──────┬───────┬───────┬──────────┬────────────┬───────────┬────────┐");
        Console.WriteLine($"│ №   │ Название карты             │ Цвет │ Цена  │ Вес   │ Появление│ Активация  │ Окупаемость│ Статус │");
        Console.WriteLine($"├─────┼────────────────────────────┼──────┼───────┼───────┼──────────┼────────────┼───────────┼────────┤");

        int num = 1;
        double totalNormalizedProb = 0;
        
        foreach (var result in results)
        {
            // Нормализуем вероятность появления
            var normalizedAppearanceProb = result.AppearanceProbability * normalizationFactor;
            totalNormalizedProb += normalizedAppearanceProb;
            
            var colorIcon = GetColorIcon(result.Color);
            Console.WriteLine(
                $"│ {num,3} │ {result.CardName,-26} │ {colorIcon,-4} │ {result.Cost,5} │ {result.Weight,5} │ " +
                $"{normalizedAppearanceProb * 100,7:0.00}% │ " +
                $"{result.ActivationProbability * 100,9:0.00}% │ " +
                $"{(result.ExpectedPayback == double.MaxValue ? "∞" : result.ExpectedPayback.ToString("0.0")),9} │ " +
                $"{result.BalanceStatus,-6} │");
            
            num++;
        }

        Console.WriteLine($"├─────┴────────────────────────────┴──────┴───────┴───────┴──────────┴────────────┴───────────┴────────┤");
        Console.WriteLine($"│ Σ нормализованных вероятностей: {totalNormalizedProb * 100,5:0.00}% {(Math.Abs(totalNormalizedProb - 1.0) < 0.001 ? "✓" : "⚠")}                                                      │");
        Console.WriteLine($"│ * Вероятности нормализованы: реальная частота = (вес карты) / (Σ всех весов)                        │");
        Console.WriteLine($"└─────────────────────────────────────────────────────────────────────────────────────────────────────────┘");
    }

    /// <summary>
    /// Выводит итоговую статистику.
    /// </summary>
    private void PrintSummaryStatistics(List<CardSimulationResult> allResults)
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  📈 ИТОГОВАЯ СТАТИСТИКА ПО ГРУППАМ КАРТ                        ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        var grouped = allResults.GroupBy(r => r.Color);
        
        foreach (var group in grouped)
        {
            var colorIcon = GetColorIcon(group.Key);
            var avgPayback = group.Average(r => r.ExpectedPayback == double.MaxValue ? 20 : r.ExpectedPayback);
            var avgActivation = group.Average(r => r.ActivationProbability) * 100;
            var avgCost = group.Average(r => r.Cost);
            
            Console.WriteLine($"{colorIcon} {group.Key,-10} | Карт: {group.Count(),2} | " +
                            $"Сред. цена: {avgCost,5:0.0} | " +
                            $"Сред. окупаемость: {avgPayback,5:0.0} ходов | " +
                            $"Сред. активация: {avgActivation,5:0.0}%");
        }
        
        Console.WriteLine();
        Console.WriteLine("💡 Рекомендации:");
        Console.WriteLine("   • Карты с окупаемостью < 2 ходов могут быть слишком сильными");
        Console.WriteLine("   • Карты с окупаемостью > 8 ходов могут быть слишком слабыми");
        Console.WriteLine("   • Purple карты имеют низкую вероятность активации (5%)");
        Console.WriteLine("   • Вероятности появления нормализованы (сумма = 100% за раунд)");
        Console.WriteLine();
    }

    /// <summary>
    /// Возвращает иконку для цвета карты.
    /// </summary>
    private static string GetColorIcon(CardColor color) => color switch
    {
        CardColor.Blue => "🔵",
        CardColor.Red => "🔴",
        CardColor.Purple => "🟣",
        CardColor.Gold => "🟡",
        _ => "⚪"
    };

    /// <summary>
    /// Статистика для карты в ходе симуляции.
    /// </summary>
    private class CardStatistics
    {
        public int AppearanceCount { get; set; }
        public int ActivationCount { get; set; }
    }
}