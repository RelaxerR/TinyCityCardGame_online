#!/usr/bin/env python3
"""
Color Engine - Monte Carlo Simulation
Симуляция прогрессии карточной игры с анализом баланса
Версия с расширенным логированием
"""

import random
import numpy as np
import logging
import sys
from dataclasses import dataclass, field
from typing import List, Dict, Optional, Tuple
from enum import Enum
import json
from datetime import datetime

# ============================================================================
# 🔧 НАСТРОЙКА ЛОГИРОВАНИЯ
# ============================================================================

def setup_logging(log_level: str = "INFO", log_file: Optional[str] = None,
                  console_output: bool = True) -> logging.Logger:
    """
    Настраивает систему логирования для симулятора.
    
    Args:
        log_level: Уровень логирования (DEBUG, INFO, WARNING, ERROR, CRITICAL)
        log_file: Путь к файлу для сохранения логов (опционально)
        console_output: Выводить ли логи в консоль
    
    Returns:
        Настроенный logger
    """
    # Создаем formatter с временными метками и уровнем
    formatter = logging.Formatter(
        '%(asctime)s | %(levelname)-8s | %(name)s | %(message)s',
        datefmt='%Y-%m-%d %H:%M:%S'
    )

    # Создаем logger
    logger = logging.getLogger("ColorEngine")
    logger.setLevel(getattr(logging, log_level.upper(), logging.INFO))

    # Очищаем существующие handlers (для перезапуска)
    logger.handlers.clear()

    # Console handler
    if console_output:
        ch = logging.StreamHandler(sys.stdout)
        ch.setLevel(getattr(logging, log_level.upper(), logging.INFO))
        ch.setFormatter(formatter)
        logger.addHandler(ch)

    # File handler (если указан)
    if log_file:
        fh = logging.FileHandler(log_file, encoding='utf-8', mode='a')
        fh.setLevel(logging.DEBUG)  # В файл пишем всё
        fh.setFormatter(formatter)
        logger.addHandler(fh)

    logger.info(f"🔧 Логирование инициализировано | Уровень: {log_level} | Файл: {log_file or 'нет'}")
    return logger


# Глобальный logger (будет инициализирован в main)
logger: logging.Logger = None


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

    # Настройки логирования
    log_detailed_turns: bool = False  # Логировать каждый ход детально
    log_card_effects: bool = True     # Логировать применение эффектов карт
    log_market_changes: bool = True   # Логировать изменения рынка

    @property
    def total_color_weight(self) -> int:
        return (self.color_chance_blue + self.color_chance_red +
                self.color_chance_purple + self.color_chance_gold)

    def log_config(self):
        """Выводит конфигурацию в лог"""
        if logger:
            logger.info("📋 Конфигурация игры:")
            logger.info(f"   • WinTarget: {self.win_target} монет")
            logger.info(f"   • DailyIncome: {self.daily_income}")
            logger.info(f"   • StartCoins: [{self.start_coins_min}, {self.start_coins_max}]")
            logger.info(f"   • Players: [{self.min_players}, {self.max_players}]")
            logger.info(f"   • ColorWeights: Blue={self.color_chance_blue}, "
                        f"Red={self.color_chance_red}, Purple={self.color_chance_purple}, "
                        f"Gold={self.color_chance_gold}")
            logger.info(f"   • TotalWeight: {self.total_color_weight}")
            logger.debug(f"   • DetailedTurns: {self.log_detailed_turns}")
            logger.debug(f"   • LogCardEffects: {self.log_card_effects}")


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

    def __post_init__(self):
        """Логирование создания карты (только в DEBUG)"""
        if logger and logger.isEnabledFor(logging.DEBUG):
            logger.debug(f"🃏 Карта создана: {self.name} | Цвет: {self.color.value} | "
                         f"Cost: {self.cost} | Reward: {self.reward} | Weight: {self.weight}")

    def get_effect_value(self) -> int:
        """Извлекает числовое значение из эффекта"""
        parts = self.effect.split()
        if len(parts) >= 2:
            try:
                return int(parts[-1])
            except ValueError:
                return self.reward
        return self.reward

    def __str__(self):
        return f"{self.name}({self.color.value}, cost:{self.cost}, eff:'{self.effect}')"


