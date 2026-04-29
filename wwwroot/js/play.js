// ============================================================================
// Play.js - Основная логика игрового процесса
// Tiny City Online - Game Client Logic
// ============================================================================

let lastActivePlayer = "";
let previousMarketCount = 0;

document.addEventListener("DOMContentLoaded", () => {
    // Инициализация кнопок летописи
    initLogToggle();

    // Запуск подключения
    GameApp.start(() => {
        console.log("[Play.js] Игра подключена. Запрашиваю состояние стола...");
        GameApp.connection.invoke("InitGameView", GameApp.roomCode);
    });

    // ========================================================================
    // ⚠️ ВАЖНО: Используем GameApp.connection.on(), а не GameApp.on()
    // ========================================================================

    GameApp.connection.on("UpdateTable", (data) => {
        renderGame(data);
    });

    GameApp.connection.on("GameOver", (winnerName) => {
        showVictoryScreen(winnerName);
    });

    GameApp.connection.on("ShowMessage", (msg, type) => {
        addToLog(msg, type);
        showTurnAlert(msg);
    });
});

// ============================================================================
// РЕНДЕРИНГ ИГРЫ
// ============================================================================

function renderGame(data) {
    const myName = GameApp.userName;
    const currentPlayerName = data.currentPlayer;
    const isMyTurn = (myName === currentPlayerName);
    const activeColorStr = String(data.activeColor).toLowerCase();
    const myData = data.players.find(p => p.name === myName);

    updateRoundDisplay(data.roundNumber);
    updateTurnNotification(isMyTurn, myName, currentPlayerName);
    updatePlayersList(data.players, myName, currentPlayerName);
    updatePhaseIndicator(activeColorStr);
    updateMarket(data.market, isMyTurn, myData);
    updateInventory(myData, isMyTurn, activeColorStr);
    updateEndTurnButton(isMyTurn);
    updateDeckCount(data.deckCount);
}

function updateRoundDisplay(roundNumber) {
    const roundElem = document.getElementById("roundNum");
    if (roundElem) roundElem.innerText = roundNumber || 1;
}

function updateTurnNotification(isMyTurn, myName, currentPlayerName) {
    if (isMyTurn && lastActivePlayer !== myName) {
        showTurnAlert("🏰 ВАШ ХОД, ПОСЕЛЕНЕЦ!");
    }
    lastActivePlayer = currentPlayerName;
}

function updatePlayersList(players, myName, currentPlayerName) {
    const playersDiv = document.getElementById("playersList");
    if (!playersDiv) return;

    playersDiv.innerHTML = players.map(p => {
        const isTurn = p.name === currentPlayerName;
        const isMe = p.name === myName;

        // Формируем HTML для любимого цвета, если он есть
        let favColorHtml = '';
        if (p.favoriteColor) {
            const colorClass = p.favoriteColor.toLowerCase();
            const icons = {
                'blue': '🔵',
                'gold': '🟡',
                'red': '🔴',
                'purple': '🟣'
            };
            const icon = icons[colorClass] || '⚪';
            // Используем title для подсказки с описанием эффекта
            const tooltip = getFavoriteColorTooltip(p.favoriteColor);
            favColorHtml = `<span class="fav-color-indicator" title="${tooltip}">${icon}</span>`;
        }

        return `<div class="player-pill ${isTurn ? 'active-glow' : ''}">
            ${p.name} ${isMe ? '<strong>(Вы)</strong>' : ''}: 
            <strong>${p.coins}💰</strong>
            ${favColorHtml}
        </div>`;
    }).join('');
}

function getFavoriteColorTooltip(color) {
    switch(color) {
        case 'Blue': return 'Любимый цвет: Защита от кражи карт, уязвим к красным (+2 урон)';
        case 'Gold': return 'Любимый цвет: Защита от кражи денег (50%), приоритетная цель для фиолетовых';
        case 'Red': return 'Любимый цвет: Монополия на синие, штраф к золоту, двойная кража у синих';
        case 'Purple': return 'Любимый цвет: +50% к золоту, игнор чужих синих, охотник за золотыми';
        default: return '';
    }
}

function updatePhaseIndicator(colorClass) {
    const colorBox = document.getElementById("currentPhaseColor");
    if (colorBox) colorBox.className = "color-indicator " + colorClass;
}

