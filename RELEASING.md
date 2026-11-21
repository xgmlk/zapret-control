# Выпуск релиза

1. Обновите `VERSION` и локальный `CHANGELOG`.
2. Добавьте `.github/release-notes/v<version>.md` — полный список изменений
   для пользователей. Без этого файла релизный workflow завершится ошибкой.
3. Соберите и проверьте:
   `build.ps1`, `tests\run-tests.ps1 -NoLive`, `tests\t7-smoke.ps1`.
4. Закоммитьте изменения и запушьте `main`.
5. Поставьте тег `v<version>` и запушьте его:
   `git tag -a v<version> -m "Zapret Control <version>"`,
   `git push origin v<version>`.
6. Workflow `release` соберёт Setup, прогонит тесты, сформирует `release.json`
   и опубликует GitHub Release.
7. Проверьте assets и SHA256. Если сертификата T2 нет, workflow создаёт
   prerelease — после проверки переведите его в latest:
   `gh release edit v<version> --prerelease=false --latest`.

Тело релиза собирается автоматически:

- шапка из `.github/release-notes-header.md` дословно;
- раздел «Что изменилось» из `.github/release-notes/v<version>.md`;
- ссылка **Full Changelog** на сравнение с предыдущим тегом.

`--generate-notes` не заменяет полный список изменений. Шапку не переводить
на английский.
