namespace SmartSchool.Shared.Helpers;

/// <summary>
/// Provider-neutral WhatsApp alert templates (English + Roman-Urdu).
/// Language is chosen per school with WhatsApp:Language ("en" | "ur").
/// WhatsApp clients render *bold* text natively.
/// </summary>
public static class WhatsAppTemplates
{
    public sealed record TemplateVars(
        string StudentName,
        string ClassName,
        string BusNumber,
        string Time,
        string Location);

    /// <summary>"BOARDED" alert (pick-in).</summary>
    public static string PickIn(TemplateVars v, string language) => language == "ur"
        ? $"""
🚌 SmartSchool Alert

✅ Aap ka bacha *{v.StudentName}* (Class {v.ClassName}) Bus #{v.BusNumber} mai baith chuka hai.

🕐 Waqt: {v.Time}
📍 Jagah: {v.Location}

— SmartSchool System
"""
        : $"""
🚌 SmartSchool Alert

✅ Your child *{v.StudentName}* (Class {v.ClassName}) has BOARDED Bus #{v.BusNumber}.

🕐 Time: {v.Time}
📍 Location: {v.Location}

— SmartSchool System
""";

    /// <summary>"DROPPED OFF safely" alert (drop-out).</summary>
    public static string DropOut(TemplateVars v, string language) => language == "ur"
        ? $"""
🚌 SmartSchool Alert

🏠 Aap ka bacha *{v.StudentName}* (Class {v.ClassName}) Bus #{v.BusNumber} se mehfooz utar chuka hai.

🕐 Waqt: {v.Time}
📍 Jagah: {v.Location}

— SmartSchool System
"""
        : $"""
🚌 SmartSchool Alert

🏠 Your child *{v.StudentName}* (Class {v.ClassName}) has been DROPPED OFF safely.

🕐 Time: {v.Time}
📍 Location: {v.Location}

— SmartSchool System
""";
}
