# Git & GitHub Setup — SmartSchool Pro

Sandbox **workspace ka kaam hai code likhna**, code ka **permanent ghar GitHub** hai.
Ye file aap ko 10 minute mein full setup deti hai.

## 1) GitHub account + repo (2 min)

1. https://github.com → Sign up (free).
2. New repository → name: **`smartschool-pro`** → **Private** → Create (README add NA karein — repo pehle se populated hai).

## 2) Apne Windows PC par tools (30-40 min, free)

| Tool | Kahan se | Kyun |
|---|---|---|
| Git for Windows | git-scm.com | code push/pull |
| .NET 8 SDK | dot.net/download | API build/run |
| Visual Studio 2022 **Community** (free) | visualstudio.microsoft.com | Phase 2 WPF ke liye best |
| SQL Server Express + SSMS | microsoft.com/sql-server/sql-server-downloads | database |
| Flutter SDK | docs.flutter.dev (Phase 3 par) | mobile apps |

(Optional lekin aasaan: **GitHub Desktop** ya `winget install GitHub.cli` → `gh auth login`.)

## 3) Code PC par laana (3 commands)

Agar pehli martaba — Arena se project folder download kar ke PC par rakhein, phir:

```powershell
cd SmartSchoolPro

git remote add origin https://github.com/<AAPKA-USERNAME>/smartschool-pro.git
git branch -M main
git push -u origin main
```

> Repo pehle se `git init` ho chukka hai aur pehla commit bana hua hai — sirf remote add kar ke push karna hai.

Baad ka roz ka cycle:

```
# coding se pehle          # coding ke baad (PC par)
git pull                   git add -A
                           git commit -m "kya banaya"
                           git push
```

## 4) Automatic checks (GitHub Actions — free)

`.github/workflows/api-ci.yml` laga di hai: **har push par GitHub ka server khud
`dotnet restore + build + EF model check` chalata hai** aur ghalat code par laal ✗
dikha deta hai. Private repo free quota ~2000 min/month — hamari build ~2 min ki hai,
yani roz kaini pushes aasani se fit.

Phase 2 (WPF) mein doosri workflow `windows-latest` runner par add hogi jo **WPF .exe /
installer khud build kar ke artifact** bana degi — PC par Visual Studio na ho toh bhi
exe mil jayegi, phir usay **Google Drive par school ko share** kar saktay hain.

## 5) Google Drive — code ke liye NAHI ❌

| Kaam | Tool | Wajah |
|---|---|---|
| Source code | **GitHub/Git** | history, branches, rollback, merge — Drive mein kuch nahi |
| DB backup (.bak), installers, docs ka share | **Google Drive** | free sharing link |
| Student photos/documents (Module 1) | API upload folder / cloud storage | Drive sync folder code ki tarah treat na karein |

Drive sync ke andar source folder rakhna = corruption + conflicts ka garam waada. Mat karein. 🙂

## 6) Agar PC kamzor hai — GitHub Codespaces (browser mein full dev PC)

GitHub par repo ke upar **Code → Codespaces → Create**: browser mein poora Linux dev
environment (dotnet preinstalled, free monthly quota). `dotnet run` + forwarded port se
API chala kar test kar saktay hain bina kuch install kiye.
