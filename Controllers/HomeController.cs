using Microsoft.AspNetCore.Mvc;
using TinyCityCardGame_online.Models;
using TinyCityCardGame_online.Services;

namespace TinyCityCardGame_online.Controllers;

public class HomeController : Controller
{
    private readonly GameSessionService _sessionService;

    // Внедряем сервис через конструктор
    public HomeController(GameSessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public IActionResult Index() => View();

    [HttpPost]
    public IActionResult CreateLobby(HomeViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.PlayerName)) return RedirectToAction("Index");

        // Генерируем красивый короткий код (4 символа)
        string roomCode = Guid.NewGuid().ToString().Substring(0, 4).ToUpper();
        
        return RedirectToAction("Lobby", "Game", new { code = roomCode, user = model.PlayerName });
    }

    [HttpPost]
    public IActionResult JoinLobby(HomeViewModel model)
    {
        // Проверка: введена ли информация
        if (string.IsNullOrEmpty(model.RoomCode) || string.IsNullOrEmpty(model.PlayerName))
        {
            TempData["Error"] = "Заполните все поля!";
            return RedirectToAction("Index");
        }

        var players = _sessionService.GetPlayers(model.RoomCode);

        // Проверка существования комнаты
        if (players.Count == 0)
        {
            TempData["Error"] = "Такой гавани не существует!";
            return RedirectToAction("Index");
        }

        // Проверка уникальности имени
        if (players.Any(p => p.Equals(model.PlayerName, StringComparison.OrdinalIgnoreCase)))
        {
            TempData["Error"] = "Имя уже занято в этой комнате!";
            return RedirectToAction("Index");
        }

        return RedirectToAction("Lobby", "Game", new { code = model.RoomCode, user = model.PlayerName });
    }
}
