using ClosedXML.Excel;
using TinyCityCardGame_online.Models;

namespace TinyCityCardGame_online.Services;

/// <summary>
/// Сервис загрузки карт из Excel-файла конфигурации.
/// </summary>
public class CardLoader
{
    private readonly ILogger<CardLoader> _logger;
    private const string CardsSubPath = "wwwroot/images/cards";
    private const int DefaultWeight = 50;
    private const int MinWeight = 1;
    private const int MaxWeight = 100;

    /// <summary>
    /// Инициализирует новый экземпляр класса CardLoader.
    /// </summary>
    /// <param name="logger">Логгер для записи событий загрузки.</param>
    public CardLoader(ILogger<CardLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Загружает список карт из Excel-файла.
    /// </summary>
    /// <param name="filePath">Путь к Excel-файлу с картами.</param>
    /// <returns>Список загруженных карт.</returns>
    public List<Card> LoadCardsFromExcel(string filePath)
    {
        var cards = new List<Card>();
        
        if (!File.Exists(filePath))
        {
            _logger.LogError("Файл карт не найден: {FilePath}", filePath);
            return cards;
        }

        var imgPath = Path.Combine(Directory.GetCurrentDirectory(), CardsSubPath);
        _logger.LogInformation("Начало загрузки карт из {FilePath}. Путь к изображениям: {ImgPath}", filePath, imgPath);

        try
        {
            using var workbook = new XLWorkbook(filePath);
            var worksheet = workbook.Worksheet(1);
            var rows = worksheet.RangeUsed()?.RowsUsed();

            if (rows != null)
                cards.AddRange(rows.Select(row => ParseCardRow(row, imgPath)).OfType<Card>());

            _logger.LogInformation("Успешно загружено {Count} карт из Excel", cards.Count);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Критическая ошибка при загрузке Excel: {Message}", ex.Message);
        }

        return cards;
    }

    /// <summary>
    /// Парсит строку Excel и создает объект карты.
    /// </summary>
    /// <param name="row">Строка данных из Excel.</param>
    /// <param name="imgPath">Путь к папке с изображениями.</param>
    /// <returns>Объект карты или null при ошибке.</returns>
    private Card? ParseCardRow(IXLRangeRow row, string imgPath)
    {
        try
        {
            var nameValue = row.Cell(1).GetString().Trim();
            
            if (IsHeaderRow(nameValue))
                return null;

            var iconFile = row.Cell(6).GetString().Trim();
            ValidateIconFile(iconFile, imgPath, row.RowNumber(), nameValue);

            var card = new Card
            {
                Id = GenerateCardId(),
                Name = nameValue,
                Color = ParseCardColor(row.Cell(2).GetString().Trim(), row.RowNumber(), nameValue),
                Effect = row.Cell(3).GetString().Trim(),
                Cost = row.Cell(4).GetValue<int>(),
                Reward = row.Cell(5).GetValue<int>(),
                Icon = BuildIconPath(iconFile),
                Description = row.Cell(7).GetString(),
                Weight = ParseCardWeight(row.Cell(8).GetValue<int>()),
                Narrative = row.Cell(9).GetString(),
            };

            var validationErrors = card.Validate();
            if (validationErrors.Count != 0)
            {
                _logger.LogWarning("Карта {CardName} имеет ошибки валидации: {Errors}", 
                    nameValue, string.Join("; ", validationErrors));
            }

            return card;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка парсинга строки {RowNumber}: {Message}", row.RowNumber(), ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Проверяет, является ли строка заголовком таблицы.
    /// </summary>
    /// <param name="nameValue">Значение ячейки имени.</param>
    /// <returns>True если это заголовок.</returns>
    private static bool IsHeaderRow(string nameValue) => 
        string.IsNullOrEmpty(nameValue) || 
        nameValue.Equals("Name", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Проверяет существование файла иконки.
    /// </summary>
    /// <param name="iconFile">Имя файла иконки.</param>
    /// <param name="imgPath">Путь к папке изображений.</param>
    /// <param name="rowNumber">Номер строки для логирования.</param>
    /// <param name="cardName">Название карты.</param>
    private void ValidateIconFile(string iconFile, string imgPath, int rowNumber, string cardName)
    {
        if (!string.IsNullOrEmpty(iconFile) && 
            !File.Exists(Path.Combine(imgPath, iconFile)))
        {
            _logger.LogWarning(
                "Строка {RowNumber}: Файл '{IconFile}' не найден в {ImgPath}. Карта '{CardName}' будет без картинки.",
                rowNumber, iconFile, imgPath, cardName);
        }
    }

    /// <summary>
    /// Парсит цвет карты из строки.
    /// </summary>
    /// <param name="colorValue">Строковое значение цвета.</param>
    /// <param name="rowNumber">Номер строки для логирования.</param>
    /// <param name="cardName">Название карты.</param>
    /// <returns>Значение перечисления CardColor.</returns>
    private CardColor ParseCardColor(string colorValue, int rowNumber, string cardName)
    {
        try
        {
            return Enum.Parse<CardColor>(colorValue, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Строка {RowNumber}: Неверный цвет '{ColorValue}' для карты '{CardName}'. Используется Blue по умолчанию.",
                rowNumber, colorValue, cardName);
            return CardColor.Blue;
        }
    }

    /// <summary>
    /// Парсит вес карты с валидацией диапазона.
    /// </summary>
    /// <param name="weight">Значение веса из Excel.</param>
    /// <returns>Валидированное значение веса.</returns>
    private static int ParseCardWeight(int weight)
    {
        return weight <= 0 ? DefaultWeight : Math.Clamp(weight, MinWeight, MaxWeight);
    }

    /// <summary>
    /// Генерирует уникальный идентификатор карты.
    /// </summary>
    /// <returns>Уникальный ID.</returns>
    private static int GenerateCardId() => Guid.NewGuid().GetHashCode();

    /// <summary>
    /// Строит относительный путь к иконке для веб-доступа.
    /// </summary>
    /// <param name="iconFile">Имя файла иконки.</param>
    /// <returns>Относительный путь.</returns>
    private static string BuildIconPath(string iconFile) => 
        string.IsNullOrEmpty(iconFile) ? "/images/cards/placeholder.png" : $"/images/cards/{iconFile}";
}