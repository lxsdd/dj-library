@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "FRAME64=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
set "FRAME32=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"

if exist "%FRAME64%\csc.exe" (
  set "FRAME=%FRAME64%"
) else if exist "%FRAME32%\csc.exe" (
  set "FRAME=%FRAME32%"
) else (
  echo.
  echo ERROR: .NET Framework C# compiler csc.exe was not found.
  echo Expected under:
  echo   %FRAME64%
  echo or
  echo   %FRAME32%
  echo.
  exit /b 1
)

set "CSC=%FRAME%\csc.exe"
set "WPF=%FRAME%\WPF"

set "PF=%WPF%\PresentationFramework.dll"
set "PC=%WPF%\PresentationCore.dll"
set "WB=%WPF%\WindowsBase.dll"
set "SX=%FRAME%\System.Xaml.dll"
set "SYS=%FRAME%\System.dll"
set "CORE=%FRAME%\System.Core.dll"
set "XML=%FRAME%\System.Xml.dll"
set "WEBEXT=%FRAME%\System.Web.Extensions.dll"
set "SEC=%FRAME%\System.Security.dll"

if not exist "%PF%" set "PF=%FRAME%\PresentationFramework.dll"
if not exist "%PC%" set "PC=%FRAME%\PresentationCore.dll"
if not exist "%WB%" set "WB=%FRAME%\WindowsBase.dll"

if not exist "%PF%" goto :missing_pf
if not exist "%PC%" goto :missing_pc
if not exist "%WB%" goto :missing_wb
if not exist "%SX%" goto :missing_sx
if not exist "%SYS%" goto :missing_sys
if not exist "%CORE%" goto :missing_core
if not exist "%XML%" goto :missing_xml
if not exist "%WEBEXT%" goto :missing_webext
if not exist "%SEC%" goto :missing_sec

echo.
echo Building DJ Library Native v0.4.0...
echo Compiler: %CSC%
echo.

"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /win32icon:"DJLibrary.ico" /out:"DJLibrary.exe" ^
  /reference:"%PF%" ^
  /reference:"%PC%" ^
  /reference:"%WB%" ^
  /reference:"%SX%" ^
  /reference:"%SYS%" ^
  /reference:"%CORE%" ^
  /reference:"%XML%" ^
  /reference:"%WEBEXT%" ^
  /reference:"%SEC%" ^
  "src\BuildInfo.cs" ^
  "src\CdxCompatibility.cs" ^
  "src\DiscPlaybackRuleWindow.cs" ^
  "src\Models.cs" ^
  "src\CatalogNativeModels.cs" ^
  "src\DataStore.cs" ^
  "src\DataStore.Catalog.cs" ^
  "src\BridgeSnapshot.cs" ^
  "src\MetadataNormalizerNative.cs" ^
  "src\MetadataNormalizerAnalysis.cs" ^
  "src\MetadataNormalizerPreview.cs" ^
  "src\MetadataNormalizerPreviewWindow.cs" ^
  "src\CdMetadataFetchOptions.cs" ^
  "src\Settings.cs" ^
  "src\WindowGeometrySettings.cs" ^
  "src\GridLayoutSettings.cs" ^
  "src\FieldSchema.cs" ^
  "src\HorizontalScrollSupport.cs" ^
  "src\UiHelpers.cs" ^
  "src\GridGovernance.cs" ^
  "src\WorkspaceManager.cs" ^
  "src\GridRuntimeSupport.cs" ^
  "src\UiRuntimeSelfTest.cs" ^
  "src\ColumnsWindow.cs" ^
  "src\DetailsWindows.cs" ^
  "src\MainWindow.cs" ^
  "src\MainWindow.CatalogWorkspace.cs" ^
  "src\WinSqlite.cs" ^
  "src\CatalogMultiDiscCompatibility.cs" ^
  "src\CatalogModels.cs" ^
  "src\CatalogService.cs" ^
  "src\CatalogService.NativeProjection.cs" ^
  "src\WindowsCdDrive.cs" ^
  "src\WindowsCdTextFallback.cs" ^
  "src\CdMetadataNetwork.cs" ^
  "src\CdMetadataSources.cs" ^
  "src\CdMetadataPipeline.Options.cs" ^
  "src\DiscogsIndependentMetadataSource.cs" ^
  "src\DiscogsCredentials.cs" ^
  "src\CdMetadataPostProcessor.cs" ^
  "src\CdMetadataPostProcessor.Options.cs" ^
  "src\CdMetadataSelectionModels.cs" ^
  "src\CdMetadataSelection.cs" ^
  "src\CdMetadataSelection.Extensions.cs" ^
  "src\CdMetadataSourcePickerDialog.cs" ^
  "src\UnifiedEditors.cs" ^
  "src\CdMetadataChoiceDialog.cs" ^
  "src\CdCapturePreviewDialog.cs" ^
  "src\CatalogSelfTest.cs" ^
  "src\CatalogRuntimeSelfTest.cs" ^
  "src\CatalogWindow.cs" ^
  "src\CatalogWindow.Workspace.cs" ^
  "src\Program.cs"

if errorlevel 1 (
  echo.
  echo ERROR: The native app could not be compiled.
  exit /b 1
)

echo.
echo Build successful: %CD%\DJLibrary.exe
echo.
exit /b 0

:missing_pf
echo ERROR: PresentationFramework.dll was not found: %PF%
goto :missing
:missing_pc
echo ERROR: PresentationCore.dll was not found: %PC%
goto :missing
:missing_wb
echo ERROR: WindowsBase.dll was not found: %WB%
goto :missing
:missing_sx
echo ERROR: System.Xaml.dll was not found: %SX%
goto :missing
:missing_sys
echo ERROR: System.dll was not found: %SYS%
goto :missing
:missing_core
echo ERROR: System.Core.dll was not found: %CORE%
goto :missing
:missing_xml
echo ERROR: System.Xml.dll was not found: %XML%
goto :missing
:missing_webext
echo ERROR: System.Web.Extensions.dll was not found: %WEBEXT%
goto :missing
:missing_sec
echo ERROR: System.Security.dll was not found: %SEC%
goto :missing
:missing
exit /b 1
