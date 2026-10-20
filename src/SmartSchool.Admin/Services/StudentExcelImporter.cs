using System.IO;
using ClosedXML.Excel;
using SmartSchool.Admin.Models;

namespace SmartSchool.Admin.Services;

public sealed record ImportResult(int Total, int Imported, int Failed, List<string> Errors);

/// <summary>
/// Excel (.xlsx) ya CSV se student bulk-import:
/// row-1 headers; synonyms samajhta hai (Student Name / Name …), class ke naam se match,
/// phir normal upsert API se ek ek row daalti hai — kuch bhi fail ho to list mein daal deti hai.
/// </summary>
public static class StudentExcelImporter
{
    // header → property mapping with Urdu/English/typo-tolerant synonyms
    private static readonly (string Key, string[] Aliases)[] MapSpec =
    [
        ("name", ["student name", "student", "name", "studentname", "bachcha", "naam"]),
        ("father", ["father name", "fathername", "father", "walid", "guardian"]),
        ("phone", ["phone", "phone number", "father phone", "phone no", "mobile", "cell", "contact"]),
        ("roll", ["roll", "roll no", "rollno", "roll number", "class roll"]),
        ("class", ["class", "grade", "class name", "class section", "darja"]),
        ("address", ["address", "home address", "pata", "addr"]),
        ("bus", ["bus", "bus #", "bus no", "bus number", "busno"]),
        ("dob", ["dob", "date of birth", "birth", "birthday"]),
        ("mother", ["mother phone", "mother", "mother no"]),
    ];

