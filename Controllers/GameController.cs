using Microsoft.AspNetCore.Mvc;

namespace TinyCityCardGame_online.Controllers;

public class GameController : Controller
{
    public IActionResult Lobby(string code, string user)
    {
        ViewBag.RoomCode = code;
        ViewBag.UserName = user;
        return View();
    }
    
    public IActionResult Play(string code, string user)
    {
        ViewBag.RoomCode = code;
        ViewBag.UserName = user;
        return View();
    }
}
