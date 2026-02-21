// ============================================================================
// GameApp - Глобальный объект для SignalR подключения
// Tiny City Online - Game Connection Manager
// ============================================================================

const GameApp = {
    /**
     * SignalR соединение
     */
    connection: null,

    /**
     * Код текущей комнаты
     */
    roomCode: null,

    /**
     * Имя текущего пользователя
     */
    userName: null,

    /**
     * Инициализация подключения
     */
    init() {
        // Проверяем наличие библиотеки SignalR
        if (typeof signalR === 'undefined') {
            console.error('[GameApp] SignalR библиотека не загружена!');
            return false;
        }

        // Получаем параметры из URL
        const params = new URLSearchParams(window.location.search);
        this.roomCode = params.get('code');
        this.userName = params.get('user');

        // Создаем подключение
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl('/gameHub')
            .withAutomaticReconnect([0, 2000, 5000, 10000])
            .configureLogging(signalR.LogLevel.Information)
            .build();

        console.log('[GameApp] Инициализировано. Room:', this.roomCode, 'User:', this.userName);
        return true;
    },

    /**
     * Запуск подключения
     * @param {Function} onStarted - Callback после успешного подключения
     */
    start: async function(onStarted) {
        if (!this.connection) {
            const initialized = this.init();
            if (!initialized) return;
        }

        if (this.connection.state === signalR.HubConnectionState.Connected) {
            console.log('[GameApp] Уже подключено');
            if (onStarted) onStarted();
            return;
        }

        try {
            await this.connection.start();
            console.log('[GameApp] SignalR Connected. State:', this.connection.state);
            if (onStarted) onStarted();
        } catch (err) {
            console.error('[GameApp] Connection failed:', err);
            setTimeout(() => this.start(onStarted), 5000);
        }
    },

    /**
     * Подписка на событие от сервера
     * @param {string} eventName - Имя события
     * @param {Function} handler - Обработчик события
     */
    on: function(eventName, handler) {
        if (this.connection) {
            this.connection.on(eventName, handler);
            console.log('[GameApp] Подписка на событие:', eventName);
        } else {
            console.warn('[GameApp] Попытка подписки до инициализации соединения');
        }
    },

    /**
     * Отписка от события
     * @param {string} eventName - Имя события
     * @param {Function} handler - Обработчик для отписки
     */
    off: function(eventName, handler) {
        if (this.connection) {
            this.connection.off(eventName, handler);
        }
    },

    /**
     * Отправка данных на сервер
     * @param {string} methodName - Имя метода хаба
     * @param  {...any} args - Аргументы метода
     */
    send: async function(methodName, ...args) {
        if (!this.connection) {
            console.warn('[GameApp] Попытка отправки без подключения');
            return;
        }

        try {
            await this.connection.invoke(methodName, ...args);
        } catch (err) {
            console.error('[GameApp] Ошибка отправки', methodName + ':', err);
        }
    },

    /**
     * Остановка соединения
     */
    stop: async function() {
        if (this.connection) {
            await this.connection.stop();
            console.log('[GameApp] Соединение остановлено');
        }
    }
};

// ============================================================================
// Инициализация при загрузке страницы
// ============================================================================
document.addEventListener('DOMContentLoaded', () => {
    GameApp.init();
});