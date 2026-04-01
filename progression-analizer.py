#!/usr/bin/env python3
"""
Color Engine - Monte Carlo Simulation
Симуляция прогрессии карточной игры с анализом баланса
"""

import random
import numpy as np
from dataclasses import dataclass, field
from typing import List, Dict, Optional, Tuple
from enum import Enum
import json

# ============================================================================
# 📊 КОНФИГУРАЦИЯ СИМУЛЯЦИИ
# ============================================================================

@dataclass
class GameConfig:
    """Конфигурация параметров игры"""
    win_target: int = 100
    daily_income: int = 1
    start_coins_min: int = 5
    start_coins_max: int = 10
    min_players: int = 2
    max_players: int = 4
    
    # Вероятности цветов (из appsettings.json)
    color_chance_blue: int = 50
    color_chance_red: int = 25
    color_chance_purple: int = 5
    color_chance_gold: int = 30
    
    @property
    def total_color_weight(self) -> int:
        return (self.color_chance_blue + self.color_chance_red + 
                self.color_chance_purple + self.color_chance_gold)

# ============================================================================
# 🃏 МОДЕЛИ ДАННЫХ
# ============================================================================

class CardColor(Enum):
    Blue = "Blue"
    Red = "Red"
    Purple = "Purple"
    Gold = "Gold"

@dataclass
class Card:
    """Представление карты в игре"""
    id: int
    name: str
    color: CardColor
    effect: str
    cost: int
    reward: int
    weight: int
    description: str = ""
    
    def get_effect_value(self) -> int:
        """Извлекает числовое значение из эффекта"""
        parts = self.effect.split()
        if len(parts) >= 2:
            try:
                return int(parts[-1])
            except ValueError:
                return self.reward
        return self.reward

@dataclass
class Player:
    """Представление игрока"""
    name: str
    coins: int
    inventory: List[Card] = field(default_factory=list)
    has_bought_this_turn: bool = False
    
    def can_afford(self, card: Card) -> bool:
        return self.coins >= card.cost
    
    def buy_card(self, card: Card) -> bool:
        if self.can_afford(card):
            self.coins -= card.cost
            self.inventory.append(card)
            self.has_bought_this_turn = True
            return True
        return False
    
    def activate_cards(self, active_color: CardColor, all_players: List['Player']) -> int:
        """Активирует карты подходящего цвета, возвращает заработанное"""
        earned = 0
        for card in self.inventory:
            if card.color == active_color and card.effect:
                earned += self._execute_effect(card, all_players)
        return earned
    
    def _execute_effect(self, card: Card, all_players: List['Player']) -> int:
        """Выполняет эффект карты"""
        parts = card.effect.upper().split()
        if not parts:
            return 0
        
        cmd = parts[0]
        value = card.get_effect_value()
        
        if cmd == "GET":
            return value
        elif cmd == "GETALL":
            return value  # Каждый получает
        elif cmd == "STEAL_MONEY":
            # Упрощенная модель: крадем у случайного оппонента
            opponents = [p for p in all_players if p.name != self.name and p.coins > 0]
            if opponents:
                victim = random.choice(opponents)
                stolen = min(value, victim.coins)
                victim.coins -= stolen
                return stolen
        elif cmd == "GETBY":
            # Доход за карты определенного цвета
            if len(parts) >= 2:
                try:
                    target_color = CardColor[parts[1]]
                    count = sum(1 for c in self.inventory if c.color == target_color)
                    return count * value
                except (KeyError, ValueError):
                    pass
        elif cmd == "STEAL_CARD":
            # Упрощенно: считаем как эквивалент монет
            return value // 2
        
        return 0
    
    def reset_turn(self):
        self.has_bought_this_turn = False
        self.coins += 1  # Daily income

@dataclass
class GameState:
    """Состояние игровой сессии"""
    room_code: str
    players: List[Player]
    market: List[Card]
    active_color: CardColor
    round_number: int = 1
    current_player_index: int = 0
    turn_order: List[str] = field(default_factory=list)
    
    def get_current_player(self) -> Optional[Player]:
        if not self.turn_order or self.current_player_index >= len(self.turn_order):
            return None
        player_name = self.turn_order[self.current_player_index]
        return next((p for p in self.players if p.name == player_name), None)
    
    def next_player(self):
        self.current_player_index = (self.current_player_index + 1) % len(self.turn_order)
        if self.current_player_index == 0:
            self.round_number += 1
            # Сброс состояния хода для всех
            for player in self.players:
                player.reset_turn()
    
    def check_winner(self, win_target: int) -> Optional[Player]:
        for player in self.players:
            if player.coins >= win_target:
                return player
        return None

