@echo off
rem ==========================================================================
rem HarmonyOS ArkTS Project - hvigorw wrapper
rem Calls DevEco Studio built-in hvigor with correct environment
rem ==========================================================================

set HVIGOR_HOME=C:\ProgramData\Huawei\DevEco Studio\tools\hvigor
set NODE_HOME=C:\ProgramData\Huawei\DevEco Studio\tools\node
set DEVECO_SDK_HOME=C:\ProgramData\Huawei\DevEco Studio\sdk
set JAVA_HOME=C:\Program Files\Android\openjdk\jdk-21.0.8
set PATH=%JAVA_HOME%\bin;%PATH%

call "%HVIGOR_HOME%\bin\hvigorw.bat" %*
