using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace SmartSchool.Admin.Helpers   // <-- SmartSchool.Admin namespace
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════════════
    ///  FAST WHATSAPP FLOW — delay-zero karne wala runner
    ///
    ///  Yahan 3 tajurba-shuda usool hain (Waheed POS mein 6 mahine
    ///  laga kar nikale):
    ///
    ///  USOOL 1 — JAMNA Khatam:
    ///  Bhaari kaam (PDF render waghera) UI thread par hota hai (WPF ka
    ///  FlowDocument doosre thread par nahi ja sakta). Click ke foran baad
    ///  pehle Dispatcher ko EK Render-priority chakkar dene do — button ka
    ///  dabna/cursor ka badalna user ko FORAN nazar aa jata hai, phir bhaari
    ///  kaam shuru. App "Not Responding" kabhi nahi lagti.
    ///
    ///  USOOL 2 — Bhaari kaam ka waqt WhatsApp ke khulne ke PEECHHE:
    ///  WhatsApp BAND ho to us ke khulne mein khud 1.5-3 second lagte hain.
    ///  Launch ka hukm PEHLE de do, apna bhaari kaam ussi dauran kar lo —
    ///  user dono ko aik hi intezaar samajhta hai.
    ///
    ///  USOOL 3 — Clipboard ka tarteeb:
    ///  WhatsApp PEHLE SE chal rahi ho to usay haath mat lagao se pehle —
    ///  saamne aate hi clipboard ka qabza le legi. Pehle file banao,
    ///  clipboard set KARO (tasdeeq ke sath), PHIR WhatsApp saamne lao.
    ///══════════════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static class FastShare
    {
        /// <summary>
        /// FAST FLOW: file banwao + clipboard + WhatsApp — sab tarteeb ke sath.
        ///
        /// buildFile: aap ka file banane wala kaam (misal PDF export).
        /// Yeh UI thread par hi chalta hai (WPF ki pabandi), is liye
        /// UI-freeze usool se bachaya jata hai.
        /// </summary>
        public static void ShareFile(Window owner,
            Func<string> buildFile,          // file ka poora path return kare
            string? phone = null,
            string? caption = null)
        {
            Busy(owner, true);

            // USOOL 1: pehle screen apna roop dikha de (button press, cursor)
            owner?.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

            owner?.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    // USOOL 2: WhatsApp band hai to pehle launch ka hukm —
                    // hamara bhaari kaam us ke khulne ke peechHE chhupega
                    bool pehleSeChalRahi = Services.WhatsAppShareService.IsWhatsAppRunning();
                    if (!pehleSeChalRahi)
                        Services.WhatsAppShareService.OpenWhatsApp(phone, caption);

                    // bhaari kaam (PDF render waghera) — WhatsApp abhi load ho rahi hai
                    string path = buildFile();

                    // USOOL 3: clipboard PEHLE (WhatsApp abhi background — qabza hamara)
                    Services.WhatsAppShareService.CopyFileToClipboard(path);

                    // ab WhatsApp saamne (agar pehle se chal rahi thi)
                    if (pehleSeChalRahi)
                        Services.WhatsAppShareService.OpenWhatsApp(phone, caption);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Share fail hua:\n\n" + ex.Message,
                        "Share", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                finally { Busy(owner, false); }
            }));
        }

        /// <summary>Wait cursor + button disable (UI freeze ka ehsaas khatam).</summary>
        private static void Busy(Window? owner, bool on)
        {
            if (owner == null) return;
            owner.Dispatcher.Invoke(() =>
            {
                owner.Cursor = on ? System.Windows.Input.Cursors.Wait : null;
                Mouse.OverrideCursor = on ? System.Windows.Input.Cursors.Wait : null;
            });
        }
    }
}