@dataclass
class Player:
    """Представление игрока"""
    name: str
    coins: int
    inventory: List[Card] = field(default_factory=list)
    has_bought_this_turn: bool = False

    def log_state(self, context: str = ""):
        """Логирует текущее состояние игрока"""
        if logger and logger.isEnabledFor(logging.DEBUG):
            inv_summary = ", ".join([c.name for c in self.inventory[:3]])
            if len(self.inventory) > 3:
                inv_summary += f" ... (+{len(self.inventory)-3})"
            logger.debug(f"👤 {self.name} | Coins: {self.coins} | Inv: [{inv_summary}] | "
                         f"CanBuy: {not self.has_bought_this_turn} | {context}")

    def can_afford(self, card: Card) -> bool:
        result = self.coins >= card.cost
        if logger and logger.isEnabledFor(logging.DEBUG) and self.has_bought_this_turn:
            logger.debug(f"💰 {self.name} проверяет покупку {card.name}: "
                         f"{self.coins} >= {card.cost} = {result}")
        return result

    def buy_card(self, card: Card) -> bool:
        if self.can_afford(card):
            old_coins = self.coins
            self.coins -= card.cost
            self.inventory.append(card)
            self.has_bought_this_turn = True

            if logger:
                logger.info(f"🛒 {self.name} купил {card.name} за {card.cost} монет "
                            f"(осталось: {self.coins})")
                if logger.isEnabledFor(logging.DEBUG):
                    logger.debug(f"   📦 Инвентарь обновлён: {len(self.inventory)} карт")
            return True

        if logger and logger.isEnabledFor(logging.WARNING):
            logger.warning(f"⚠️ {self.name} не может купить {card.name}: "
                           f"{self.coins} < {card.cost}")
        return False

    def activate_cards(self, active_color: CardColor, all_players: List['Player']) -> int:
        """Активирует карты подходящего цвета, возвращает заработанное"""
        earned = 0
        matching_cards = [c for c in self.inventory if c.color == active_color and c.effect]

        if matching_cards and logger:
            logger.info(f"✨ {self.name} активирует {len(matching_cards)} карт цвета {active_color.value}")

        for card in matching_cards:
            effect_earned = self._execute_effect(card, all_players)
            earned += effect_earned

            if logger and effect_earned > 0:
                logger.debug(f"   🎯 {card.name} → +{effect_earned} монет")

        if earned > 0 and logger:
            logger.info(f"💵 {self.name} заработал {earned} монет от активации")

        return earned

    def _execute_effect(self, card: Card, all_players: List['Player']) -> int:
        """Выполняет эффект карты с логированием"""
        parts = card.effect.upper().split()
        if not parts:
            return 0

        cmd = parts[0]
        value = card.get_effect_value()
        earned = 0

        if logger and logger.isEnabledFor(logging.DEBUG) and card.effect:
            logger.debug(f"🔮 Эффект карты {card.name}: '{card.effect}' → cmd={cmd}, value={value}")

        if cmd == "GET":
            earned = value
            if logger and logger.isEnabledFor(logging.DEBUG):
                logger.debug(f"   ➕ GET: +{earned} монет")

        elif cmd == "GETALL":
            earned = value
            if logger and logger.isEnabledFor(logging.DEBUG):
                logger.debug(f"   ➕ GETALL: +{earned} монет (каждому)")

        elif cmd == "STEAL_MONEY":
            opponents = [p for p in all_players if p.name != self.name and p.coins > 0]
            if opponents:
                victim = random.choice(opponents)
                stolen = min(value, victim.coins)
                victim.coins -= stolen
                earned = stolen
                if logger:
                    logger.info(f"   🗡️  {card.name}: украдено {stolen} у {victim.name}")
            elif logger and logger.isEnabledFor(logging.DEBUG):
                logger.debug(f"   🗡️  STEAL_MONEY: нет доступных жертв")

        elif cmd == "GETBY":
            if len(parts) >= 2:
                try:
                    target_color = CardColor[parts[1]]
                    count = sum(1 for c in self.inventory if c.color == target_color)
                    earned = count * value
                    if logger and logger.isEnabledFor(logging.DEBUG):
                        logger.debug(f"   ➕ GETBY {target_color.value}: {count} карт × {value} = {earned}")
                except (KeyError, ValueError) as e:
                    if logger and logger.isEnabledFor(logging.WARNING):
                        logger.warning(f"⚠️ Ошибка парсинга GETBY в {card.name}: {e}")

        elif cmd == "STEAL_CARD":
            earned = value // 2
            if logger and logger.isEnabledFor(logging.DEBUG):
                logger.debug(f"   🗡️  STEAL_CARD: эквивалент +{earned} монет (упрощённо)")

        if earned != 0 and logger and logger.isEnabledFor(logging.DEBUG):
            logger.debug(f"✅ Эффект выполнен: итоговый доход +{earned}")

        return earned

    def reset_turn(self):
        old_coins = self.coins
        self.has_bought_this_turn = False
        self.coins += 1  # Daily income

        if logger and logger.isEnabledFor(logging.DEBUG):
            logger.debug(f"🔄 {self.name}: новый ход | +1 монета ({old_coins}→{self.coins})")


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

    def log_state_summary(self):
        """Логирует краткое состояние игры"""
        if logger and logger.isEnabledFor(logging.INFO):
            market_summary = ", ".join([f"{c.name}({c.cost})" for c in self.market[:3]])
            if len(self.market) > 3:
                market_summary += "..."
            logger.info(f"🎮 Раунд {self.round_number} | Цвет: {self.active_color.value} | "
                        f"Рынок: [{market_summary}]")

    def get_current_player(self) -> Optional[Player]:
        if not self.turn_order or self.current_player_index >= len(self.turn_order):
            return None
        player_name = self.turn_order[self.current_player_index]
        return next((p for p in self.players if p.name == player_name), None)

    def next_player(self):
        prev_index = self.current_player_index
        self.current_player_index = (self.current_player_index + 1) % len(self.turn_order)

        if self.current_player_index == 0:
            self.round_number += 1
            if logger:
                logger.info(f"🔁 Конец раунда {self.round_number-1} → начало раунда {self.round_number}")
            for player in self.players:
                player.reset_turn()
        elif logger and logger.isEnabledFor(logging.DEBUG):
            logger.debug(f"👉 Ход переходит: {prev_index} → {self.current_player_index}")

    def check_winner(self, win_target: int) -> Optional[Player]:
        for player in self.players:
            if player.coins >= win_target:
                if logger:
                    logger.info(f"🏆 ПОБЕДА! {player.name} достиг {player.coins} монет (цель: {win_target})")
                return player
        return None