function updateMarket(market, isMyTurn, myData) {
    const marketDiv = document.getElementById("marketCards");
    if (!marketDiv) return;

    const isReplenishing = market.length > previousMarketCount;

    marketDiv.innerHTML = market.map((card, index) => {
        const canAfford = isMyTurn && !myData?.hasBoughtThisTurn && myData?.coins >= card.cost;
        const animClass = (isReplenishing && index >= previousMarketCount) ? 'market-card-anim' : '';

        const cardImg = card.icon
            ? `<img src="${card.icon}" class="card-illustration" alt="${card.name}">`
            : `<div class="no-icon">🏰</div>`;

        return `
            <div class="card-item ${animClass} ${canAfford ? 'can-afford' : 'disabled'}" 
                 onclick="buyCard(${card.id}, ${canAfford})">
                <div class="card-header ${card.color.toLowerCase()}">${card.name}</div>
                <div class="card-body">
                    <div class="img-container">${cardImg}</div>
                    <div class="card-price">Цена: ${card.cost}💰</div>
                </div>
                <div class="card-footer" title="${card.description}">
                    ${card.description || 'Базовое поселение'}
                </div>
            </div>`;
    }).join('');

    previousMarketCount = market.length;
}

function updateInventory(myData, isMyTurn, serverActiveColor) {
    const inventoryDiv = document.getElementById("myInventory");
    if (!myData || !inventoryDiv) return;

    const myCoinsElem = document.getElementById("myCoins");
    if (myCoinsElem) myCoinsElem.innerText = myData.coins;

    if (myData.inventory?.length > 0) {
        inventoryDiv.innerHTML = myData.inventory.map(c => {
            const cardColor = String(c.color || "").toLowerCase();
            const isRightPhase = (cardColor === serverActiveColor);
            const canActivate = isRightPhase && isMyTurn && !c.isUsed;
            const usedClass = c.isUsed ? "card-used" : "";
            const glowClass = canActivate ? "active-card-glow" : "";

            const cardImgHtml = c.icon
                ? `<img src="${c.icon}" class="card-illustration" alt="${c.name}">`
                : `<div class="no-icon">🏰</div>`;

            const dynamicBonus = calculateCardBonus(c, myData.inventory);

            return `
                <div class="card-mini-container ${!isRightPhase ? 'wrong-phase' : ''} 
                                                ${canActivate ? 'clickable-card' : ''}" 
                     onclick="useCard(${c.id}, ${canActivate}, event)">
                    <div class="card-mini-inner">
                        <div class="card-front ${glowClass} ${usedClass}">
                            <div class="card-header ${cardColor}">${c.name}</div>
                            <div class="card-body">
                                <div class="img-container" style="height: 90px;"> 
                                    ${cardImgHtml}
                                </div>
                                <div style="font-weight: bold; color: #8b4513; font-size: 0.9rem;">
                                    БОНУС: +${dynamicBonus}💰
                                </div>
                            </div>
                            <div class="card-mini-footer">
                                ${c.description || 'Ресурс поселения'}
                            </div>
                        </div>
                        <div class="card-back">
                            <strong style="color: #ffd700; font-size: 0.8rem; text-transform: uppercase;">
                                ${c.name}
                            </strong>
                            <p style="font-size: 0.7rem; margin: 10px 0;">${c.narrative}</p>
                            <div class="dsl-info">⚡ ${c.effect}</div>
                        </div>
                    </div>
                </div>`;
        }).join('');
    } else {
        inventoryDiv.innerHTML = "<p style='opacity:0.5; padding: 20px; color: #4a2c2a;'>Ваши земли пока пустуют. Купите что-нибудь на рынке!</p>";
    }
}

function updateEndTurnButton(isMyTurn) {
    const endBtn = document.getElementById("endTurnBtn");
    if (endBtn) endBtn.disabled = !isMyTurn;
}

function updateDeckCount(deckCount) {
    const deckElem = document.getElementById("deckCount");
    const deckStack = document.querySelector(".deck-stack");

    if (deckElem) {
        deckElem.innerText = (deckCount > 0) ? `${deckCount} 🂠` : "ПУСТО";
    }

    if (deckStack) {
        deckStack.classList.toggle("empty", deckCount <= 0);
    }
}

// ============================================================================
// ДЕЙСТВИЯ ИГРОКА
// ============================================================================

function buyCard(cardId, canAfford) {
    if (!canAfford) return;

    GameApp.connection.invoke("PlayerClickCard", GameApp.roomCode, cardId)
        .catch(err => console.error("[Play.js] Ошибка покупки:", err));
}

function useCard(cardId, canActivate, event) {
    if (!canActivate) return;

    // Анимация монет
    const coin = document.createElement("div");
    coin.className = "coin-anim";
    coin.innerText = "+💰";
    coin.style.left = event.pageX + "px";
    coin.style.top = event.pageY + "px";
    document.body.appendChild(coin);
    setTimeout(() => coin.remove(), 1000);

    GameApp.connection.invoke("ActivateCard", GameApp.roomCode, cardId)
        .catch(err => console.error("[Play.js] Ошибка активации:", err));
}

