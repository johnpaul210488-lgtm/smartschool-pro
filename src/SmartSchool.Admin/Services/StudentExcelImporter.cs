using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using SmartSchool.Admin.Models;

namespace SmartSchool.Admin.Services;

public sealed record ImportResult
{
    public int Total { get; init; }
    public int Imported { get; init; }
    public int Failed { get; init; }
    public int DupesSkipped { get; init; }
    public int BusesCreated { get; init; }
    public int DriversCreated { get; init; }
    public int ClassesCreated { get; init; }
    public List<string> Errors { get; init; } = new();
    public string? CredsFilePath { get; init; }
    public static ImportResult Fail(params string[] errs) => new() { Errors = [.. errs] };
}

/// <summary>
/// Student bulk-import. Do formats samajhta hai:
///   1) FLAT sheet: row 1 headers (English/Urdu synonyms: Student Name, Father Name, Phone, Class, Roll No…)
///   2) BUS WORKBOOK (school ka transport file): har bus ka alag sheet, usmein
///      "BUS NO: n &lt;DRIVER&gt; Mob: ..." meta + SL No/Name/Year/O/N/B/G/Bus No/Location/Mob F/Mob M columns.
///      FS2 (12 PM) return sheets ki duplicate entries skip hoti hain.
/// Bus workbook ke liye missing buses (drivers ke saath) aur classes (Year 1, FS 2…) khud ba-khud ban jate hain.
/// </summary>
public static partial class StudentExcelImporter
{
    // ============ FLAT-sheet header synonyms ============
    private static readonly (string Key, string[] Aliases)[] MapSpec =
    [
        ("name", ["student name", "student", "name", "studentname", "naam"]),
        ("father", ["father name", "fathername", "father", "walid", "guardian"]),
        ("phone", ["phone", "phone number", "father phone", "phone no", "mobile", "cell", "contact"]),
        ("roll", ["roll", "roll no", "rollno", "roll number", "class roll"]),
        ("class", ["class", "grade", "class name", "class section", "year"]),
        ("address", ["address", "home address", "pata", "addr", "location"]),
        ("bus", ["bus", "bus #", "bus no", "bus number", "busno"]),
        ("dob", ["dob", "date of birth", "birth", "birthday"]),
        ("mother", ["mother phone", "mother", "mother no"]),
        ("gender", ["gender", "sex", "b/g", "boy/girl"]),
        ("onflag", ["o/n", "old/new", "status"]),
    ];

