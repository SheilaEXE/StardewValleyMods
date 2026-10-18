@echo off
set "ROOT=C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\Mods\Home Sweet Home - Spouse Dialogues"

echo Limpando arquivos que podem ter sido compilados na pasta mae errada...

del "%ROOT%\HomeSweetHomeSpouseDialogues.dll" 2>nul
del "%ROOT%\HomeSweetHomeSpouseDialogues.pdb" 2>nul
del "%ROOT%\HomeSweetHomeSpouseDialogues.deps.json" 2>nul
del "%ROOT%\manifest.json" 2>nul
del "%ROOT%\config.json" 2>nul
del "%ROOT%\README - dialog keys.txt" 2>nul

echo Pronto. Isso nao apagou as pastas:
echo - "%ROOT%\Home Sweet Home - Spouse Dialogues"
echo - "%ROOT%\[CP] Home Sweet Home - Spouse Dialogues"
echo.
pause
