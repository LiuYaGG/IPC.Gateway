using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using IPC.EdgeGateway;

namespace IPC.Gateway.WebHost;

internal static class HistoryPageWorkbook
{
    private const string Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string OfficeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string Types = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const int CellLimit = 32767;

    public static void Write(Stream destination, IReadOnlyList<HistoryValueRow> rows, bool english, CancellationToken ct, TimeZoneInfo? timeZone = null)
    {
        timeZone ??= TimeZoneInfo.Local;
        string[] headers = english
            ? [$"Timestamp ({timeZone.Id})", "Tag path", "Data type", "Value", "Raw value", "Unit", "Quality", "Error", "Processing type", "Processing details"]
            : [$"采样时间 ({timeZone.Id})", "点位路径", "数据类型", "工程值", "原始值", "单位", "质量", "错误信息", "处理类型", "处理详情"];
        WriteTable(destination, headers, [31, 58, 16, 30, 30, 12, 18, 44, 20, 54],
            rows.Select(row => new string?[] { WorkbookTimeZone.Format(row.Timestamp, timeZone),
                row.Path, row.DataType, row.ValueText, row.RawValueText, row.Unit,
                LocalizeQuality(row.Quality, english), row.ErrorMessage, row.EdgeProcessingType, row.Details?.GetRawText() }),
            english ? "Tag history" : "点位历史", english, ct);
    }

