using System.ComponentModel.DataAnnotations;

namespace TinyCityCardGame_online.Models;

/// <summary>
/// Модель представления для главной страницы с данными игрока и комнаты.
/// </summary>
public class HomeViewModel
{
    private const int MinNameLength = 2;
    private const int MaxNameLength = 20;
    private const int RoomCodeLength = 4;

    /// <summary>
    /// Имя игрока для входа в игру.
    /// </summary>
    [Required(ErrorMessage = "Имя игрока обязательно")]
    [StringLength(MaxNameLength, MinimumLength = MinNameLength, 
        ErrorMessage = "Имя должно содержать от 2 до 20 символов")]
    [RegularExpression(@"^[a-zA-Zа-яА-Я0-9_]+$", 
        ErrorMessage = "Имя может содержать только буквы, цифры и подчеркивание")]
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>
    /// Код комнаты для присоединения к существующей сессии.
    /// </summary>
    [StringLength(RoomCodeLength, MinimumLength = RoomCodeLength, 
        ErrorMessage = "Код комнаты должен содержать ровно 4 символа")]
    [RegularExpression(@"^[0-9]+$", 
        ErrorMessage = "Код комнаты должен содержать только цифры")]
    public string RoomCode { get; set; } = string.Empty;

    /// <summary>
    /// Проверяет, заполнены ли данные для создания новой комнаты.
    /// </summary>
    /// <returns>True если данные валидны для создания комнаты.</returns>
    public bool CanCreateRoom() => !string.IsNullOrWhiteSpace(PlayerName);

    /// <summary>
    /// Проверяет, заполнены ли данные для присоединения к комнате.
    /// </summary>
    /// <returns>True если данные валидны для входа в комнату.</returns>
    public bool CanJoinRoom() => !string.IsNullOrWhiteSpace(PlayerName) && !string.IsNullOrWhiteSpace(RoomCode);

    /// <summary>
    /// Генерирует случайный код комнаты.
    /// </summary>
    /// <returns>4-значный код комнаты.</returns>
    public static string GenerateRoomCode() => 
        new Random().Next(1000, 9999).ToString();

    /// <summary>
    /// Очищает данные модели.
    /// </summary>
    public void Clear()
    {
        PlayerName = string.Empty;
        RoomCode = string.Empty;
    }

    /// <summary>
    /// Возвращает сообщение об ошибке для длины имени.
    /// </summary>
    /// <returns>Текст ошибки валидации.</returns>
    public static string GetNameLengthError() => 
        $"Имя должно содержать от {MinNameLength} до {MaxNameLength} символов";

    /// <summary>
    /// Возвращает сообщение об ошибке для кода комнаты.
    /// </summary>
    /// <returns>Текст ошибки валидации.</returns>
    public static string GetRoomCodeError() => 
        $"Код комнаты должен содержать ровно {RoomCodeLength} символа";
}