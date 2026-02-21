using Microsoft.AspNetCore.Mvc;
using TinyCityCardGame_online.Services;

namespace TinyCityCardGame_online.Controllers;

/// <summary>
/// Контроллер игровых страниц (лобби и игра).
/// </summary>
public class GameController : Controller
{
    private readonly GameSessionService _sessionService;
    private readonly ILogger<GameController> _logger;

    /// <summary>
    /// Инициализирует новый экземпляр класса GameController.
    /// </summary>
    /// <param name="sessionService">Сервис управления сессиями.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    public GameController(GameSessionService sessionService, ILogger<GameController> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    /// <summary>
    /// Страница лобби перед началом игры.
    /// </summary>
    /// <param name="code">Код комнаты.</param>
    /// <param name="user">Имя игрока.</param>
    public IActionResult Lobby(string code, string user)
    {
        _logger.LogDebug("Открытие лобби: Room={Code}, User={User}", code, user);

        ViewBag.RoomCode = code;
        ViewBag.UserName = user;
        ViewBag.Players = _sessionService.GetPlayers(code);

        return View();
    }

    /// <summary>
    /// Страница активной игры.
    /// </summary>
    /// <param name="code">Код комнаты.</param>
    /// <param name="user">Имя игрока.</param>
    public IActionResult Play(string code, string user)
    {
        _logger.LogDebug("Открытие игры: Room={Code}, User={User}", code, user);

        ViewBag.RoomCode = code;
        ViewBag.UserName = user;

        // TODO: Добавить проверку, запущена ли игра в этой комнате
        // if (!_sessionService.IsGameStarted(code)) return RedirectToAction("Lobby");

        return View();
    }
}
