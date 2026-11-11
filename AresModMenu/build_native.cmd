@echo off
setlocal
set ROOT=%~dp0
set NDK_BUILD=%ROOT%..\tools\ndk_extract\android-ndk-r27d\ndk-build.cmd
if not exist "%NDK_BUILD%" (
    echo [ERROR] ndk-build.cmd not found at %NDK_BUILD%
    exit /b 1
)
call "%NDK_BUILD%" NDK_PROJECT_PATH=%ROOT%app\src\main APP_BUILD_SCRIPT=%ROOT%app\src\main\jni\Android.mk NDK_APPLICATION_MK=%ROOT%app\src\main\jni\Application.mk -j8
endlocal