# ============================================================================
# 🎲 СИМУЛЯТОР ИГРЫ
# ============================================================================

class ColorEngineSimulator:
    """Основной симулятор игры"""
    
    def __init__(self, config: GameConfig, cards: List[Card]):
        self.config = config
        self.base_cards = cards
        self.rng = random.Random(42)  # Фиксированный seed для воспроизводимости
    
    def select_active_color(self) -> CardColor:
        """Выбирает активный цвет раунда"""
        roll = self.rng.randint(0, self.config.total_color_weight - 1)
        
        if roll < self.config.color_chance_blue:
            return CardColor.Blue
        roll -= self.config.color_chance_blue
        
        if roll < self.config.color_chance_gold:
            return CardColor.Gold
        roll -= self.config.color_chance_gold
        
        if roll < self.config.color_chance_red:
            return CardColor.Red
        
        return CardColor.Purple
    
    def get_available_cards(self, round_number: int) -> List[Card]:
        """Получает карты, доступные в текущем раунде"""
        return [c for c in self.base_cards if c.cost <= round_number]
    
    def select_market_cards(self, available_cards: List[Card], market_size: int) -> List[Card]:
        """Выбирает карты на рынок с учетом весов"""
        if not available_cards:
            return []
        
        market = []
        pool = available_cards.copy()
        
        for _ in range(market_size):
            if not pool:
                break
            
            card = self._select_weighted_card(pool)
            if card:
                market.append(card)
                pool.remove(card)
        
        return market
    
    def _select_weighted_card(self, pool: List[Card]) -> Optional[Card]:
        """Выбирает одну карту с учетом весов"""
        if not pool:
            return None
        
        total_weight = sum(c.weight for c in pool)
        if total_weight <= 0:
            return self.rng.choice(pool)
        
        roll = self.rng.randint(0, total_weight - 1)
        current_sum = 0
        
        for card in pool:
            current_sum += card.weight
            if roll < current_sum:
                return card
        
        return self.rng.choice(pool)
    
    def simulate_game(self, player_count: int = 4, max_rounds: int = 100) -> Dict:
        """Симулирует одну полную игру"""
        # Инициализация игроков
        players = []
        for i in range(player_count):
            start_coins = self.rng.randint(
                self.config.start_coins_min,
                self.config.start_coins_max
            )
            players.append(Player(name=f"Player_{i+1}", coins=start_coins))
        
        # Инициализация состояния
        state = GameState(
            room_code="SIM_001",
            players=players,
            market=[],
            active_color=self.select_active_color(),
            turn_order=[p.name for p in players]
        )
        
        # Заполняем начальный рынок
        available = self.get_available_cards(state.round_number)
        market_size = player_count + 1
        state.market = self.select_market_cards(available, market_size)
        
        # Игровой цикл
        stats = {
            'rounds_played': 0,
            'winner': None,
            'winner_coins': 0,
            'player_final_coins': [],
            'cards_bought': [],
            'color_history': []
        }
        
        while state.round_number <= max_rounds:
            stats['rounds_played'] = state.round_number
            stats['color_history'].append(state.active_color.name)
            
            # Ход каждого игрока
            for _ in range(len(players)):
                current_player = state.get_current_player()
                if not current_player:
                    break
                
                # 1. Активация карт
                current_player.activate_cards(state.active_color, players)
                
                # 2. Покупка карты (если есть деньги и не покупал в этот ход)
                if not current_player.has_bought_this_turn and state.market:
                    affordable = [c for c in state.market if current_player.can_afford(c)]
                    if affordable:
                        # Простая стратегия: покупаем самую дорогую из доступных
                        card_to_buy = max(affordable, key=lambda c: c.cost)
                        if current_player.buy_card(card_to_buy):
                            state.market.remove(card_to_buy)
                            stats['cards_bought'].append({
                                'player': current_player.name,
                                'card': card_to_buy.name,
                                'round': state.round_number,
                                'cost': card_to_buy.cost
                            })
                        
                        # Пополняем рынок
                        available = self.get_available_cards(state.round_number)
                        new_card = self._select_weighted_card(available)
                        if new_card:
                            state.market.append(new_card)
                
                # 3. Проверка победы
                winner = state.check_winner(self.config.win_target)
                if winner:
                    stats['winner'] = winner.name
                    stats['winner_coins'] = winner.coins
                    stats['player_final_coins'] = [p.coins for p in players]
                    return stats
                
                # Переход к следующему игроку
                state.next_player()
            
            # Новый раунд - новый активный цвет
            state.active_color = self.select_active_color()
            
            # Проверка на бесконечную игру
            if state.round_number >= max_rounds:
                stats['player_final_coins'] = [p.coins for p in players]
                stats['winner'] = max(players, key=lambda p: p.coins).name
                stats['winner_coins'] = max(p.coins for p in players)
                break
        
        return stats

