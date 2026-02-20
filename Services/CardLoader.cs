using TinyCityCardGame_online.Models;

namespace TinyCityCardGame_online.Services;

using ClosedXML.Excel;

public class CardLoader {
    public List<Card> LoadCardsFromExcel(string filePath)
    {
        var cards = new List<Card>();
        // Путь к папке с картинками
        string imgPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "cards");

        try
        {
            using var workbook = new XLWorkbook(filePath);
            var worksheet = workbook.Worksheet(1);
            var rows = worksheet.RangeUsed().RowsUsed();

            foreach (var row in rows)
            {
                string nameValue = row.Cell(1).GetString().Trim();
                if (string.IsNullOrEmpty(nameValue) || nameValue.Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;

                string iconFile = row.Cell(6).GetString().Trim();
            
                // ПРОВЕРКА: Существует ли файл картинки?
                if (!File.Exists(Path.Combine(imgPath, iconFile)))
                {
                    Console.WriteLine($"[WARN] Строка {row.RowNumber()}: Файл '{iconFile}' не найден в {imgPath}. Карта '{nameValue}' будет без картинки.");
                }

                try
                {
                    cards.Add(new Card
                    {
                        Id = Guid.NewGuid().GetHashCode(),
                        Name = nameValue,
                        Color = Enum.Parse<CardColor>(row.Cell(2).GetString().Trim(), true),
                        Effect = row.Cell(3).GetString().Trim(),
                        Cost = row.Cell(4).GetValue<int>(),
                        Reward = row.Cell(5).GetValue<int>(),
                        Icon = "/images/cards/" + iconFile, // Сохраняем относительный путь для веба
                        Description = row.Cell(7).GetString()
                    });
                }
                catch (Exception ex) { Console.WriteLine($"[ERROR] Ошибка в строке {row.RowNumber()}: {ex.Message}"); }
            }
            Console.WriteLine($"[SUCCESS] Загружено карт из Excel: {cards.Count}");
        }
        catch (Exception ex) { Console.WriteLine($"[CRITICAL] Ошибка Excel: {ex.Message}"); }
        return cards;
    }
}