# ============================================================================
# 🎲 СИМУЛЯТОР ИГРЫ (продолжение с логированием)
# ============================================================================

class ColorEngineSimulator:
    """Основной симулятор игры с расширенным логированием"""

    def __init__(self, config: GameConfig, cards: List[Card],
                 log_level: str = "INFO"):
        self.config = config
        self.base_cards = cards
        self.rng = random.Random(42)  # Фиксированный seed для воспроизводимости
        self.log_level = log_level

        if logger:
            logger.info(f"🎲 Симулятор инициализирован | Seed: 42 | Карт: {len(cards)}")

    def select_active_color(self) -> CardColor:
        """Выбирает активный цвет раунда с логированием"""
        roll = self.rng.randint(0, self.config.total_color_weight - 1)

        # Логирование выбора цвета в DEBUG
        if logger and logger.isEnabledFor(logging.DEBUG):
            logger.debug(f"🎨 Roll для цвета: {roll} из {self.config.total_color_weight}")

        if roll < self.config.color_chance_blue:
            color = CardColor.Blue
        elif roll < self.config.color_chance_blue + self.config.color_chance_gold:
            color = CardColor.Gold
        elif roll < (self.config.color_chance_blue + self.config.color_chance_gold +
                     self.config.color_chance_red):
            color = CardColor.Red
        else:
            color = CardColor.Purple

        if logger and logger.isEnabledFor(logging.INFO):
            logger.info(f"🎨 Активный цвет раунда: {color.value} (roll: {roll})")

        return color

    def get_available_cards(self, round_number: int) -> List[Card]:
        available = [c for c in self.base_cards if c.cost <= round_number]
        if logger and logger.isEnabledFor(logging.DEBUG):
            logger.debug(f"📦 Доступные карты для раунда {round_number}: "
                         f"{len(available)} из {len(self.base_cards)}")
        return available

    def select_market_cards(self, available_cards: List[Card],
                            market_size: int) -> List[Card]:
        """Выбирает карты на рынок с логированием"""
        if not available_cards:
            if logger and logger.isEnabledFor(logging.WARNING):
                logger.warning("⚠️ Нет доступных карт для рынка")
            return []

        market = []
        pool = available_cards.copy()

        for i in range(market_size):
            if not pool:
                break

            card = self._select_weighted_card(pool)
            if card:
                market.append(card)
                pool.remove(card)
                if logger and logger.isEnabledFor(logging.DEBUG):
                    logger.debug(f"🛍️  Рынок [{i+1}/{market_size}]: добавлена {card.name} "
                                 f"(weight: {card.weight})")

        if logger and logger.isEnabledFor(logging.INFO):
            market_names = ", ".join([c.name for c in market])
            logger.info(f"🛍️  Рынок сформирован: [{market_names}]")

        return market

    def _select_weighted_card(self, pool: List[Card]) -> Optional[Card]:
        """Выбирает одну карту с учетом весов"""
        if not pool:
            return None

        total_weight = sum(c.weight for c in pool)
        if total_weight <= 0:
            chosen = self.rng.choice(pool)
            if logger and logger.isEnabledFor(logging.DEBUG):
                logger.debug(f"🎲 Weight=0, случайный выбор: {chosen.name}")
            return chosen

        roll = self.rng.randint(0, total_weight - 1)
        current_sum = 0

        for card in pool:
            current_sum += card.weight
            if roll < current_sum:
                if logger and logger.isEnabledFor(logging.DEBUG):
                    logger.debug(f"🎲 Weighted roll: {roll}/{total_weight} → {card.name} "
                                 f"(cumulative: {current_sum})")
                return card

        fallback = self.rng.choice(pool)
        if logger and logger.isEnabledFor(logging.DEBUG):
            logger.debug(f"🎲 Fallback выбор: {fallback.name}")
        return fallback

    def simulate_game(self, player_count: int = 4, max_rounds: int = 100,
                      game_id: Optional[int] = None) -> Dict:
        """Симулирует одну полную игру с детальным логированием"""

        game_prefix = f"[Game#{game_id}] " if game_id else ""

        if logger:
            logger.info(f"{game_prefix}🎮 Начало симуляции | Игроков: {player_count} | "
                        f"MaxRounds: {max_rounds}")

        # Инициализация игроков
        players = []
        for i in range(player_count):
            start_coins = self.rng.randint(
                self.config.start_coins_min,
                self.config.start_coins_max
            )
            players.append(Player(name=f"Player_{i+1}", coins=start_coins))
            if logger and logger.isEnabledFor(logging.DEBUG):
                logger.debug(f"{game_prefix}👤 Создан игрок {i+1}: {start_coins} стартовых монет")

        # Инициализация состояния
        state = GameState(
            room_code=f"SIM_{game_id or '001'}",
            players=players,
            market=[],
            active_color=self.select_active_color(),
            turn_order=[p.name for p in players]
        )

        # Заполняем начальный рынок
        available = self.get_available_cards(state.round_number)
        market_size = player_count + 1
        state.market = self.select_market_cards(available, market_size)
        state.log_state_summary()

        # Игровой цикл
        stats = {
            'rounds_played': 0,
            'winner': None,
            'winner_coins': 0,
            'player_final_coins': [],
            'cards_bought': [],
            'color_history': [],
            'game_id': game_id
        }

        # Главный игровой цикл
        while state.round_number <= max_rounds:
            stats['rounds_played'] = state.round_number
            stats['color_history'].append(state.active_color.name)

            # Лог начала раунда
            if logger and logger.isEnabledFor(logging.INFO):
                logger.info(f"{game_prefix}🔹 Раунд {state.round_number} | "
                            f"Цвет: {state.active_color.value}")

            # Ход каждого игрока
            for player_idx in range(len(players)):
                current_player = state.get_current_player()
                if not current_player:
                    break

                if logger and self.config.log_detailed_turns:
                    logger.debug(f"{game_prefix}▶️  Ход: {current_player.name} (раунд {state.round_number})")
                    current_player.log_state("start_turn")

                # 1. Активация карт
                earned = current_player.activate_cards(state.active_color, players)
                logger.info(f"earned = {earned}")
                if earned > 0:
                    stats.setdefault('total_earned', 0)
                    stats['total_earned'] = stats.get('total_earned', 0) + earned
                    current_player.coins += earned
                    logger.info(f"Новый баланс игрока: {current_player.coins}")

                # 2. Покупка карты
                if (not current_player.has_bought_this_turn and state.market and
                        self.config.log_market_changes):

                    affordable = [c for c in state.market if current_player.can_afford(c)]

                    if affordable:
                        card_to_buy = max(affordable, key=lambda c: c.cost)

                        if logger and logger.isEnabledFor(logging.DEBUG):
                            logger.debug(f"{game_prefix}🛒 {current_player.name} рассматривает покупку: "
                                         f"{card_to_buy.name} (cost: {card_to_buy.cost})")

                        if current_player.buy_card(card_to_buy):
                            state.market.remove(card_to_buy)
                            stats['cards_bought'].append({
                                'player': current_player.name,
                                'card': card_to_buy.name,
                                'round': state.round_number,
                                'cost': card_to_buy.cost,
                                'color': card_to_buy.color.value
                            })

                            # Пополняем рынок
                            available = self.get_available_cards(state.round_number)
                            new_card = self._select_weighted_card(available)
                            if new_card:
                                old_market = [c.name for c in state.market]
                                state.market.append(new_card)
                                if logger and logger.isEnabledFor(logging.DEBUG):
                                    logger.debug(f"{game_prefix}🔄 Рынок обновлён: "
                                                 f"{old_market} → {[c.name for c in state.market]}")
                    elif logger and logger.isEnabledFor(logging.DEBUG):
                        logger.debug(f"{game_prefix}💸 {current_player.name}: нет доступных карт для покупки")

                # 3. Проверка победы
                winner = state.check_winner(self.config.win_target)
                if winner:
                    stats['winner'] = winner.name
                    stats['winner_coins'] = winner.coins
                    stats['player_final_coins'] = [p.coins for p in players]

                    if logger:
                        logger.info(f"{game_prefix}🏆 Игра завершена! Победитель: {winner.name} | "
                                    f"Раундов: {state.round_number} | Финальные монеты: "
                                    f"{[p.coins for p in players]}")
                    return stats

                # Переход к следующему игроку
                state.next_player()

            # Лог конца раунда
            if logger and logger.isEnabledFor(logging.DEBUG):
                coins_summary = ", ".join([f"{p.name}:{p.coins}" for p in players])
                logger.debug(f"{game_prefix}📊 Конец раунда {state.round_number} | Монеты: [{coins_summary}]")

            # Новый раунд - новый активный цвет
            state.active_color = self.select_active_color()
            state.log_state_summary()

            # Проверка на бесконечную игру
            if state.round_number >= max_rounds:
                if logger:
                    logger.warning(f"{game_prefix}⚠️ Достигнут лимит раундов ({max_rounds})")

                stats['player_final_coins'] = [p.coins for p in players]
                winner = max(players, key=lambda p: p.coins)
                stats['winner'] = winner.name
                stats['winner_coins'] = winner.coins

                if logger:
                    logger.info(f"{game_prefix}🏁 Игра завершена по лимиту | Лидер: {winner.name} "
                                f"({winner.coins} монет)")
                break

        return stats