    public static async Task<ImportResult> ImportAsync(string filePath, IProgress<string> progress, CancellationToken ct = default)
    {
        var api = ApiClient.Instance;

        if (filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return await ImportFlatAsync(ReadCsv(filePath), api, progress, ct, rowOffset: 1);

        using var wb = new XLWorkbook(filePath);
        if (LooksLikeBusWorkbook(wb))
            return await ImportBusWorkbookAsync(wb, filePath, api, progress, ct);

        var rows = ReadFlatXlsx(wb);
        return await ImportFlatAsync(rows, api, progress, ct, rowOffset: 1);
    }

    // ========================================================================
    //  BUS WORKBOOK PATH
    // ========================================================================

    private sealed record SheetRow(int SheetRowNo, string SlNo, string Name, string Year,
        string OnFlag, string Bg, string Bus1, string Bus2, string Loc, string MobF, string MobM, string Flag);

    private sealed class SheetPlan
    {
        public string Title { get; init; } = "";
        public int BusNo { get; init; }
        public bool IsNoonReturn { get; init; }
        public string DriverName { get; init; } = "";
        public string DriverPhoneRaw { get; init; } = "";
        public string NannyNote { get; init; } = "";
        public List<SheetRow> Rows { get; } = new();
    }

    private static bool LooksLikeBusWorkbook(XLWorkbook wb)
        => wb.Worksheets.Any(ws => ws.Rows(1, 10).Any(r => NormA(r.Cell(1).GetString()) == "sl no"));

    private static string NormA(string s) => s.Trim().ToLowerInvariant();

    private static List<SheetPlan> ParseBusWorkbook(XLWorkbook wb, List<string> notes)
    {
        var plans = new List<SheetPlan>();
        foreach (var ws in wb.Worksheets)
        {
            // ---- meta line: "BUS NO: 1  KHAN FARAZ  Mob: 050 8089038" ----
            string meta = "", nanny = "";
            foreach (var r in ws.Rows(1, 6))
            {
                foreach (var c in r.CellsUsed())
                {
                    var v = c.GetString().Trim();
                    if (v.Contains("BUS NO", StringComparison.OrdinalIgnoreCase)) meta = v;
                    else if (v.StartsWith("Nani", StringComparison.OrdinalIgnoreCase)) nanny = v;
                }
            }
            if (meta.Length == 0) continue;

            var m = BusMetaRegex().Match(meta);
            var busNo = m.Success && int.TryParse(m.Groups[1].Value, out var bn) ? bn : 0;
            var driverName = m.Success ? TitleCase(SingleLetterSuffix().Replace(m.Groups[2].Value, "").Trim()) : "";
            var driverPhone = m.Success ? m.Groups[3].Value.Trim() : "";
            var isNoon = meta.Contains("AT 12 PM", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(ws.Name, @"\bFS2\b|NOON|12 PM", RegexOptions.IgnoreCase);
            if (driverName.Length == 0)
            {
                // fallback: everything between bus no and Mob:
                var m2 = MetaLooseRegex().Match(meta);
                if (m2.Success) driverName = TitleCase(SingleLetterSuffix().Replace(m2.Groups[1].Value, "").Trim());
            }

            // ---- header row ----
            IXLRow? header = null;
            foreach (var r in ws.Rows(1, 10))
                if (NormA(r.Cell(1).GetString()) == "sl no") { header = r; break; }
            if (header is null) { notes.Add($"Sheet '{ws.Name}': header row (SL No) nahi mili — sheet skip."); continue; }

            var plan = new SheetPlan
            {
                Title = ws.Name, BusNo = busNo, IsNoonReturn = isNoon,
                DriverName = driverName, DriverPhoneRaw = driverPhone, NannyNote = nanny
            };
            for (var i = header.RowNumber() + 1; i <= ws.LastRowUsed()!.RowNumber(); i++)
            {
                var row = ws.Row(i);
                var name = row.Cell(2).GetString().Trim();
                if (name.Length == 0) continue;
                plan.Rows.Add(new SheetRow(i,
                    SlNo: row.Cell(1).GetString().Trim(),
                    Name: name,
                    Year: row.Cell(3).GetString().Trim().ToUpperInvariant(),
                    OnFlag: row.Cell(4).GetString().Trim().ToUpperInvariant(),
                    Bg: row.Cell(5).GetString().Trim().ToUpperInvariant(),
                    Bus1: row.Cell(6).GetString().Trim(),
                    Bus2: row.Cell(7).GetString().Trim(),
                    Loc: row.Cell(8).GetString().Trim(),
                    MobF: row.Cell(9).GetString().Trim(),
                    MobM: row.Cell(10).GetString().Trim(),
                    Flag: row.Cell(11).GetString().Trim().ToUpperInvariant()));
            }
            plans.Add(plan);
        }
        return plans;
    }

    private static async Task<ImportResult> ImportBusWorkbookAsync(
        XLWorkbook wb, string filePath, ApiClient api, IProgress<string> progress, CancellationToken ct)
    {
        var errors = new List<string>();
        var notes = new List<string>();
        var plans = ParseBusWorkbook(wb, errors);
        // main sheets pehle, phir FS2/noon-return sheets (dupes wahi honge)
        plans = [.. plans.OrderBy(p => p.IsNoonReturn)];
        if (plans.Count == 0)
            return new ImportResult { Errors = ["Workbook samajh nahi aya — kisi bhi sheet mein 'BUS NO' meta ya 'SL No' header nahi mila."] };

        var totalRows = plans.Sum(p => p.Rows.Count);
        progress.Report($"📋 {plans.Count} sheets mili: {totalRows} rows. System check ho raha hai…");

        // ---- 1) Buses: ensure all numbers exist ----
        var busR = await api.BusesAsync();
        var buses = busR.Data ?? new List<BusListItem>();
        var neededNos = plans.Where(p => p.BusNo > 0).Select(p => p.BusNo).Distinct()
            .Concat(plans.SelectMany(p => p.Rows).SelectMany(r => new[] { r.Bus1, r.Bus2 })
                .Where(s => s.Length > 0).Select(s => int.TryParse(s, out var n) ? n : 0))
            .Where(n => n > 0).Distinct().OrderBy(n => n).ToList();

        int busesCreated = 0;
        foreach (var n in neededNos)
        {
            if (buses.Any(b => b.BusNumber.Equals(n.ToString(), StringComparison.OrdinalIgnoreCase))) continue;
            ct.ThrowIfCancellationRequested();
            progress.Report($"🚌 Bus #{n} bana raha hun…");
            var r = await api.PostAsync<BusListItem>("api/buses", new BusUpsertRequest
            {
                BusNumber = n.ToString(),
                Route = $"Imported route #{n} (Buses page par edit karein)",
                VehicleNo = $"TBD-{n}",
                Capacity = 40
            });
            if (r.Ok && r.Data is not null) { buses.Add(r.Data); busesCreated++; }
            else errors.Add($"Bus #{n} create nahi hui: {r.Error}");
        }

        // ---- 2) Drivers: create (with app logins) + assign to their bus ----
        var drvR = await api.DriversAsync();
        var drivers = drvR.Data ?? new List<DriverListItem>();
        var credLines = new List<string>();
        int driversCreated = 0;

        foreach (var p in plans.Where(pp => pp.BusNo > 0).GroupBy(pp => pp.BusNo).Select(g => g.First()))
        {
            ct.ThrowIfCancellationRequested();
            var bus = buses.FirstOrDefault(b => b.BusNumber == p.BusNo.ToString());
            if (bus is null) continue;
            if (p.DriverName.Length < 3)
            {
                notes.Add($"Bus #{p.BusNo}: driver ka naam sheet se nahi mila — bus bina driver ke hai.");
                continue;
            }

            var driver = drivers.FirstOrDefault(d =>
                d.FullName.Equals(p.DriverName, StringComparison.OrdinalIgnoreCase));
            if (driver is null)
            {
                driver = drivers.FirstOrDefault(d =>
                    NormalizePhone(p.DriverPhoneRaw).Length > 5 &&
                    NormalizePhone(d.Phone) == NormalizePhone(p.DriverPhoneRaw));
            }
            if (driver is null)
            {
                progress.Report($"👨‍✈️ Driver {p.DriverName} (Bus #{p.BusNo}) bana raha hun…");
                var username = UniqueUsername(p.DriverName, drivers);
                var password = GeneratePassword(p.DriverName, p.BusNo);
                var license = $"IMP-{p.BusNo}-{Guid.NewGuid():N}"[..20];
                var r = await api.PostAsync<DriverListItem>("api/drivers", new DriverCreateRequest
                {
                    FullName = p.DriverName,
                    Phone = NormalizePhone(p.DriverPhoneRaw),
                    LicenseNo = license,
                    Username = username,
                    Password = password
                });
                if (r.Ok && r.Data is not null)
                {
                    driver = r.Data; drivers.Add(driver); driversCreated++;
                    credLines.Add($"Bus #{p.BusNo}  {p.DriverName}   username: {username}   password: {password}");
                }
                else { errors.Add($"Driver '{p.DriverName}' (Bus #{p.BusNo}) create nahi hua: {r.Error}"); continue; }
            }
            else if (bus.DriverId is null)
            {
                // driver maujood tha — usi ko assign
            }
            else continue; // bus already has a driver; sheet wala overwrite nahi

            if (bus.DriverId != driver.Id)
            {
                var a = await api.PutAsync<object>($"api/buses/{bus.Id}/assign-driver", new { driverId = driver.Id });
                if (a.Ok) { bus.DriverId = driver.Id; bus.DriverName = driver.FullName; }
                else errors.Add($"Bus #{p.BusNo} ko driver assign nahi hua: {a.Error}");
            }
            if (p.NannyNote.Length > 0)
                notes.Add($"Bus #{p.BusNo} nanny info: {p.NannyNote}");
        }

        // ---- 3) Classes: ensure Year/FS labels exist ----
        var clsR = await api.ClassesAsync();
        var classes = clsR.Data ?? new List<ClassListItem>();
        var yearsNeeded = plans.SelectMany(p => p.Rows).Select(r => r.Year).Where(y => y.Length > 0)
            .Distinct().ToList();
        int classesCreated = 0;
        var classByYear = new Dictionary<string, ClassListItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var year in yearsNeeded)
        {
            ct.ThrowIfCancellationRequested();
            var found = FindClassByYear(classes, year);
            if (found is null)
            {
                var (name, section) = YearToCreate(year);
                if (name.Length == 0) continue;
                progress.Report($"🏫 Class '{name}-{section}' bana raha hun…");
                var r = await api.PostAsync<ClassListItem>("api/classes", new
                { name, section, teacherId = (int?)null });
                if (r.Ok && r.Data is not null) { classes.Add(r.Data); found = r.Data; classesCreated++; }
                else if (r.Error.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                {
                    // concurrent or fuzzy-miss — refresh and retry match
                    var again = await api.ClassesAsync();
                    if (again.Data is not null) { classes = again.Data; found = FindClassByYear(classes, year); }
                    if (found is null) errors.Add($"Class {year} create nahi hui: {r.Error}");
                }
                else errors.Add($"Class '{year}' create nahi hui: {r.Error}");
            }
            if (found is not null) classByYear[year] = found;
        }

        // ---- 4) Students: dedupe across sheets, then POST ----
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int ok = 0, failed = 0, dupes = 0, processed = 0;

        // blank-Year sibling inference sets (father/mother phone → year)
        var yearByPhone = plans.SelectMany(p => p.Rows)
            .Where(r => r.Year.Length > 0)
            .GroupBy(r => FirstPhoneKey(r))
            .Where(g => g.Key.Length > 6)
            .ToDictionary(g => g.Key, g => g.First().Year, StringComparer.OrdinalIgnoreCase);

        foreach (var p in plans)
        {
            foreach (var r in p.Rows)
            {
                ct.ThrowIfCancellationRequested();
                processed++;
                var key = NameKey(r.Name);
                if (!seen.Add(key))
                {
                    dupes++;
                    continue; // noon-return sheet par wohi bachcha dobara aata hai
                }
                progress.Report($"🎓 ({processed}/{totalRows}) {TrimTo(r.Name, 30)}…");

                try
                {
                    var year = r.Year;
                    if (year.Length == 0 && yearByPhone.TryGetValue(FirstPhoneKey(r), out var sy))
                    {
                        year = sy;
                        notes.Add($"⚠ {TrimTo(r.Name, 26)}: Year blank thi — siblings ({sy}) se guess ki gayi. Verify karein.");
                    }

                    ClassListItem? cls = null;
                    if (year.Length > 0 && classByYear.TryGetValue(year, out var c)) cls = c;
                    else cls = FindClassByYear(classes, year);
                    if (cls is null)
                    {
                        failed++;
                        errors.Add($"❌ {TrimTo(r.Name, 26)}: class '{(year.Length > 0 ? year : "(blank)")}' nahi mili — student skip.");
                        continue;
                    }

                    var busText = r.Bus1.Length > 0 ? r.Bus1 : r.Bus2;
                    int? busId = null;
                    if (busText.Length > 0 && int.TryParse(busText, out var bn2))
                        busId = buses.FirstOrDefault(b => b.BusNumber == bn2.ToString())?.Id;
                    if (busId is null && p.BusNo > 0)
                        busId = buses.FirstOrDefault(b => b.BusNumber == p.BusNo.ToString())?.Id;

                    var mobF = NormalizePhone(r.MobF);
                    var mobM = NormalizePhone(r.MobM);
                    if (mobF.Length == 0 && mobM.Length > 0) { mobF = mobM; mobM = ""; }

                    var fatherName = DeriveFatherName(r.Name, out var fatherGuessed);
                    if (fatherGuessed) notes.Add($"ℹ {TrimTo(r.Name, 26)}: Father Name naam ke tokens se guess — '{fatherName}' (profile par verify karein)");

                    var body = new StudentUpsertRequest
                    {
                        FullName = PrettyName(r.Name),
                        Gender = r.Bg switch { "B" => "Boy", "G" => "Girl", _ => "" },
                        IsNewStudent = r.OnFlag == "N" || r.Flag.Contains("NEW"),
                        RollNo = r.SlNo,
                        DateOfBirth = "2016-01-01",
                        ClassId = cls.Id,
                        FatherName = fatherName,
                        FatherPhone = mobF,
                        MotherPhone = string.IsNullOrWhiteSpace(mobM) ? null : mobM,
                        Address = r.Loc.Length > 0 ? r.Loc : "(address sheet mein blank tha)",
                        BusId = busId,
                        IsActive = null
                    };

                    var resp = await api.PostAsync<StudentDetailResponse>("api/students", body);
                    if (resp.Ok) ok++;
                    else { failed++; errors.Add($"❌ {TrimTo(r.Name, 26)}: {resp.Error}"); }
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"❌ {TrimTo(r.Name, 26)}: {ex.Message}");
                }
            }
        }

        // ---- driver login credentials file ----
        string? credsPath = null;
        if (credLines.Count > 0)
            try
            {
                var dir = Path.GetDirectoryName(filePath)!;
                credsPath = Path.Combine(dir, $"driver-logins-{DateTime.Now:yyyyMMdd-HHmm}.txt");
                var sb = new StringBuilder();
                sb.AppendLine("SmartSchool — Driver mobile logins (SAFE rakhein, kisi par forward na karein)");
                sb.AppendLine($"Banaye gaye: {DateTime.Now:dd-MMM-yyyy HH:mm} · file: {Path.GetFileName(filePath)}");
                sb.AppendLine(new string('-', 74));
                credLines.ForEach(l => sb.AppendLine(l));
                sb.AppendLine(new string('-', 74));
                sb.AppendLine("Driver app mein username+password se login. Password baad mein badalne ho → Admin mein driver Deactivate karke dobara banayein.");
                File.WriteAllText(credsPath, sb.ToString());
            }
            catch { credsPath = null; }

        var all = new List<string>();
        if (notes.Count > 0)
        {
            all.Add("──── NOTES ────");
            all.AddRange(notes.Take(25));
        }
        if (errors.Count > 0)
        {
            all.Add("──── ERRORS ────");
            all.AddRange(errors.Take(25));
        }
        all.Add($"↪ Dobari entries (FS2 noon sheets) skip: {dupes}");
        if (busesCreated > 0 || driversCreated > 0 || classesCreated > 0)
            all.Add($"🆕 Banaye gaye: {busesCreated} buses • {driversCreated} drivers • {classesCreated} classes");
        if (credsPath is not null) all.Add($"🔑 Driver logins save: {credsPath}");

        return new ImportResult
        {
            Total = totalRows,
            Imported = ok,
            Failed = failed,
            DupesSkipped = dupes,
            BusesCreated = busesCreated,
            DriversCreated = driversCreated,
            ClassesCreated = classesCreated,
            Errors = all,
            CredsFilePath = credsPath
        };
    }

    // ========================================================================
    //  FLAT SHEET PATH (simple headers on row 1 / csv)
    // ========================================================================

    private static async Task<ImportResult> ImportFlatAsync(
        List<Dictionary<string, string>> rows, ApiClient api, IProgress<string> progress, CancellationToken ct,
        int rowOffset)
    {
        var errors = new List<string>();
        if (rows.Count == 0)
            return new ImportResult
            {
                Errors = ["File mein headers ke ilawa koi row nahi mili (ya transport-format sheet hai — uske liye isi Import button se wahi file deelne se auto-detect hoga)."]
            };

        if (!rows[0].ContainsKey("name"))
            return new ImportResult
            {
                Total = rows.Count,
                Errors = ["Header samajh nahi aya — row 1 mein 'Student Name' column chahiye. Sample: Student Name, Father Name, Phone, Class, Roll No, Home Address, Bus #"]
            };

        var classes = (await api.ClassesAsync()).Data ?? new List<ClassListItem>();
        var buses = (await api.BusesAsync()).Data ?? new List<BusListItem>();

        int ok = 0, failed = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var r = rows[i];
            progress.Report($"Row {i + rowOffset + 1}: {r.GetValueOrDefault("name")}");
            try
            {
                var name = r.GetValueOrDefault("name")?.Trim() ?? "";
                if (name.Length < 3) { failed++; errors.Add($"Row {i + rowOffset + 1}: naam bohat chhota — '{name}'"); continue; }

                var clsText = r.GetValueOrDefault("class")?.Trim() ?? "";
                var cls = FindClass(classes, clsText) ?? FindClassByYear(classes, clsText);
                if (cls is null)
                {
                    failed++;
                    errors.Add($"Row {i + rowOffset + 1}: class '{clsText}' nahi mili — pehle Classes mein banain (e.g. '1-A' ya 'Year 1-A')");
                    continue;
                }

                int? busId = null;
                var busText = (r.GetValueOrDefault("bus") ?? "").Trim();
                if (busText.Length > 0)
                {
                    var norm = busText.TrimStart('B', 'b', '-', '#').Trim();
                    var bus = buses.FirstOrDefault(b =>
                        b.BusNumber.Equals(norm, StringComparison.OrdinalIgnoreCase) ||
                        b.BusNumber.Equals(busText, StringComparison.OrdinalIgnoreCase));
                    if (bus is null) errors.Add($"Row {i + rowOffset + 1}: bus '{busText}' nahi mili — bina bus ke import");
                    else busId = bus.Id;
                }

                var rawGender = (r.GetValueOrDefault("gender") ?? "").Trim().ToUpperInvariant();
                var gender = rawGender is "B" or "BOY" or "M" ? "Boy"
                    : rawGender is "G" or "GIRL" or "F" ? "Girl" : "";
                var on = (r.GetValueOrDefault("onflag") ?? "").Trim().ToUpperInvariant();

                var fatherName = (r.GetValueOrDefault("father") ?? "").Trim();
                if (fatherName.Length < 3) fatherName = DeriveFatherName(name, out _);

                var body = new StudentUpsertRequest
                {
                    FullName = name,
                    Gender = gender,
                    IsNewStudent = on == "N",
                    FatherName = fatherName,
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
                if (body.RollNo.Length == 0) body.RollNo = (i + 1).ToString();

                var resp = await api.PostAsync<StudentDetailResponse>("api/students", body);
                if (resp.Ok) ok++;
                else { failed++; errors.Add($"Row {i + rowOffset + 1}: {resp.Error}"); }
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"Row {i + rowOffset + 1}: {ex.Message}");
            }
        }
        return new ImportResult { Total = rows.Count, Imported = ok, Failed = failed, Errors = errors };
    }

