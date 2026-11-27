using System.Globalization;
using ClosedXML.Excel;
using Hms.Modules.Reporting.Application;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace Hms.Modules.Reporting.Infrastructure;

/// <summary>
/// Выгрузка отчёта-таблицы: Excel (ClosedXML, MIT) и PDF (PDFsharp/MigraDoc, MIT).
/// В файле — гостиница, название, период, фильтры, показатели и таблица с итогами; числа остаются числами (Excel считает дальше).
/// </summary>
internal sealed class ReportExporter : IReportExporter
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public byte[] ToExcel(ReportTable report, string hotelName)
    {
        ArgumentNullException.ThrowIfNull(report);
        using var book = new XLWorkbook();
        var sheet = book.Worksheets.Add(SheetName(report.Title));
        var row = 1;
        sheet.Cell(row, 1).Value = hotelName;
        sheet.Cell(row++, 1).Style.Font.SetBold().Font.SetFontSize(14);
        sheet.Cell(row, 1).Value = report.Title;
        sheet.Cell(row++, 1).Style.Font.SetBold().Font.SetFontSize(12);
        sheet.Cell(row++, 1).Value = $"Период: {Period(report)}";
        foreach (var filter in report.Filters)
        {
            sheet.Cell(row++, 1).Value = filter;
        }

        if (report.Note is not null)
        {
            sheet.Cell(row, 1).Value = report.Note;
            sheet.Cell(row++, 1).Style.Font.SetItalic();
        }

        row++;
        foreach (var item in report.Summary)
        {
            sheet.Cell(row, 1).Value = item.Label;
            Put(sheet.Cell(row, 2), item.Value, item.Type);
            sheet.Cell(row, 2).Style.Font.SetBold();
            row++;
        }

        row++;
        var header = row;
        for (var c = 0; c < report.Columns.Count; c++)
        {
            var cell = sheet.Cell(header, c + 1);
            cell.Value = report.Columns[c].Title;
            cell.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#F3ECE2")).Alignment.SetWrapText();
        }

        foreach (var values in report.Rows)
        {
            row++;
            for (var c = 0; c < report.Columns.Count; c++)
            {
                Put(sheet.Cell(row, c + 1), values[c], report.Columns[c].Type);
            }
        }

        if (report.Totals is { } totals)
        {
            row++;
            for (var c = 0; c < report.Columns.Count; c++)
            {
                Put(sheet.Cell(row, c + 1), totals[c], report.Columns[c].Type);
                sheet.Cell(row, c + 1).Style.Font.SetBold().Border.SetTopBorder(XLBorderStyleValues.Thin);
            }
        }

        sheet.SheetView.FreezeRows(header);
        FitColumns(sheet, Math.Max(2, report.Columns.Count), 1, row);
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Ширина колонок по длине текста (8–60 символов). Не используем <c>AdjustToContents</c>:
    /// ClosedXML для него обходит системные шрифты и падает, если папка шрифтов недоступна (или их нет в контейнере).
    /// </summary>
    private static void FitColumns(IXLWorksheet sheet, int columns, int fromRow, int toRow)
    {
        for (var c = 1; c <= columns; c++)
        {
            var longest = 0;
            for (var r = fromRow; r <= toRow; r++)
            {
                var text = sheet.Cell(r, c).GetFormattedString();
                longest = Math.Max(longest, text.Length);
            }

            sheet.Column(c).Width = Math.Clamp(longest + 2, 8, 60);
        }
    }

    /// <summary>Значение ячейки с форматом: деньги — с копейками, проценты — Excel-процент, даты — датой.</summary>
    private static void Put(IXLCell cell, object? value, string type)
    {
        switch (value)
        {
            case null:
                return;
            case decimal d:
                cell.Value = d;
                cell.Style.NumberFormat.Format = type switch
                {
                    "percent" => "0.0%",
                    "money" => "#,##0.00",
                    "int" => "0",
                    _ => "#,##0.##",
                };
                return;
            case int i:
                cell.Value = i;
                return;
            case string s when type == "date" && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date):
                cell.Value = date.ToDateTime(TimeOnly.MinValue);
                cell.Style.DateFormat.Format = "dd.MM.yyyy";
                return;
            case string s when type == "datetime" && DateTime.TryParseExact(s, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at):
                cell.Value = at;
                cell.Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
                return;
            default:
                cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture);
                return;
        }
    }

    private static string SheetName(string title) => title.Length > 31 ? title[..31] : title;

    /// <summary>«22.11.2026», «22.11.2026 06:00–22:00», «20.11.2026 06:00 — 22.11.2026 22:00».</summary>
    internal static string Period(ReportTable r)
    {
        var from = r.From.ToString("dd.MM.yyyy", Ru);
        var to = r.To.ToString("dd.MM.yyyy", Ru);
        if (r.FromHour is null && r.ToHour is null)
        {
            return r.From == r.To ? from : $"{from} — {to}";
        }

        var fh = $"{r.FromHour ?? 0:00}:00";
        var th = $"{r.ToHour ?? 24:00}:00";
        return r.From == r.To ? $"{from} {fh}–{th}" : $"{from} {fh} — {to} {th}";
    }

    /// <summary>Текст ячейки PDF: деньги «12 345,00», проценты «45,2 %», даты по-русски.</summary>
    internal static string Text(object? value, string type) => value switch
    {
        null => string.Empty,
        decimal d when type == "percent" => (d * 100).ToString("0.0", Ru) + " %",
        decimal d when type == "money" => d.ToString("N2", Ru),
        decimal d when type == "int" => d.ToString("0", Ru),
        decimal d => d.ToString("#,##0.##", Ru),
        int i => i.ToString(Ru),
        string s when type == "date" && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) => date.ToString("dd.MM.yyyy", Ru),
        string s when type == "datetime" && DateTime.TryParseExact(s, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) => at.ToString("dd.MM.yyyy HH:mm", Ru),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    public byte[] ToPdf(ReportTable report, string hotelName)
    {
        ArgumentNullException.ThrowIfNull(report);
        EmbeddedFonts.Register();
        var doc = new Document();
        doc.Info.Title = report.Title;
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = EmbeddedFonts.Family;
        normal.Font.Size = 8;

        var section = doc.AddSection();
        section.PageSetup = doc.DefaultPageSetup.Clone();
        // Широкие таблицы — альбомная A4. Размеры задаём явно: в MigraDoc 6 одного Orientation недостаточно.
        var landscape = report.Columns.Count > 6;
        section.PageSetup.Orientation = Orientation.Portrait;
        section.PageSetup.PageWidth = Unit.FromCentimeter(landscape ? 29.7 : 21);
        section.PageSetup.PageHeight = Unit.FromCentimeter(landscape ? 21 : 29.7);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(1.2);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromCentimeter(1.2);
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Alignment = ParagraphAlignment.Right;
        footer.AddText($"{hotelName} · {report.Title} · стр. ");
        footer.AddPageField();
        footer.AddText(" из ");
        footer.AddNumPagesField();

        var title = section.AddParagraph(hotelName);
        title.Format.Font.Size = 10;
        var heading = section.AddParagraph(report.Title);
        heading.Format.Font.Size = 16;
        heading.Format.Font.Bold = true;
        heading.Format.SpaceAfter = Unit.FromPoint(2);
        section.AddParagraph($"Период: {Period(report)}");
        foreach (var filter in report.Filters)
        {
            section.AddParagraph(filter);
        }

        if (report.Note is not null)
        {
            var note = section.AddParagraph(report.Note);
            note.Format.Font.Italic = true;
        }

        if (report.Summary.Count > 0)
        {
            section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(4);
            var summary = section.AddTable();
            summary.Borders.Width = 0;
            summary.AddColumn(Unit.FromCentimeter(6));
            summary.AddColumn(Unit.FromCentimeter(4)).Format.Alignment = ParagraphAlignment.Right;
            foreach (var item in report.Summary)
            {
                var r = summary.AddRow();
                r.Cells[0].AddParagraph(item.Label);
                var value = r.Cells[1].AddParagraph(item.Value is null ? "—" : Text(item.Value, item.Type));
                value.Format.Font.Bold = true;
            }
        }

        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);
        var width = landscape ? 27.3 : 18.6;
        var table = section.AddTable();
        table.Borders.Color = Color.FromRgb(0xE6, 0xDC, 0xCF);
        table.Borders.Width = 0.5;
        table.Format.Font.Size = report.Columns.Count > 9 ? 7 : 8;
        foreach (var col in report.Columns)
        {
            var column = table.AddColumn(Unit.FromCentimeter(width / report.Columns.Count));
            column.Format.Alignment = col.Type is "money" or "int" or "number" or "percent" ? ParagraphAlignment.Right : ParagraphAlignment.Left;
        }

        var head = table.AddRow();
        head.HeadingFormat = true;
        head.Shading.Color = Color.FromRgb(0xF3, 0xEC, 0xE2);
        head.Format.Font.Bold = true;
        for (var c = 0; c < report.Columns.Count; c++)
        {
            head.Cells[c].AddParagraph(report.Columns[c].Title);
        }

        foreach (var values in report.Rows)
        {
            var r = table.AddRow();
            for (var c = 0; c < report.Columns.Count; c++)
            {
                r.Cells[c].AddParagraph(Text(values[c], report.Columns[c].Type));
            }
        }

        if (report.Totals is { } totals)
        {
            var r = table.AddRow();
            r.Format.Font.Bold = true;
            for (var c = 0; c < report.Columns.Count; c++)
            {
                r.Cells[c].AddParagraph(Text(totals[c], report.Columns[c].Type));
            }
        }

        if (report.Rows.Count == 0)
        {
            section.AddParagraph("За период данных нет.");
        }

        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }
}

/// <summary>Шрифт PT Sans (OFL, кириллица) из ресурсов сборки — на сервере не нужны системные шрифты.</summary>
internal sealed class EmbeddedFonts : IFontResolver
{
    public const string Family = "PT Sans";

    private static readonly Lock Gate = new();

    public static void Register()
    {
        lock (Gate)
        {
            if (GlobalFontSettings.FontResolver is not EmbeddedFonts)
            {
                GlobalFontSettings.FontResolver = new EmbeddedFonts();
            }
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold ? "PTSans-Bold" : "PTSans-Regular", false, italic);

    public byte[]? GetFont(string faceName)
    {
        var file = faceName == "PTSans-Bold" ? "PT_Sans-Web-Bold.ttf" : "PT_Sans-Web-Regular.ttf";
        var assembly = typeof(EmbeddedFonts).Assembly;
        var name = assembly.GetManifestResourceNames().First(n => n.EndsWith(file, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
