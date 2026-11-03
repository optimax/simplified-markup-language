robocopy "D:\PROJECTS\SML\Tests\svg-corpus" "D:\PROJECTS\SML\TMP" *.svg /S
D:\PROJECTS\SML\publish\smlxml\win-x64\smlxml.exe --verbose -f --reflow --profile svg "D:\Projects\SML\TMP\**"
timeout 10
