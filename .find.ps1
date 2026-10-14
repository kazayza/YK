$out = 'd:\YK Coatings\YkWeb\.find.txt'
$roots = @('d:\YK Coatings\YkWeb', $env:USERPROFILE, 'C:\Users\Admin\AppData\Local\Programs\Microsoft VS Code')
$names = @('po-verify.png','po-live.png','po-actual-view.png')
"--- po-verify / recent png search ---" | Out-File $out -Encoding utf8
foreach ($r in $roots) {
  if (Test-Path $r) {
    foreach ($n in $names) {
      Get-ChildItem -Path $r -Filter $n -Recurse -ErrorAction SilentlyContinue -Force |
        Select-Object FullName, Length, LastWriteTime |
        Out-File $out -Append -Encoding utf8
    }
  }
}
"--- user .playwright-mcp ---" | Out-File $out -Append -Encoding utf8
$pmcp = Join-Path $env:USERPROFILE '.playwright-mcp'
if (Test-Path $pmcp) {
  Get-ChildItem $pmcp -Filter *.png -Recurse -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 15 FullName,LastWriteTime |
    Out-File $out -Append -Encoding utf8
} else { "none" | Out-File $out -Append -Encoding utf8 }
"DONE" | Out-File $out -Append -Encoding utf8