    internal static void WriteTable(Stream destination, string[] headers, int[] widths, IEnumerable<string?[]> rows,
        string sheetName, bool english, CancellationToken ct)
    {
        string longSheet = english ? "Long values" : "超长字段";
        var overflow = new List<(int Row, string Column, string Value)>();
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        using (var sheet = OpenXml(archive, "xl/worksheets/sheet1.xml"))
        {
            BeginSheet(sheet, widths);
            WriteRow(sheet, 1, headers);
            int sheetRow = 1;
            foreach (string?[] values in rows)
            {
                ct.ThrowIfCancellationRequested();
                if (++sheetRow > 1048576) throw new InvalidDataException("The export exceeds the Excel worksheet row limit.");
                for (int j = 0; j < values.Length; j++)
                {
                    if (values[j] is not { Length: > CellLimit } value) continue;
                    overflow.Add((sheetRow, headers[j], value));
                    values[j] = english ? "See Long values sheet" : "见「超长字段」工作表";
                }
                WriteRow(sheet, sheetRow, values);
            }
            sheet.WriteEndElement();
            sheet.WriteEndElement();
        }
        if (overflow.Count > 0)
        {
            using var sheet = OpenXml(archive, "xl/worksheets/sheet2.xml");
            BeginSheet(sheet, [14, 22, 12, 90]);
            WriteRow(sheet, 1, english ? ["Source row", "Field", "Part", "Text"] : ["原表行号", "字段", "分段序号", "完整内容分段"]);
            int sheetRow = 1;
            foreach (var field in overflow)
            {
                int offset = 0;
                int part = 0;
                while (offset < field.Value.Length)
                {
                    ct.ThrowIfCancellationRequested();
                    int length = Math.Min(CellLimit, field.Value.Length - offset);
                    if (char.IsHighSurrogate(field.Value[offset + length - 1]) && offset + length < field.Value.Length) length--;
                    if (++sheetRow > 1048576) throw new InvalidDataException("The history page exceeds the Excel worksheet row limit.");
                    WriteRow(sheet, sheetRow, [field.Row.ToString(CultureInfo.InvariantCulture), field.Column,
                        (++part).ToString(CultureInfo.InvariantCulture), field.Value.Substring(offset, length)]);
                    offset += length;
                }
            }
            sheet.WriteEndElement();
            sheet.WriteEndElement();
        }
        string[] names = overflow.Count == 0 ? [sheetName] : [sheetName, longSheet];
        using (var xml = OpenXml(archive, "xl/workbook.xml"))
        {
            xml.WriteStartElement("workbook", Spreadsheet);
            xml.WriteStartElement("sheets", Spreadsheet);
            for (int i = 0; i < names.Length; i++)
            {
                xml.WriteStartElement("sheet", Spreadsheet);
                xml.WriteAttributeString("name", names[i]);
                xml.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("r", "id", OfficeRel, $"rId{i + 1}");
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
            xml.WriteEndElement();
        }
        using (var xml = OpenXml(archive, "_rels/.rels"))
        {
            xml.WriteStartElement("Relationships", PackageRel);
            Relationship(xml, 1, "officeDocument", "xl/workbook.xml");
            xml.WriteEndElement();
        }
        using (var xml = OpenXml(archive, "xl/_rels/workbook.xml.rels"))
        {
            xml.WriteStartElement("Relationships", PackageRel);
            for (int i = 1; i <= names.Length; i++) Relationship(xml, i, "worksheet", $"worksheets/sheet{i}.xml");
            xml.WriteEndElement();
        }
        using (var xml = OpenXml(archive, "[Content_Types].xml"))
        {
            xml.WriteStartElement("Types", Types);
            xml.WriteStartElement("Default", Types);
            xml.WriteAttributeString("Extension", "rels");
            xml.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml");
            xml.WriteEndElement();
            ContentType(xml, "/xl/workbook.xml", "sheet.main");
            for (int i = 1; i <= names.Length; i++) ContentType(xml, $"/xl/worksheets/sheet{i}.xml", "worksheet");
            xml.WriteEndElement();
        }
    }

    private static XmlWriter OpenXml(ZipArchive archive, string path) =>
        XmlWriter.Create(archive.CreateEntry(path, CompressionLevel.Fastest).Open(), new XmlWriterSettings
        { Encoding = new UTF8Encoding(false), CloseOutput = true, NewLineHandling = NewLineHandling.Entitize });

    private static void ContentType(XmlWriter xml, string path, string type)
    {
        xml.WriteStartElement("Override", Types);
        xml.WriteAttributeString("PartName", path);
        xml.WriteAttributeString("ContentType", $"application/vnd.openxmlformats-officedocument.spreadsheetml.{type}+xml");
        xml.WriteEndElement();
    }

    private static void Relationship(XmlWriter xml, int id, string type, string target)
    {
        xml.WriteStartElement("Relationship", PackageRel);
        xml.WriteAttributeString("Id", $"rId{id}");
        xml.WriteAttributeString("Type", $"{OfficeRel}/{type}");
        xml.WriteAttributeString("Target", target);
        xml.WriteEndElement();
    }

    private static void BeginSheet(XmlWriter sheet, int[] widths)
    {
        sheet.WriteStartElement("worksheet", Spreadsheet);
        sheet.WriteStartElement("cols", Spreadsheet);
        for (int i = 0; i < widths.Length; i++)
        {
            sheet.WriteStartElement("col", Spreadsheet);
            string column = (i + 1).ToString(CultureInfo.InvariantCulture);
            sheet.WriteAttributeString("min", column);
            sheet.WriteAttributeString("max", column);
            sheet.WriteAttributeString("width", widths[i].ToString(CultureInfo.InvariantCulture));
            sheet.WriteAttributeString("customWidth", "1");
            sheet.WriteEndElement();
        }
        sheet.WriteEndElement();
        sheet.WriteStartElement("sheetData", Spreadsheet);
    }

    private static void WriteRow(XmlWriter sheet, int index, string?[] values)
    {
        sheet.WriteStartElement("row", Spreadsheet);
        sheet.WriteAttributeString("r", index.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] is not { } value) continue;
            sheet.WriteStartElement("c", Spreadsheet);
            sheet.WriteAttributeString("r", $"{(char)('A' + i)}{index}");
            // Inline strings preserve Int64 precision, leading zeroes and literal formulas.
            sheet.WriteAttributeString("t", "inlineStr");
            sheet.WriteStartElement("is", Spreadsheet);
            sheet.WriteStartElement("t", Spreadsheet);
            sheet.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
            WriteText(sheet, value);
            sheet.WriteEndElement();
            sheet.WriteEndElement();
            sheet.WriteEndElement();
        }
        sheet.WriteEndElement();
    }

    private static void WriteText(XmlWriter sheet, string value)
    {
        // SpreadsheetML escapes XML control characters, and escapes literal _xHHHH_ sequences.
        int start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            if (char.IsHighSurrogate(ch) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) { i++; continue; }
            bool looksEscaped = ch == '_' && i + 6 < value.Length && value[i + 1] == 'x' && value[i + 6] == '_' &&
                char.IsAsciiHexDigit(value[i + 2]) && char.IsAsciiHexDigit(value[i + 3]) &&
                char.IsAsciiHexDigit(value[i + 4]) && char.IsAsciiHexDigit(value[i + 5]);
            if (XmlConvert.IsXmlChar(ch) && !looksEscaped) continue;
            if (i > start) sheet.WriteString(value.Substring(start, i - start));
            sheet.WriteString($"_x{(int)ch:X4}_");
            start = i + 1;
        }
        if (start < value.Length) sheet.WriteString(start == 0 ? value : value[start..]);
    }

    private static string? LocalizeQuality(string? quality, bool english) =>
        english ? quality : quality switch
        {
            "Good" => "正常", "Unknown" => "无数据", "Bad" => "异常", "ReadError" => "读取失败",
            "NotConnected" => "未连接", "Disabled" => "停用", "OutOfRange" => "越界", "Filtered" => "已过滤",
            "Spike" => "毛刺", "Uncertain" => "不确定", "NotFound" => "未找到", "AccessDenied" => "拒绝访问", _ => quality
        };
}
