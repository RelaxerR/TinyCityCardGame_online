using Microsoft.AspNetCore.Mvc;
using TinyCityCardGame_online.Models;

namespace TinyCityCardGame_online.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();

    [HttpPost]
    public IActionResult CreateLobby(HomeViewModel model)
    {
        if (string.IsNullOrEmpty(model.PlayerName)) return View("Index");

        // Генерируем уникальный код комнаты
        string roomCode = Guid.NewGuid().ToString().Substring(0, 6).ToUpper();
        
        // Переходим в лобби, передавая код и имя
        return RedirectToAction("Lobby", "Game", new { code = roomCode, user = model.PlayerName });
    }

    [HttpPost]
    public IActionResult JoinLobby(HomeViewModel model)
    {
        if (string.IsNullOrEmpty(model.RoomCode)) return View("Index");
        return RedirectToAction("Lobby", "Game", new { code = model.RoomCode, user = model.PlayerName });
    }
}
