// Глобальный объект для доступа из любой вьюшки
const GameApp = {
    connection: new signalR.HubConnectionBuilder()
        .withUrl("/gameHub")
        .withAutomaticReconnect([0, 2000, 5000, 10000]) // Пытаться переподключиться
        .configureLogging(signalR.LogLevel.Information)
        .build(),

    roomCode: new URLSearchParams(window.location.search).get('code'),
    userName: new URLSearchParams(window.location.search).get('user'),

    start: async function(onStarted) {
        if (this.connection.state === signalR.HubConnectionState.Connected) return;

        try {
            await this.connection.start();
            console.log("SignalR Connected. State:", this.connection.state);
            if (onStarted) onStarted();
        } catch (err) {
            console.error("Connection failed: ", err);
            setTimeout(() => this.start(onStarted), 5000); // Рекурсивный перезапуск
        }
    }
};
