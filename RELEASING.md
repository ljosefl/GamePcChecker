# Релиз и распространение

## Версия приложения

Номер задаётся в `src/GamePcChecker.App/GamePcChecker.App.csproj` в элементе `<Version>` (сейчас `1.0.0`). При сборке он попадает в подпись сборки и в блок «версия» в правом верхнем углу главного окна.

## Сборка для других ПК

Из корня репозитория:

```powershell
.\scripts\publish-release.ps1 -SelfContained
```

Появится папка `artifacts/publish-win-x64-selfcontained` и zip `artifacts/GamePcChecker-v…-win-x64-selfcontained.zip`. Загрузите zip в GitHub Release как основной артефакт (удобнее для пользователей, чем один exe).

Вариант **без** встроенного рантайма (меньше размер, на ПК должен быть установлен **.NET Desktop** той же основной версии, что и `TargetFramework` в проекте):

```powershell
.\scripts\publish-release.ps1
```

Опция `-SingleFile` у скрипта собирает один exe (медленнее старт из‑за распаковки); для WPF обычно предпочтительнее папка публикации или zip.

## Проверка обновлений в приложении

1. Репозиторий проекта: **https://github.com/ljosefl/GamePcChecker** — при первом выкладывании создайте пустой публичный репозиторий с таким именем (или скорректируйте URL ниже).
2. Рядом с `GamePcChecker.exe` пользователь (или вы при упаковке) копирует `github-update.example.json` → `github-update.json` и подставляет ваши `owner` и `repo`.
3. Либо задаёт переменную среды `GAMEPCCHECKER_GITHUB_UPDATE` в формате `владелец/репо`.
4. При запуске приложение запрашивает `GET /repos/{owner}/{repo}/releases/latest` (без токена, лимит GitHub ~60 запросов/час с одного IP).

В релиз прикрепляйте zip или exe с именем, в котором есть `win`/`x64`/`GamePcChecker` — так приложение чаще выберет правильный файл для кнопки «Скачать файл». Иначе откроется страница релиза.

Тег релиза на GitHub должен совпадать с версией приложения (например тег `v1.0.0` для сборки `1.0.0`).

## Git: тег и релиз на GitHub

Подставьте свой URL и имя ветки.

```bash
git init
git add .
git commit -m "Initial release"
git branch -M main
git remote add origin https://github.com/ljosefl/GamePcChecker.git
git push -u origin main
git tag -a v1.0.0 -m "Release 1.0.0"
git push origin v1.0.0
```

Создание релиза с артефактами через GitHub CLI:

```bash
gh release create v1.0.0 artifacts/GamePcChecker-v1.0.0-win-x64-selfcontained.zip --title "Game PC Checker 1.0.0" --notes "Первая публичная сборка."
```

Или через веб‑интерфейс: **Releases → Draft a new release → выбрать тег → приложить zip**.

## Новый открытый репозиторий

На GitHub: **New repository → Public**, без README (или слейте позже). Затем команды выше.

Личный токен для HTTPS‑push создаётся в **Settings → Developer settings → Personal access tokens** (scope `repo` для частных; для публичного достаточно прав на push в зависимости от настроек организации).