function finishTurn() {
    GameApp.connection.invoke("EndTurn", GameApp.roomCode)
        .catch(err => console.error("[Play.js] Ошибка завершения хода:", err));
}

// ============================================================================
// УВЕДОМЛЕНИЯ И ЛОГИ
// ============================================================================

function showTurnAlert(text) {
    const alertBox = document.getElementById("turn-alert");
    if (!alertBox) return;

    alertBox.innerText = text;
    alertBox.style.display = "block";
    setTimeout(() => { alertBox.style.display = "none"; }, 3000);
}

function addToLog(message, type = "") {
    const logDiv = document.getElementById("gameLog");
    if (!logDiv) return;

    const item = document.createElement("div");
    item.className = `log-item ${type}`;

    const now = new Date();
    const timeStr = `[${now.getHours()}:${now.getMinutes().toString().padStart(2, '0')}] `;
    item.innerHTML = `<span>${timeStr}${message}</span>`;

    logDiv.appendChild(item);
    logDiv.scrollTop = logDiv.scrollHeight;
}

// ============================================================================
// ЭКРАН ПОБЕДЫ
// ============================================================================

function showVictoryScreen(winnerName) {
    const overlay = document.createElement("div");
    overlay.className = "victory-overlay";
    overlay.innerHTML = `
        <div class="victory-box">
            <h1 class="victory-title">👑 ТРИУМФ 👑</h1>
            <div class="winner-name">${winnerName.toUpperCase()}</div>
            <p>Возвёл величайшую столицу и накопил богатства!</p>
            <hr class="victory-hr">
            <div id="countdown-msg">Возврат в порт через <span id="timer-sec">5</span>...</div>
        </div>
        <div id="coin-rain"></div>
    `;

    document.body.appendChild(overlay);
    startCoinRain();

    let seconds = 5;
    const timerElem = document.getElementById("timer-sec");
    const interval = setInterval(() => {
        seconds--;
        if (timerElem) timerElem.innerText = seconds;
        if (seconds <= 0) {
            clearInterval(interval);
            window.location.href = "/";
        }
    }, 1000);
}

function startCoinRain() {
    const container = document.getElementById("coin-rain");
    if (!container) return;

    for (let i = 0; i < 50; i++) {
        const coin = document.createElement("div");
        coin.className = "falling-coin";
        coin.innerText = "💰";
        coin.style.left = Math.random() * 100 + "vw";
        coin.style.animationDelay = Math.random() * 2 + "s";
        coin.style.fontSize = (Math.random() * 20 + 20) + "px";
        container.appendChild(coin);
    }
}

// ============================================================================
// ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ
// ============================================================================

function calculateCardBonus(card, allCards) {
    if (!card.effect) return card.reward || 0;

    const parts = card.effect.split(' ');
    const cmd = parts[0].toUpperCase();

    if (cmd === "GETBY") {
        const targetColor = parts[1].toLowerCase();
        const multiplier = parseInt(parts[2]);
        const count = allCards.filter(c => String(c.color).toLowerCase() === targetColor).length;
        return count * multiplier;
    }

    return card.reward || 0;
}

function initLogToggle() {
    const logContainer = document.getElementById("logContainer");
    const logToggleBtn = document.getElementById("logToggleBtn");
    const logOpenBtn = document.getElementById("logOpenBtn");

    // Проверка сохранённого состояния в localStorage
    const isCollapsed = localStorage.getItem("logCollapsed") === "true";
    if (isCollapsed) {
        logContainer?.classList.add("collapsed");
        logOpenBtn?.style.setProperty("display", "flex");
    }

    // Кнопка закрытия
    logToggleBtn?.addEventListener("click", () => {
        logContainer?.classList.add("collapsed");
        logOpenBtn?.style.setProperty("display", "flex");
        localStorage.setItem("logCollapsed", "true");
    });

    // Кнопка открытия
    logOpenBtn?.addEventListener("click", () => {
        logContainer?.classList.remove("collapsed");
        logOpenBtn?.style.setProperty("display", "none");
        localStorage.setItem("logCollapsed", "false");
    });
}

// ============================================================================
// ОБРАБОТКА ОШИБОК
// ============================================================================

window.addEventListener('error', (event) => {
    console.error('[Play.js] Глобальная ошибка:', event.error);
    addToLog(`⚠️ Ошибка: ${event.error?.message || 'Неизвестная ошибка'}`, 'important');
});

window.addEventListener('unhandledrejection', (event) => {
    console.error('[Play.js] Необработанное исключение Promise:', event.reason);
});