    public static async Task<ImportResult> ImportAsync(string filePath, IProgress<string> progress, CancellationToken ct = default)
    {
        var rows = filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            ? ReadCsv(filePath)
            : ReadXlsx(filePath);

        var errors = new List<string>();
        if (rows.Count == 0) return new ImportResult(0, 0, 0, ["File mein headers ke ilawa koi row nahi mili."]);

        if (!rows[0].ContainsKey("name"))
            return new ImportResult(rows.Count, 0, 0, [
                "Header samajh nahi aya — first row mein 'Student Name' column chahiye. " +
                "Sample names: Student Name, Father Name, Phone, Class, Roll No, Home Address, Bus #"
            ]);

        // Classes + buses cache (match karte ke liye)
        var clsR = await ApiClient.Instance.ClassesAsync();
        var busR = await ApiClient.Instance.BusesAsync();
        var classes = clsR.Data ?? [];
        var buses = busR.Data ?? [];

        int ok = 0, failed = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var r = rows[i];
            progress.Report($"Row {i + 1}/{rows.Count}: {r.GetValueOrDefault("name")}");
            try
            {
                var name = (r.GetValueOrDefault("name") ?? "").Trim();
                if (name.Length < 3) { failed++; errors.Add($"Row {i + 1}: naam 3 haroof se chhota — '{name}'"); continue; }

                var clsName = (r.GetValueOrDefault("class") ?? "").Trim();
                var cls = FindClass(classes, clsName);
                if (cls is null) { failed++; errors.Add($"Row {i + 1}: class '{clsName}' system mein nahi — pehle Classes mein banaein (ya naam theek se likhein, e.g. '1-A')"); continue; }

                int? busId = null;
                var busText = (r.GetValueOrDefault("bus") ?? "").Trim();
                if (busText.Length > 0)
                {
                    var norm = busText.TrimStart('B', 'b', '-', '#').Trim();
                    var bus = buses.FirstOrDefault(b =>
                        b.BusNumber.Equals(norm, StringComparison.OrdinalIgnoreCase) ||
                        b.BusNumber.Equals(busText, StringComparison.OrdinalIgnoreCase));
                    if (bus is null) errors.Add($"Row {i + 1}: bus '{busText}' nahi mili — bina bus ke import kiya");
                    else busId = bus.Id;
                }

                var body = new StudentUpsertRequest
                {
                    FullName = name,
                    FatherName = (r.GetValueOrDefault("father") ?? "").Trim(),
                    FatherPhone = NormalizePhone(r.GetValueOrDefault("phone")),
                    MotherPhone = string.IsNullOrWhiteSpace(r.GetValueOrDefault("mother"))
                        ? null : NormalizePhone(r.GetValueOrDefault("mother")),
                    RollNo = (r.GetValueOrDefault("roll") ?? "").Trim(),
                    ClassId = cls.Id,
                    Address = (r.GetValueOrDefault("address") ?? "").Trim(),
                    DateOfBirth = ParseDob(r.GetValueOrDefault("dob")),
                    BusId = busId,
                    IsActive = null
                };

                var resp = await ApiClient.Instance.PostAsync<StudentDetailResponse>("api/students", body);
                if (resp.Ok) ok++;
                else { failed++; errors.Add($"Row {i + 1}: {resp.Error}"); }
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"Row {i + 1}: {ex.Message}");
            }
        }
        return new ImportResult(rows.Count, ok, failed, errors);
    }

    private static ClassListItem? FindClass(List<ClassListItem> classes, string text)
    {
        if (text.Length == 0) return null;
        var t = text.Replace(" ", "").ToLowerInvariant();
        return classes.FirstOrDefault(c =>
            c.DisplayName.Replace(" ", "").Equals(t, StringComparison.OrdinalIgnoreCase) ||
            c.Name.Replace(" ", "").Equals(t, StringComparison.OrdinalIgnoreCase) ||
            $"{c.Name}-{c.Section}".Replace(" ", "").Equals(t, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length == 10 && !digits.StartsWith("0") ? "0" + digits : digits;
    }

    private static string ParseDob(string? raw)
    {
        if (!string.IsNullOrWhiteSpace(raw) &&
            DateTime.TryParse(raw, out var d) && d is { Year: > 2000 })
            return d.ToString("yyyy-MM-dd");
        return "2016-01-01"; // na mila to safe default (baad mein edit ho sakta hai)
    }

    private static List<Dictionary<string, string>> ReadXlsx(string path)
    {
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();
        var headerCells = ws.Row(1).CellsUsed().ToList();
        var colByKey = new Dictionary<string, int>();
        foreach (var (key, aliases) in MapSpec)
        {
            var cell = headerCells.FirstOrDefault(c =>
                aliases.Any(a => NormalizeHeader(c.GetString()) == a));
            if (cell is not null) colByKey[key] = cell.Address.ColumnNumber;
        }
        var rows = new List<Dictionary<string, string>>();
        for (var r = 2; r <= ws.LastRowUsed()!.RowNumber(); r++)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool anyVal = false;
            foreach (var (key, col) in colByKey)
            {
                var v = ws.Cell(r, col).GetString().Trim();
                if (v.Length > 0) anyVal = true;
                dict[key] = v;
            }
            if (anyVal) rows.Add(dict);
        }
        return rows;
    }

    private static List<Dictionary<string, string>> ReadCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 1) return [];
        var headers = SplitCsv(lines[0]);
        var colByKey = new Dictionary<string, int>();
        foreach (var (key, aliases) in MapSpec)
        {
            var idx = Array.FindIndex(headers, h => aliases.Any(a => NormalizeHeader(h) == a));
            if (idx >= 0) colByKey[key] = idx;
        }
        var rows = new List<Dictionary<string, string>>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cells = SplitCsv(lines[i]);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, col) in colByKey)
                dict[key] = col < cells.Length ? cells[col].Trim() : "";
            rows.Add(dict);
        }
        return rows;
    }

    private static string NormalizeHeader(string h)
        => h.Trim().ToLowerInvariant().Replace("_", " ").Trim();

    private static string[] SplitCsv(string line)
    {
        var cells = new List<string>();
        bool inQuotes = false; var cur = new System.Text.StringBuilder();
        foreach (var ch in line)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (ch == ',' && !inQuotes) { cells.Add(cur.ToString()); cur.Clear(); continue; }
            cur.Append(ch);
        }
        cells.Add(cur.ToString());
        return cells.ToArray();
    }
}