    // ========================================================================
    //  READERS
    // ========================================================================

    private static List<Dictionary<string, string>> ReadFlatXlsx(XLWorkbook wb)
    {
        var ws = wb.Worksheets.First(w => w.LastRowUsed() is not null);
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
            var anyVal = false;
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

    // ========================================================================
    //  HELPERS
    // ========================================================================

    private static ClassListItem? FindClass(List<ClassListItem> classes, string text)
    {
        if (text.Length == 0) return null;
        var t = text.Replace(" ", "").ToLowerInvariant();
        return classes.FirstOrDefault(c =>
            N(c.DisplayName) == t || N(c.Name) == t ||
            N($"{c.Name}-{c.Section}") == t || N(c.Name) == "class" + t);

        static string N(string s) => s.Replace(" ", "").ToLowerInvariant();
    }

    /// <summary>"Y3"/"Year 3"/"FS2" wala matching — transport sheet style.</summary>
    private static ClassListItem? FindClassByYear(List<ClassListItem> classes, string year)
    {
        if (year.Length == 0) return null;
        var y = year.ToUpperInvariant().Replace(" ", "");
        var variants = y.StartsWith("FS")
            ? new[] { y, y.Insert(2, " "), "FOUNDATION2" }
            : int.TryParse(y.TrimStart('Y'), out var n)
                ? new[] { y, "YEAR" + n, n.ToString(), "GRADE" + n }
                : [y];
        return classes.FirstOrDefault(c =>
        {
            var dn = c.DisplayName.ToUpperInvariant().Replace(" ", "");
            var nm = c.Name.ToUpperInvariant().Replace(" ", "");
            var sect = c.Section.ToUpperInvariant();
            return variants.Contains(dn) || variants.Contains(nm) ||
                   variants.Contains(nm + "-" + sect) || variants.Contains(nm.Replace("-", ""));
        });
    }

    private static (string Name, string Section) YearToCreate(string year)
    {
        var y = year.ToUpperInvariant().Trim();
        if (y.StartsWith("FS"))
            return char.IsDigit(y.Last()) ? ($"FS {y[^1]}", "A") : ("", "");
        var digits = y.TrimStart('Y');
        return int.TryParse(digits, out var n) ? ($"Year {n}", "A") : ("", "");
    }

    /// <summary>Arabic naming: pehla token bachcha, baqi tokens aksar walid ka naam.</summary>
    private static string DeriveFatherName(string studentName, out bool guessed)
    {
        var parts = studentName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            guessed = true;
            return PrettyName(string.Join(' ', parts.Skip(1)));
        }
        guessed = false;
        return "(father name nahi mila)";
    }

