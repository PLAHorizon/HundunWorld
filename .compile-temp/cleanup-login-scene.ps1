# 一次性清理：从 Login.scene 移除 PrefabFactoryCanvas(UICanvas) 与其子 Canvas UIControl 残留块
$path = "c:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Maps\Login.scene"
$c = [IO.File]::ReadAllText($path)

function Find-BlockBounds([string]$text, [string]$idFragment) {
    $i = $text.IndexOf($idFragment)
    if ($i -lt 0) { return $null }
    # 向前找块起始 '{'
    $start = $text.LastIndexOf('{', $i)
    # 向后做花括号配对
    $depth = 0; $j = $start
    while ($j -lt $text.Length) {
        $ch = $text[$j]
        if ($ch -eq '{') { $depth++ }
        elseif ($ch -eq '}') { $depth--; if ($depth -eq 0) { break } }
        elseif ($ch -eq '"') { # 跳过字符串内容
            $j++
            while ($j -lt $text.Length -and $text[$j] -ne '"') { if ($text[$j] -eq '\') { $j++ }; $j++ }
        }
        $j++
    }
    if ($depth -ne 0) { throw "brace mismatch near $idFragment" }
    return @($start, $j)
}

foreach ($id in @('6cfedd0545452ca85f073d9348d20e7b', 'd9b65c354504c4f754e92a97132d9b57')) {
    $b = Find-BlockBounds $c ('"ID": "' + $id)
    if ($null -eq $b) { Write-Host "block $id not found"; continue }
    $s = $b[0]; $e = $b[1]
    # 吞掉块后紧跟的逗号与空白
    $tail = $e + 1
    while ($tail -lt $c.Length -and ($c[$tail] -eq ' ' -or $c[$tail] -eq "`t" -or $c[$tail] -eq "`r" -or $c[$tail] -eq "`n")) { $tail++ }
    if ($tail -lt $c.Length -and $c[$tail] -eq ',') { $tail++ }
    # 吞掉块前的行首空白与换行
    $head = $s
    while ($head -gt 0 -and ($c[$head-1] -eq ' ' -or $c[$head-1] -eq "`t")) { $head-- }
    if ($head -gt 1 -and $c[$head-1] -eq "`n") { $head-- }
    if ($head -gt 2 -and $c[$head-1] -eq "`r") { $head-- }
    $c = $c.Remove($head, $tail - $head)
    Write-Host ("removed block $id (chars " + ($tail - $head) + ")")
}

[IO.File]::WriteAllText($path, $c)
Write-Host "done"
