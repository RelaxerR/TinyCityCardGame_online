using Microsoft.AspNetCore.Mvc;
using TinyCityCardGame_online.Models;
using TinyCityCardGame_online.Services;

namespace TinyCityCardGame_online.Controllers;

/// <summary>
/// Контроллер главной страницы и лобби.
/// </summary>
public class HomeController : Controller
{
    private readonly GameSessionService _sessionService;
    private readonly ILogger<HomeController> _logger;

    /// <summary>
    /// Инициализирует новый экземпляр класса HomeController.
    /// </summary>
    /// <param name="sessionService">Сервис управления сессиями.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    public HomeController(GameSessionService sessionService, ILogger<HomeController> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    /// <summary>
    /// Главная страница входа в игру.
    /// </summary>
    public IActionResult Index() => View();

    /// <summary>
    /// Создает новое лобби для игры.
    /// </summary>
    /// <param name="model">Данные формы создания лобби.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CreateLobby(HomeViewModel model)
    {
        _logger.LogInformation("Попытка создания лобби игроком {PlayerName}", model.PlayerName);

        if (string.IsNullOrWhiteSpace(model.PlayerName))
        {
            _logger.LogWarning("Некорректные данные для создания лобби");
            return RedirectToAction("Index");
        }

        var roomCode = GenerateRoomCode();
        _sessionService.CreateRoom(roomCode);

        _logger.LogInformation("Создано лобби {RoomCode} для игрока {PlayerName}", roomCode, model.PlayerName);

        return RedirectToAction("Lobby", "Game", new { code = roomCode, user = model.PlayerName });
    }

    /// <summary>
    /// Присоединяет игрока к существующему лобби.
    /// </summary>
    /// <param name="model">Данные формы присоединения.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult JoinLobby(HomeViewModel model)
    {
        _logger.LogInformation("Попытка присоединения к комнате {RoomCode} игроком {PlayerName}", 
            model.RoomCode, model.PlayerName);

        if (!ValidateJoinLobby(model))
            return RedirectToAction("Index");

        return RedirectToAction("Lobby", "Game", new { code = model.RoomCode, user = model.PlayerName });
    }

    /// <summary>
    /// Валидирует данные для присоединения к лобби.
    /// </summary>
    /// <param name="model">Данные формы.</param>
    /// <returns>True если данные валидны.</returns>
    private bool ValidateJoinLobby(HomeViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.RoomCode) || string.IsNullOrWhiteSpace(model.PlayerName))
        {
            SetError("Заполните все поля!");
            return false;
        }

        if (!_sessionService.RoomExists(model.RoomCode))
        {
            SetError("Такой гавани не существует!");
            return false;
        }

        if (!_sessionService.IsAvailableToJoin(model.RoomCode))
        {
            SetError("В этой гавани уже максимальное кол-во игроков.");
            return false;
        }

        if (_sessionService.IsPlayerInRoom(model.RoomCode, model.PlayerName))
        {
            SetError("Имя уже занято в этой комнате!");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Устанавливает сообщение об ошибке во TempData.
    /// </summary>
    /// <param name="message">Текст ошибки.</param>
    private void SetError(string message)
    {
        TempData["Error"] = message;
        _logger.LogWarning("Ошибка присоединения к лобби: {Message}", message);
    }

    /// <summary>
    /// Генерирует уникальный код комнаты.
    /// </summary>
    /// <returns>4-значный код комнаты.</returns>
    private static string GenerateRoomCode() => 
        new Random().Next(1000, 9999).ToString();

    /// <summary>
    /// Страница политики конфиденциальности.
    /// </summary>
    public IActionResult Privacy() => View();
}