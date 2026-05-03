# Релиз и распространение

## Версия приложения и журнал изменений

1. Номер версии задаётся в `src/GamePcChecker.App/GamePcChecker.App.csproj`:
   - `<Version>` — семантическая версия (например `1.2.0`), видна в UI и в проверке обновлений;
   - `<AssemblyVersion>` и `<FileVersion>` — обычно `мажор.минор.патч.0` (например `1.2.0.0`).
2. После **любых** значимых правок перед коммитом/релизом:
   - **увеличьте** версию (как минимум последний номер патча для мелких исправлений);
   - добавьте запись в **[CHANGELOG.md](CHANGELOG.md)** в секцию `[Unreleased]` или сразу в новый блок `[x.y.z]` с датой и перечислением изменений (добавлено / изменено / исправлено).
3. Тег на GitHub для релиза должен совпадать с версией из csproj (например `v1.2.1` для сборки `1.2.1`).

Подробная история релизов — в **CHANGELOG.md** (в поставке копия переносится в **`etc\CHANGELOG.md`**, см. `scripts/organize-publish-layout.ps1`).

## Сборка для других ПК

Из корня репозитория:

```powershell
.\scripts\publish-release.ps1 -SelfContained
```

Появится папка `artifacts/publish-win-x64-selfcontained` (в корне — **`GamePcChecker.exe`**, каталоги **`data`**, **`temp`**, **`etc`**) и zip `artifacts/GamePcChecker-v…-win-x64-selfcontained.zip`. Загрузите zip в GitHub Release как основной артефакт.

По умолчанию сборка **single-file** (один exe в корне + подпапки). Много `.dll` рядом с exe: **`.\scripts\publish-release.ps1 -SelfContained -MultiFile`**.

Вариант **без** встроенного рантайма (меньше размер, на ПК должен быть установлен **.NET Desktop** той же основной версии, что и `TargetFramework` в проекте):

```powershell
.\scripts\publish-release.ps1
```

Первый запуск single-file может быть чуть медленнее из‑за распаковки. Вариант **`-MultiFile`** — классическая папка публикации без single-file.

## Релиз на GitHub без локального PAT (Actions)

В репозитории включён workflow **[Release](.github/workflows/release.yml)**:

- При **push тега** вида `v*` (например после `git push origin v1.2.3`) GitHub сам собирает self-contained zip и создаёт/обновляет **Release** с вложениями.
- Если тег **уже есть**, а Release с файлами — нет: **Actions → Release → Run workflow**, в поле ввести тег (например **`v1.2.3`**) и запустить. Артефакты появятся у этого тега без повторного `git push`.

Локальный сценарий с PAT по-прежнему: `.\scripts\create-github-release.ps1` (переменная `GITHUB_TOKEN` или `GH_TOKEN`).

## Проверка обновлений в приложении

1. Репозиторий проекта: **https://github.com/ljosefl/GamePcChecker** — при первом выкладывании создайте пустой публичный репозиторий с таким именем (или скорректируйте URL ниже).
2. Рядом с `GamePcChecker.exe` пользователь (или вы при упаковке) копирует `github-update.example.json` → `github-update.json` и подставляет ваши `owner` и `repo`.
3. Либо задаёт переменную среды `GAMEPCCHECKER_GITHUB_UPDATE` в формате `владелец/репо`.
4. При запуске приложение запрашивает `GET /repos/{owner}/{repo}/releases/latest` (без токена, лимит GitHub ~60 запросов/час с одного IP).

В релиз прикрепляйте zip или exe с именем, в котором есть `win`/`x64`/`GamePcChecker` — так приложение чаще выберет правильный файл для кнопки «Скачать файл». Иначе откроется страница релиза.

## Git: тег и релиз на GitHub

Подставьте свой URL и имя ветки.

```bash
git init
git add .
git commit -m "Initial release"
git branch -M main
git remote add origin https://github.com/ljosefl/GamePcChecker.git
git push -u origin main
git tag -a v1.2.1 -m "Release 1.2.1"
git push origin v1.2.1
```

Создание релиза с артефактами через GitHub CLI (подставьте актуальную версию и имя zip из `artifacts/`):

```bash
gh release create v1.2.1 artifacts/GamePcChecker-v1.2.1-win-x64-selfcontained.zip --title "Game PC Checker 1.2.1" --notes-file CHANGELOG.md
```

Через PowerShell и токен (**Fine-grained** или classic `repo`), после сборки zip:

```powershell
$env:GITHUB_TOKEN = "<ваш PAT>"
.\scripts\create-github-release.ps1
```

Или через веб‑интерфейс: **Releases → Create a new release → тег с номером версии (например `v1.2.1`) → заголовок с номером версии → в описание вставьте соответствующий фрагмент из CHANGELOG.md** → прикрепите zip из `artifacts\`.

## Новый открытый репозиторий

На GitHub: **New repository → Public**, без README (или слейте позже). Затем команды выше.

Личный токен для HTTPS‑push создаётся в **Settings → Developer settings → Personal access tokens** (scope `repo` для частных; для публичного достаточно прав на push в зависимости от настроек организации).