# ============================================================================
# 📈 АНАЛИЗ ПРОГРЕССИИ
# ============================================================================

class ProgressionAnalyzer:
    """Анализатор прогрессии игры"""
    
    def __init__(self, simulator: ColorEngineSimulator):
        self.simulator = simulator
        self.results = []
    
    def run_simulation(self, num_games: int = 1000, player_count: int = 4):
        """Запускает серию симуляций"""
        print(f"🎲 Запуск {num_games} симуляций игры...")
        
        for i in range(num_games):
            result = self.simulator.simulate_game(player_count=player_count)
            self.results.append(result)
            
            if (i + 1) % 100 == 0:
                print(f"  Прогресс: {i + 1}/{num_games} игр")
        
        print(f"✅ Симуляция завершена\n")
    
    def analyze_progression(self):
        """Анализирует прогрессию игры"""
        if not self.results:
            print("❌ Нет данных для анализа")
            return
        
        # 1. Статистика по раундам
        rounds = [r['rounds_played'] for r in self.results]
        print("╔══════════════════════════════════════════════════════════════╗")
        print("║  📊 СТАТИСТИКА ПРОГРЕССИИ                                    ║")
        print("╚══════════════════════════════════════════════════════════════╝")
        print()
        print(f"📍 Среднее количество раундов до победы: {np.mean(rounds):.2f}")
        print(f"📍 Минимум раундов: {min(rounds)}")
        print(f"📍 Максимум раундов: {max(rounds)}")
        print(f"📍 Медиана: {np.median(rounds):.2f}")
        print(f"📍 Стандартное отклонение: {np.std(rounds):.2f}")
        print()
        
        # 2. Распределение по раундам
        print("📈 Распределение игр по количеству раундов:")
        round_bins = [0] * 11  # 0-10, 11-20, ..., 91-100
        for r in rounds:
            bin_idx = min(r // 10, 10)
            round_bins[bin_idx] += 1
        
        for i, count in enumerate(round_bins):
            if count > 0:
                start = i * 10 + 1
                end = (i + 1) * 10 if i < 10 else "100+"
                bar = "█" * int(count / max(round_bins) * 40)
                print(f"  {start:3}-{end:>3} раундов: {bar} {count} игр ({count/len(rounds)*100:.1f}%)")
        print()
        
        # 3. Статистика по игрокам
        all_final_coins = []
        for r in self.results:
            all_final_coins.extend(r['player_final_coins'])
        
        print("💰 Статистика монет у игроков:")
        print(f"  Среднее финальное количество: {np.mean(all_final_coins):.2f}")
        print(f"  Медиана: {np.median(all_final_coins):.2f}")
        print()
        
        # 4. Анализ покупок карт
        if self.results[0].get('cards_bought'):
            all_cards = []
            for r in self.results:
                all_cards.extend(r['cards_bought'])
            
            print("🃏 Статистика покупок карт:")
            print(f"  Всего покупок: {len(all_cards)}")
            print(f"  Среднее покупок за игру: {len(all_cards) / len(self.results):.2f}")
            
            # По раундам
            by_round = {}
            for card in all_cards:
                round_num = card['round']
                by_round[round_num] = by_round.get(round_num, 0) + 1
            
            print("\n  Покупки по раундам:")
            for round_num in sorted(by_round.keys())[:10]:  # Первые 10 раундов
                count = by_round[round_num]
                bar = "█" * int(count / max(by_round.values()) * 30)
                print(f"    Раунд {round_num:2}: {bar} {count}")
        print()
        
        # 5. Анализ активных цветов
        if self.results[0].get('color_history'):
            all_colors = []
            for r in self.results:
                all_colors.extend(r['color_history'])
            
            print("🎨 Активные цвета (фактические vs ожидаемые):")
            color_counts = {}
            for color in all_colors:
                color_counts[color] = color_counts.get(color, 0) + 1
            
            total = len(all_colors)
            expected = {
                'Blue': self.simulator.config.color_chance_blue,
                'Gold': self.simulator.config.color_chance_gold,
                'Red': self.simulator.config.color_chance_red,
                'Purple': self.simulator.config.color_chance_purple
            }
            total_expected = sum(expected.values())
            
            for color, count in sorted(color_counts.items(), key=lambda x: -x[1]):
                actual_pct = count / total * 100
                exp_pct = expected.get(color, 0) / total_expected * 100
                diff = actual_pct - exp_pct
                sign = "+" if diff >= 0 else ""
                print(f"  {color:8}: {actual_pct:5.1f}% (ожидалось {exp_pct:5.1f}%, {sign}{diff:.1f}%)")
        print()
    
    def get_balance_recommendations(self) -> List[str]:
        """Генерирует рекомендации по балансу"""
        recommendations = []
        
        if not self.results:
            return recommendations
        
        rounds = [r['rounds_played'] for r in self.results]
        avg_rounds = np.mean(rounds)
        
        # Анализ скорости игры
        if avg_rounds < 20:
            recommendations.append("⚠️ Игра слишком быстрая (< 20 раундов в среднем)")
            recommendations.append("   → Увеличьте WinTarget или уменьшите доход карт")
        elif avg_rounds > 60:
            recommendations.append("⚠️ Игра слишком медленная (> 60 раундов в среднем)")
            recommendations.append("   → Уменьшите WinTarget или увеличьте доход карт")
        else:
            recommendations.append("✅ Длительность игры в целевом диапазоне (20-60 раундов)")
        
        # Анализ вариативности
        std_rounds = np.std(rounds)
        if std_rounds > 20:
            recommendations.append("⚠️ Высокая вариативность длительности игр")
            recommendations.append("   → Рассмотрите механику negative feedback для отстающих")
        else:
            recommendations.append("✅ Стабильная длительность игр")
        
        # Анализ прогрессии
        early_rounds = [r for r in rounds if r < 15]
        late_rounds = [r for r in rounds if r > 50]
        
        if len(early_rounds) / len(rounds) > 0.3:
            recommendations.append("⚠️ Слишком много быстрых игр (< 15 раундов)")
            recommendations.append("   → Увеличьте стоимость ранних карт")
        
        if len(late_rounds) / len(rounds) > 0.2:
            recommendations.append("⚠️ Слишком много затяжных игр (> 50 раундов)")
            recommendations.append("   → Добавьте карты с большим эффектом для поздней игры")
        
        return recommendations

# ============================================================================
# 🃏 ЗАГРУЗКА КАРТ (упрощенная модель)
# ============================================================================

def create_sample_cards() -> List[Card]:
    """Создает тестовый набор карт (заглушка для Cards.xlsx)"""
    cards_data = [
        # Blue карты (производство)
        (1, "GETALL 1", CardColor.Blue, 1, 1, 10, "Все получают 1"),
        (2, "GETALL 2", CardColor.Blue, 1, 2, 10, "Все получают 2"),
        (3, "GETALL 3", CardColor.Blue, 2, 3, 10, "Все получают 3"),
        (4, "GETALL 4", CardColor.Blue, 2, 4, 10, "Все получают 4"),
        (5, "GETALL 5", CardColor.Blue, 3, 5, 10, "Все получают 5"),
        (6, "GETALL 6", CardColor.Blue, 3, 6, 10, "Все получают 6"),
        (7, "GETALL 7", CardColor.Blue, 4, 7, 10, "Все получают 7"),
        (8, "GETALL 8", CardColor.Blue, 4, 8, 10, "Все получают 8"),
        (9, "GETALL 9", CardColor.Blue, 5, 9, 10, "Все получают 9"),
        (10, "GETALL 10", CardColor.Blue, 5, 10, 10, "Все получают 10"),
        
        # Red карты (кража)
        (11, "STEAL_MONEY ALL 1", CardColor.Red, 2, 4, 30, "Украсть 1 у всех"),
        (12, "STEAL_MONEY ALL 2", CardColor.Red, 3, 8, 30, "Украсть 2 у всех"),
        (13, "STEAL_MONEY ALL 3", CardColor.Red, 6, 12, 30, "Украсть 3 у всех"),
        (14, "STEAL_MONEY ALL 4", CardColor.Red, 8, 16, 30, "Украсть 4 у всех"),
        (15, "STEAL_MONEY ALL 5", CardColor.Red, 10, 20, 30, "Украсть 5 у всех"),
        (16, "STEAL_MONEY ALL 6", CardColor.Red, 12, 24, 30, "Украсть 5 у всех"),
        (17, "STEAL_MONEY ALL 7", CardColor.Red, 14, 28, 30, "Украсть 5 у всех"),
        (18, "STEAL_MONEY ALL 8", CardColor.Red, 16, 32, 30, "Украсть 5 у всех"),
        (19, "STEAL_MONEY ALL 9", CardColor.Red, 18, 36, 30, "Украсть 5 у всех"),
        (20, "STEAL_MONEY ALL 10", CardColor.Red, 20, 40, 30, "Украсть 5 у всех"),
        
        # Gold карты (личный доход)
        (21, "GET 6", CardColor.Gold, 1, 6, 50, "Получить 6"),
        (22, "GET 18", CardColor.Gold, 5, 18, 50, "Получить 18"),
        (23, "GET 30", CardColor.Gold, 10, 30, 50, "Получить 30"),
        
        # Purple карты (специальные)
        (24, "STEAL_CARD RANDOM", CardColor.Purple, 8, 200, 15, "Украсть карту"),
    ]
    
    cards = []
    for data in cards_data:
        cards.append(Card(
            id=data[0],
            name=f"Card_{data[0]}",
            color=data[2],
            effect=data[1],
            cost=data[3],
            reward=data[4],
            weight=data[5],
            description=data[6]
        ))
    
    return cards

# ============================================================================
# 🚀 ЗАПУСК СИМУЛЯЦИИ
# ============================================================================

def main():
    """Точка входа"""
    print("╔══════════════════════════════════════════════════════════════╗")
    print("║         🎮 COLOR ENGINE - PROGRESSION SIMULATOR 🎮          ║")
    print("║              Метод Монте-Карло для анализа баланса          ║")
    print("╚══════════════════════════════════════════════════════════════╝")
    print()
    
    # 1. Конфигурация
    config = GameConfig(
        win_target=100,
        daily_income=1,
        start_coins_min=5,
        start_coins_max=10,
        color_chance_blue=50,
        color_chance_red=25,
        color_chance_purple=5,
        color_chance_gold=30
    )
    
    print("⚙️ Параметры конфигурации:")
    print(f"   Цель победы: {config.win_target} монет")
    print(f"   Ежедневный доход: {config.daily_income}")
    print(f"   Стартовые монеты: {config.start_coins_min}-{config.start_coins_max}")
    print(f"   Вероятности цветов: Blue={config.color_chance_blue}, " +
          f"Red={config.color_chance_red}, Purple={config.color_chance_purple}, " +
          f"Gold={config.color_chance_gold}")
    print()
    
    # 2. Загрузка карт
    cards = create_sample_cards()
    print(f"🃏 Загружено {len(cards)} карт")
    print(f"   Blue: {sum(1 for c in cards if c.color == CardColor.Blue)}")
    print(f"   Red: {sum(1 for c in cards if c.color == CardColor.Red)}")
    print(f"   Gold: {sum(1 for c in cards if c.color == CardColor.Gold)}")
    print(f"   Purple: {sum(1 for c in cards if c.color == CardColor.Purple)}")
    print()
    
    # 3. Инициализация симулятора
    simulator = ColorEngineSimulator(config, cards)
    analyzer = ProgressionAnalyzer(simulator)
    
    # 4. Запуск симуляции
    num_simulations = 1000
    player_count = 4
    analyzer.run_simulation(num_games=num_simulations, player_count=player_count)
    
    # 5. Анализ прогрессии
    analyzer.analyze_progression()
    
    # 6. Рекомендации
    print("╔══════════════════════════════════════════════════════════════╗")
    print("║  💡 РЕКОМЕНДАЦИИ ПО БАЛАНСУ                                  ║")
    print("╚══════════════════════════════════════════════════════════════╝")
    print()
    recommendations = analyzer.get_balance_recommendations()
    for rec in recommendations:
        print(rec)
    print()
    
    print("✅ Симуляция завершена успешно!")

if __name__ == "__main__":
    main()