    private static string PrettyName(string raw)
    {
        var s = string.Join(' ', raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var upper = s.Count(char.IsUpper);
        if (s.Length > 4 && upper > s.Length * 0.6)
            return TitleCase(s.ToLowerInvariant());
        return s;
    }

    private static string TitleCase(string s) =>
        string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

    private static string NameKey(string s) =>
        new string(s.ToUpperInvariant().Where(char.IsLetter).ToArray());

    private static string FirstPhoneKey(SheetRow r)
    {
        var f = NormalizePhone(r.MobF);
        if (f.Length > 6) return f;
        return NormalizePhone(r.MobM);
    }

    private static string NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        // Excel number-cell se leading zero kharch hojata: '559308594' -> '0559308594'
        if (digits.Length == 9 && digits.StartsWith('5')) digits = "0" + digits;
        return digits;
    }

    private static string ParseDob(string? raw)
    {
        if (!string.IsNullOrWhiteSpace(raw) &&
            DateTime.TryParse(raw, out var d) && d is { Year: > 2000 })
            return d.ToString("yyyy-MM-dd");
        return "2016-01-01";
    }

    private static string TrimTo(string s, int n) => s.Length <= n ? s : s[..n];

    private static string UniqueUsername(string fullName, List<DriverListItem> existing)
    {
        var tokens = fullName.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var baseName = tokens.Length >= 2 ? $"{tokens[0]}.{tokens[^1]}" : tokens.FirstOrDefault() ?? "driver";
        baseName = new string(baseName.Where(c => char.IsLetterOrDigit(c) || c == '.').ToArray());
        if (baseName.Length < 3) baseName = "driver." + baseName;
        var candidate = baseName;
        for (var i = 2; existing.Any(d => d.Username.Equals(candidate, StringComparison.OrdinalIgnoreCase)); i++)
            candidate = $"{baseName}{i}";
        return candidate;
    }

