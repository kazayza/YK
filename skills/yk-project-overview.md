# Skill: YKCoatings — Overview

## الوصف
مهارة عامة للعمل على مشروع YK Coatings ERP (Blazor Server .NET 8 + Dapper + SQL Server).

## خطوات البدء في أي جلسة
1. اقرأ `memory-bank/projectbrief.md` و`memory-bank/techContext.md` و`memory-bank/systemPatterns.md`.
2. اقرأ `memory-bank/activeContext.md` لمعرفة آخر حالة.
3. افحص `git status` (إن وجد) قبل أي تعديل.

## القواعد الذهبية
- الاسم: **YKCoatings** كودياً / **واي كي كوتينج** معروضاً. ممنوع الاسم القديم Dizurde.
- الواجهة عربية RTL، الكود إنجليزي.
- كل خدمة ترث `BaseDbService` وتُسجَّل Scoped في `Program.cs`.
- لا SQL بدون معاملات (Parameters).
- لا تعديل على `bin/`, `obj/`, `publish/`.
- بعد كل مهمة: حدّث `memory-bank/activeContext.md` و`progress.md`.

## بناء وتشغيل
```powershell
dotnet build YKCoatings.sln
dotnet run --project YKCoatings.csproj
```
