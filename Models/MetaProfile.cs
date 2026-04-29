using System.Text.Json.Serialization;

namespace TinyCityCardGame_online.Models;

/// <summary>
/// Представляет мета-профиль игрока, сохраняемый между сессиями.
/// </summary>
public class MetaProfile
{
    /// <summary>
    /// Уникальное имя игрока (ключ).
    /// </summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>
    /// Накопленная репутация (валюта прогрессии).
    /// </summary>
    public int Reputation { get; set; } = 0;

    /// <summary>
    /// Количество сыгранных игр (для статистики).
    /// </summary>
    public int GamesPlayed { get; set; } = 0;

    /// <summary>
    /// Бонус к стартовым монетам (вычисляется на основе репутации).
    /// Ограничение: макс. +2 монеты.
    /// </summary>
    [JsonIgnore] // Не сохраняем в JSON, вычисляем динамически
    public int StartCoinBonus => Math.Min(2, Reputation / 100); 
    // Логика: каждые 100 репутации дают +1 монету (максимум 2)

    /// <summary>
    /// Возвращает JSON-представление профиля (для отладки).
    /// </summary>
    public override string ToString() => $"{PlayerName}: Rep={Reputation}, Coins+={StartCoinBonus}";
}