# ============================================================================
# 📈 АНАЛИЗ ПРОГРЕССИИ (с логированием)
# ============================================================================

class ProgressionAnalyzer:
    """Анализатор прогрессии игры с логированием"""

    def __init__(self, simulator: ColorEngineSimulator):
        self.simulator = simulator
        self.results = []
        if logger:
            logger.info("📊 ProgressionAnalyzer инициализирован")

    def run_simulation(self, num_games: int = 1000, player_count: int = 4,
                       progress_interval: int = 100):
        """Запускает серию симуляций с прогресс-логами"""

        if logger:
            logger.info(f"🎲 Запуск {num_games} симуляций | Игроков: {player_count}")
            start_time = datetime.now()

        for i in range(num_games):
            result = self.simulator.simulate_game(
                player_count=player_count,
                game_id=i+1
            )
            self.results.append(result)

            # Прогресс-лог
            if (i + 1) % progress_interval == 0 and logger:
                elapsed = (datetime.now() - start_time).total_seconds()
                avg_time = elapsed / (i + 1)
                eta = avg_time * (num_games - i - 1)
                logger.info(f"  📈 Прогресс: {i + 1}/{num_games} игр | "
                            f"Время: {elapsed:.1f}с | ETA: {eta:.1f}с")

        total_time = (datetime.now() - start_time).total_seconds()
        if logger:
            logger.info(f"✅ Симуляция завершена | Всего времени: {total_time:.1f}с | "
                        f"Среднее на игру: {total_time/num_games*1000:.1f}мс")

    def analyze_progression(self):
        """Анализирует прогрессию игры с логированием результатов"""
        if not self.results:
            if logger:
                logger.warning("❌ Нет данных для анализа")
            print("❌ Нет данных для анализа")
            return

        if logger:
            logger.info("🔍 Начало анализа прогрессии...")

        # 1. Статистика по раундам
        rounds = [r['rounds_played'] for r in self.results]

        if logger:
            logger.info("╔══════════════════════════════════════════════════════════════╗")
            logger.info("║  📊 СТАТИСТИКА ПРОГРЕССИИ                                    ║")
            logger.info("╚══════════════════════════════════════════════════════════════╝")
            logger.info(f"📍 Среднее количество раундов: {np.mean(rounds):.2f}")
            logger.info(f"📍 Минимум: {min(rounds)} | Максимум: {max(rounds)}")
            logger.info(f"📍 Медиана: {np.median(rounds):.2f} | Std: {np.std(rounds):.2f}")

        # Вывод в консоль (дублируем для наглядности)
        print("\n╔══════════════════════════════════════════════════════════════╗")
        print("║  📊 СТАТИСТИКА ПРОГРЕССИИ                                    ║")
        print("╚══════════════════════════════════════════════════════════════╝\n")
        print(f"📍 Среднее количество раундов до победы: {np.mean(rounds):.2f}")
        print(f"📍 Минимум раундов: {min(rounds)}")
        print(f"📍 Максимум раундов: {max(rounds)}")
        print(f"📍 Медиана: {np.median(rounds):.2f}")
        print(f"📍 Стандартное отклонение: {np.std(rounds):.2f}\n")

        # 2. Распределение по раундам
        print("📈 Распределение игр по количеству раундов:")
        round_bins = [0] * 11
        for r in rounds:
            bin_idx = min(r // 10, 10)
            round_bins[bin_idx] += 1

        for i, count in enumerate(round_bins):
            if count > 0:
                start = i * 10 + 1
                end = (i + 1) * 10 if i < 10 else "100+"
                bar = "█" * int(count / max(round_bins) * 40)
                pct = count/len(rounds)*100
                print(f"  {start:3}-{end:>3} раундов: {bar} {count} игр ({pct:.1f}%)")
                if logger and logger.isEnabledFor(logging.DEBUG):
                    logger.debug(f"📊 Bin {start}-{end}: {count} игр ({pct:.1f}%)")
        print()

        # 3. Статистика по игрокам
        all_final_coins = []
        for r in self.results:
            all_final_coins.extend(r['player_final_coins'])

        print("💰 Статистика монет у игроков:")
        print(f"  Среднее финальное количество: {np.mean(all_final_coins):.2f}")
        print(f"  Медиана: {np.median(all_final_coins):.2f}\n")

        if logger:
            logger.info(f"💰 Coins stats: mean={np.mean(all_final_coins):.2f}, "
                        f"median={np.median(all_final_coins):.2f}")

        # 4. Анализ покупок карт
        if self.results and self.results[0].get('cards_bought'):
            all_cards = []
            for r in self.results:
                all_cards.extend(r['cards_bought'])

            print("🃏 Статистика покупок карт:")
            print(f"  Всего покупок: {len(all_cards)}")
            print(f"  Среднее покупок за игру: {len(all_cards) / len(self.results):.2f}")

            if logger:
                logger.info(f"🃏 Всего покупок: {len(all_cards)} | "
                            f"Среднее/игру: {len(all_cards)/len(self.results):.2f}")

            # По раундам
            by_round = {}
            for card in all_cards:
                round_num = card['round']
                by_round[round_num] = by_round.get(round_num, 0) + 1

            print("\n  Покупки по раундам (первые 10):")
            for round_num in sorted(by_round.keys())[:10]:
                count = by_round[round_num]
                bar = "█" * int(count / max(by_round.values()) * 30)
                print(f"    Раунд {round_num:2}: {bar} {count}")
        print()

        # 5. Анализ активных цветов
        if self.results and self.results[0].get('color_history'):
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

                if logger and logger.isEnabledFor(logging.DEBUG):
                    logger.debug(f"🎨 Color {color}: actual={actual_pct:.1f}%, "
                                 f"expected={exp_pct:.1f}%, diff={sign}{diff:.1f}%")
        print()

        if logger:
            logger.info("✅ Анализ прогрессии завершён")

    def get_balance_recommendations(self) -> List[str]:
        """Генерирует рекомендации по балансу с логированием"""
        recommendations = []

        if not self.results:
            return recommendations

        rounds = [r['rounds_played'] for r in self.results]
        avg_rounds = np.mean(rounds)

        if logger:
            logger.info("💡 Генерация рекомендаций по балансу...")

        # Анализ скорости игры
        if avg_rounds < 20:
            msg = "⚠️ Игра слишком быстрая (< 20 раундов в среднем)"
            recommendations.extend([msg, "   → Увеличьте WinTarget или уменьшите доход карт"])
            if logger:
                logger.warning(msg)
        elif avg_rounds > 60:
            msg = "⚠️ Игра слишком медленная (> 60 раундов в среднем)"
            recommendations.extend([msg, "   → Уменьшите WinTarget или увеличьте доход карт"])
            if logger:
                logger.warning(msg)
        else:
            msg = "✅ Длительность игры в целевом диапазоне (20-60 раундов)"
            recommendations.append(msg)
            if logger:
                logger.info(msg)

        # Анализ вариативности
        std_rounds = np.std(rounds)
        if std_rounds > 20:
            msg = "⚠️ Высокая вариативность длительности игр"
            recommendations.extend([msg, "   → Рассмотрите механику negative feedback для отстающих"])
            if logger:
                logger.warning(msg)
        else:
            msg = "✅ Стабильная длительность игр"
            recommendations.append(msg)
            if logger:
                logger.info(msg)

        # Анализ прогрессии
        early_rounds = [r for r in rounds if r < 15]
        late_rounds = [r for r in rounds if r > 50]

        if len(early_rounds) / len(rounds) > 0.3:
            msg = "⚠️ Слишком много быстрых игр (< 15 раундов)"
            recommendations.extend([msg, "   → Увеличьте стоимость ранних карт"])
            if logger:
                logger.warning(msg)

        if len(late_rounds) / len(rounds) > 0.2:
            msg = "⚠️ Слишком много затяжных игр (> 50 раундов)"
            recommendations.extend([msg, "   → Добавьте карты с большим эффектом для поздней игры"])
            if logger:
                logger.warning(msg)

        if logger:
            logger.info(f"✅ Сгенерировано {len(recommendations)} рекомендаций")

        return recommendations
# ============================================================================
# 🃏 ЗАГРУЗКА КАРТ (с логированием)
# ============================================================================

def create_sample_cards() -> List[Card]:
    """Создает тестовый набор карт с логированием"""
    if logger:
        logger.debug("🃏 Загрузка тестовых карт...")

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
        card = Card(
            id=data[0],
            name=f"Card_{data[0]}",
            color=data[2],
            effect=data[1],
            cost=data[3],
            reward=data[4],
            weight=data[5],
            description=data[6]
        )
        cards.append(card)

    if logger:
        logger.info(f"🃏 Загружено {len(cards)} карт")
        for color in CardColor:
            count = sum(1 for c in cards if c.color == color)
            logger.debug(f"   • {color.value}: {count} карт")

    return cards


# ============================================================================
# 🚀 ЗАПУСК СИМУЛЯЦИИ
# ============================================================================

def main():
    """Точка входа с инициализацией логирования"""

    # 0. Настройка логирования
    global logger
    logger = setup_logging(
        log_level="INFO",           # DEBUG для детальной отладки
        log_file="color_engine.log",  # None для отключения файла
        console_output=True
    )

    print("╔══════════════════════════════════════════════════════════════╗")
    print("║         🎮 COLOR ENGINE - PROGRESSION SIMULATOR 🎮          ║")
    print("║              Метод Монте-Карло для анализа баланса          ║")
    print("╚══════════════════════════════════════════════════════════════╝\n")

    logger.info("🚀 Запуск Color Engine Simulator")

    # 1. Конфигурация
    config = GameConfig(
        win_target=100,
        daily_income=1,
        start_coins_min=5,
        start_coins_max=10,
        color_chance_blue=50,
        color_chance_red=25,
        color_chance_purple=5,
        color_chance_gold=30,
        log_detailed_turns=False,  # Включите для отладки отдельных ходов
        log_card_effects=True,
        log_market_changes=True
    )
    config.log_config()
    print()

    # 2. Загрузка карт
    cards = create_sample_cards()
    print(f"🃏 Загружено {len(cards)} карт")
    print(f"   Blue: {sum(1 for c in cards if c.color == CardColor.Blue)}")
    print(f"   Red: {sum(1 for c in cards if c.color == CardColor.Red)}")
    print(f"   Gold: {sum(1 for c in cards if c.color == CardColor.Gold)}")
    print(f"   Purple: {sum(1 for c in cards if c.color == CardColor.Purple)}\n")

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
    print("╚══════════════════════════════════════════════════════════════╝\n")
    recommendations = analyzer.get_balance_recommendations()
    for rec in recommendations:
        print(rec)
        if logger and rec.startswith("⚠️"):
            logger.warning(f"💡 REC: {rec}")
        elif logger and rec.startswith("✅"):
            logger.info(f"💡 REC: {rec}")
    print()

    logger.info("✅ Симуляция завершена успешно!")
    print("✅ Симуляция завершена успешно!")

    # Закрытие логов (если нужно)
    for handler in logger.handlers:
        handler.close()


if __name__ == "__main__":
    main()