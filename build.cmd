@echo off
rem Builds ClaudeProfiles.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize /out:ClaudeProfiles.exe ^
  /r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.CSharp.dll ^
  /win32icon:app.ico /resource:app.ico,app.ico ClaudeProfiles.cs AppId.cs
