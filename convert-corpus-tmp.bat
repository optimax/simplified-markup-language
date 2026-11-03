robocopy "D:\PROJECTS\SML\Tests\svg-corpus" "D:\PROJECTS\SML\TMP" *.svg /S
D:\PROJECTS\SML\publish\smlxml\win-x64\smlxml.exe --verbose -f --clean svg "D:\Projects\SML\TMP\**\*.svg"
timeout 10
