' LexiPop launcher: run the DLL through the signed dotnet host to avoid
' SmartScreen blocking the unsigned exe. Window style 0 = hidden console.
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")

base = fso.GetParentFolderName(WScript.ScriptFullName)
dotnet = base & "\.dotnet\dotnet.exe"
If Not fso.FileExists(dotnet) Then dotnet = "dotnet"
dll = base & "\src\LexiPop\bin\Release\net10.0-windows\LexiPop.dll"

shell.CurrentDirectory = base
shell.Run """" & dotnet & """ """ & dll & """", 0, False
