using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;

namespace SmartSchool.Admin.Services   // <-- SmartSchool.Admin namespace
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════════════
    ///  WHATSAPP FILE SHARE — A to Z (Waheed POS ke 6 mahine ke tajurbe se)
    ///══════════════════════════════════════════════════════════════════════════════════════
    ///  PEHLI HAQEEQAT (samajhna zaroori):
    ///  WhatsApp kisi bhi desktop program ko ijazat NAHI deti ke woh file
    ///  khud attach kar ke bhej de. Na wa.me link se, na desktop app se —
    ///  yeh WhatsApp ki security pabandi hai, HAR software par.
    ///  (Direct bhejne ka tareeqa sirf WhatsApp Business API hai — paid,
    ///  company verification, Meta se.)
    ///
    ///  SAB SE QAREEB TAREEN BEHTAREEN RASTA (yeh class):
    ///     1. File clipboard par copy (Ctrl+V se attach ho jaye)
    ///     2. WhatsApp khud khul jati hai (Desktop app, warna Web)
    ///     3. User chat mein Ctrl+V — file attach!
    ///
    ///  Yani user ka kaam: Aik Ctrl+V. Na folder dhoondna, na paperclip.
    /// ═════════════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static class WhatsAppShareService
    {
        // ═══════════════════════════════════════════════════════════════════════════════════════════════
        //  1) MAIN ENTRY — file clipboard par + WhatsApp khula
        //════════════════════════════════════════════════════════════════════════════════════════════════
        /// <param name="filePath">Jo file bhejni hai (PDF waghera).</param>
        /// <param name="phone">Kis number par (khali = sirf WhatsApp khulegi).
        ///                     "03001234567" ya "+923001234567" dono chalein ge.</param>
        /// <param name="caption">Chat mein likha jane wala paighaam (number ke sath hi jata hai).</param>
        public static bool Share(string filePath, string? phone = null, string? caption = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                MessageBox.Show("File nahi mili — dobara koshish karein.",
                    "WhatsApp Share", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            bool copied = CopyFileToClipboard(filePath);   // pehle clipboard (hamara qabza)
            bool opened = OpenWhatsApp(phone, caption);    // phir WhatsApp

            if (!copied || !opened)
            {
                string problem = !opened
                    ? "WhatsApp khul nahi saka (na Desktop app mila, na browser)."
                    : "File clipboard par nahi ja saki (koi doosri app clipboard rok rahi hai).";
                MessageBox.Show(problem + "\n\nFile yahan mehfooz hai:\n" + filePath +
                    "\n\nWhatsApp chat mein paperclip se yeh file chun lein.",
                    "WhatsApp", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return copied || opened;
        }

        // ══════════════════════════════════════════════════════════════════════════════════════════════
        //  2) FILE SILENTLY CLIPBOARD PAR — yeh class ka dil hai
        //════════════════════════════════════════════════════════════════════════════════════════════════
        /// <summary>
        /// File ko FILE ki soorat mein clipboard par daalta hai — WhatsApp
        /// mein Ctrl+V se attachment ban jati hai.
        ///
        /// WAR PE JO MASLE AATAY HAIN (aur yahan un ka hal):
        ///  (a) "Clipboard sirf woh app set kar sakti hai jis ke paas qabza
        ///      ho" — WhatsApp saamne aa jaye to hamari set KHAMOSHI se fail
        ///      hoti hai, koi error nahi. IS LIYE: set ke baad WAPIS PARH
        ///      kar TASDEEQ karte hain (ClipboardHasFile).
        ///  (b) Purani file ka handle chipak jana — IS LIYE: pehle Clear(),
        ///      zara dam, phir nayi file.
        ///  (c) Clipboard.Flush() KABHI MAT LAGAYEIN — wo data Windows ke
        ///      hawale kar deta hai, phir Clear() usay hata nahi pata aur
        ///      purani file hamesha ke liye chipak jati hai. SetDataObject
        ///      ka doosra parameter (true) waise bhi kaafi hai.
        ///  (d) Delay — pehli koshish FORAN (koi sleep nahi; aam tor par
        ///      yehi kaamyab hoti hai kyunke hamari window saamne hai).
        ///      Sleep sirf nakaami par, aur sirf 15ms. Max 6 koshishein.
        /// </summary>
        public static bool CopyFileToClipboard(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    // (1) clipboard bilkul khali — purana handle chhoot jaye
                    try { Clipboard.Clear(); } catch { }
                    if (attempt > 0) Thread.Sleep(15);

                    // (2) ab nayi file (FileDropList = FILE, text nahi)
                    var files = new StringCollection { path };
                    var data = new DataObject();
                    data.SetFileDropList(files);

                    // "Copy" ka nishan — warna kuch apps ise "Move" samajh kar
                    // asal file ko jagah se hata sakti hain.
                    var effect = new MemoryStream(BitConverter.GetBytes((int)System.Windows.DragDropEffects.Copy));
                    data.SetData("Preferred DropEffect", effect);

                    // (3) set — Flush() NAHI (upar wajah likhi hai)
                    Clipboard.SetDataObject(data, true);

                    // (4) TASDEEQ: kya waqai HAMARI nayi file gayi?
                    if (ClipboardHasFile(path)) return true;
                }
                catch (System.Runtime.InteropServices.COMException) { /* qabza kisi aur ke paas */ }
                catch { }

                Thread.Sleep(15);
            }

            // aakhri koshish — tasdeeq ke baghair (kuch na hone se behtar)
            try
            {
                var files = new StringCollection { path };
                var data = new DataObject();
                data.SetFileDropList(files);
                Clipboard.SetDataObject(data, true);
                return ClipboardHasFile(path);
            }
            catch { return false; }
        }

        /// <summary>Clipboard wapas parh kar tasdeeq — isi ke baghair purani
        /// file chupke se pari reh jati thi.</summary>
        private static bool ClipboardHasFile(string path)
        {
            try
            {
                if (!Clipboard.ContainsFileDropList()) return false;
                foreach (string? f in Clipboard.GetFileDropList())
                {
                    if (!string.IsNullOrEmpty(f) &&
                        string.Equals(Path.GetFullPath(f), Path.GetFullPath(path),
                                      StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        // ══════════════════════════════════════════════════════════════════════════════════════════════
        //  3) WHATSAPP KHOLNA — Desktop app pehle, warna Web
        //════════════════════════════════════════════════════════════════════════════════════════════════
        /// <summary>
        /// Kya WhatsApp Desktop pehle se chal rahi hai? IS SE TARTIB KA
        /// FAISLA HOTA HAI (nahi to delay aur copy dono masle aate hain):
        ///   * BAND hai   → pehle kholo (khulne mein 1.5-3s lagte hain —
        ///                  apna bhaari kaam ussi dauran kar lo)
        ///   * CHAL RAHI  → pehle file/clipboard, PHIR saamne lao (warna
        ///                  wo clipboard ka qabza le kar hamari set bigaar deti hai)
        /// </summary>
        public static bool IsWhatsAppRunning()
        {
            try
            {
                foreach (var name in new[] { "WhatsApp", "WhatsApp.Desktop" })
                    if (Process.GetProcessesByName(name).Length > 0) return true;
            }
            catch { }
            return false;
        }

        /// <summary>WhatsApp kholta hai (number + caption ke sath ho to usi chat par).</summary>
        public static bool OpenWhatsApp(string? phone = null, string? caption = null)
        {
            string digits = OnlyDigits(phone);
            string text = string.IsNullOrWhiteSpace(caption) ? "" : Uri.EscapeDataString(caption);

            // 1) WhatsApp Desktop app ka apna scheme
            try
            {
                string uri = digits.Length >= 8
                    ? $"whatsapp://send?phone={digits}" + (text.Length > 0 ? $"&text={text}" : "")
                    : "whatsapp://";
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                return true;
            }
            catch { }

            // 2) Desktop app nahi mila — browser mein WhatsApp Web
            try
            {
                string web = digits.Length >= 8
                    ? $"https://wa.me/{digits}" + (text.Length > 0 ? $"?text={text}" : "")
                    : "https://web.whatsapp.com/";
                Process.Start(new ProcessStartInfo(web) { UseShellExecute = true });
                return true;
            }
            catch { }

            return false;
        }

        /// <summary>Folder khol kar file select (fallback: drag & drop ke liye).</summary>
        public static void TryRevealInExplorer(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"")
                { UseShellExecute = true });
            }
            catch { }
        }

        // ══════════════════════════════════════════════════════════════════════════════════════════════
        //  4) PHONE NUMBER FORMAT (Pakistan-aware)
        //════════════════════════════════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 03001234567 → 923001234567 (WhatsApp chahiye country-code ke sath,
        /// + ke BAGHAIR). Pehle se 92/0092 ho to theek karta hai.
        /// </summary>
        public static string OnlyDigits(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return "";
            var sb = new StringBuilder();
            foreach (char c in phone) if (char.IsDigit(c)) sb.Append(c);
            string d = sb.ToString();
            if (d.Length == 0) return "";

            if (d.StartsWith("0092")) d = d.Substring(2);          // 0092xxx -> 92xxx
            else if (d.StartsWith("92")) { /* pehle se theek */ }
            else if (d.StartsWith("0")) d = "92" + d.Substring(1); // 03xx -> 923xx
            else if (d.Length == 10 && d.StartsWith("3")) d = "92" + d;

            return d.Length is >= 10 and <= 15 ? d : "";
        }
    }
}