    private static string GeneratePassword(string driverName, int busNo)
    {
        const string pool = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789!@#";
        var rnd = System.Security.Cryptography.RandomNumberGenerator.GetString(pool, 4);
        var first = driverName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Driver";
        first = char.ToUpperInvariant(first[0]) + first[1..].ToLowerInvariant();
        return $"{first}B{busNo}@{rnd}";
    }

    private static string NormalizeHeader(string h) => h.Trim().ToLowerInvariant().Replace("_", " ").Trim();

    private static string[] SplitCsv(string line)
    {
        var cells = new List<string>();
        var inQuotes = false;
        var cur = new StringBuilder();
        foreach (var ch in line)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (ch == ',' && !inQuotes) { cells.Add(cur.ToString()); cur.Clear(); continue; }
            cur.Append(ch);
        }
        cells.Add(cur.ToString());
        return cells.ToArray();
    }

    [GeneratedRegex(@"(?i)BUS NO:\s*(\d+)\s+(?:FS2\s+AT\s+12\s+PM\s+)?(.+?)\s+Mob:\s*([\d\s]+)$")]
    private static partial Regex BusMetaRegex();

    [GeneratedRegex(@"(?i)BUS NO:\s*\d+\s+(.+?)\s+Mob:")]
    private static partial Regex MetaLooseRegex();

    [GeneratedRegex(@"\s+[MF]$")]
    private static partial Regex SingleLetterSuffix();
}
