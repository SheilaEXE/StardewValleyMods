@echo off
set "ROOT=C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\Mods\Home Dialogues"

echo Limpando arquivos que podem ter sido compilados na pasta mae errada...

del "%ROOT%\HomeDialogues.dll" 2>nul
del "%ROOT%\HomeDialogues.pdb" 2>nul
del "%ROOT%\HomeDialogues.deps.json" 2>nul
del "%ROOT%\manifest.json" 2>nul
del "%ROOT%\config.json" 2>nul
del "%ROOT%\README - dialog keys.txt" 2>nul

echo Pronto. Isso nao apagou as pastas:
echo - "%ROOT%\Home Dialogues"
echo - "%ROOT%\[CP] Home Dialogues"
echo.
pause
