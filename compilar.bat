@echo off
rem Uso: compilar.bat "C:\...\steamapps\common\Rubinite"
rem Necesita Mono.Cecil.dll (0.11.x, net40) en esta carpeta. Genera VistaPreviaDanio.exe.
setlocal
if "%~1"=="" ( echo Indica la carpeta del juego. & exit /b 1 )
set "M=%~1\Rubinite_Data\Managed"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
cd /d "%~dp0"

"%CSC%" -noconfig -nologo -codepage:65001 -optimize+ -target:library -nostdlib -out:RubinitePreviaDanio.dll ^
  -r:"%M%\mscorlib.dll" -r:"%M%\netstandard.dll" -r:"%M%\System.dll" -r:"%M%\System.Core.dll" ^
  -r:"%M%\UnityEngine.dll" -r:"%M%\UnityEngine.CoreModule.dll" -r:"%M%\UnityEngine.UI.dll" ^
  -r:"%M%\UnityEngine.UIModule.dll" -r:"%M%\Assembly-CSharp.dll" src\Previa.cs || exit /b 1

"%CSC%" -nologo -codepage:65001 -optimize+ -target:exe -out:VistaPreviaDanio.exe -r:Mono.Cecil.dll ^
  -resource:Mono.Cecil.dll,Mono.Cecil.dll -resource:RubinitePreviaDanio.dll,RubinitePreviaDanio.dll ^
  src\Instalador.cs || exit /b 1

echo Listo: VistaPreviaDanio.exe
