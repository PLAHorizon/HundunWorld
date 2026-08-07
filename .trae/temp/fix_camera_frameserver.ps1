# 禁用 Windows 相机帧服务器（修复 MF_E_HW_MFT_FAILED_START_STREAMING 导致摄像头无法出帧）
# 可逆：删除 EnableFrameServerMode 值即可恢复默认行为
$key = 'HKLM:\SOFTWARE\Microsoft\Windows Media Foundation\Platform'
New-Item -Path $key -Force | Out-Null
Set-ItemProperty -Path $key -Name 'EnableFrameServerMode' -Value 0 -Type DWord
$val = (Get-ItemProperty $key).EnableFrameServerMode
Write-Host "EnableFrameServerMode = $val (0 = 已禁用帧服务器)"
Start-Sleep -Seconds 3
