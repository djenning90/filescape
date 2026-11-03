#!/usr/bin/env bash
# Repeated uncapped perf runs of the exported build, comparing Vulkan vs D3D12 stability (KD-01). Prints exit codes; 0 = clean.
# Usage (WSL): tools/compare-drivers.sh   (takes ~9 minutes; builds nothing, run tools/fs.sh export first)
cd /mnt/c
for i in 1 2 3; do
  for drv in vulkan d3d12; do
    out="C:/Users/David/AppData/Local/FileScape/evidence/stability/$drv-$i"
    code=$(timeout 400 powershell.exe -NoProfile -Command "\$p = Start-Process \"\$env:LOCALAPPDATA\FileScape\build\FileScape.exe\" -ArgumentList '--rendering-driver','$drv','--','--automate=perf','--synthetic=50000','--out=$out','--window=1920x1080','--ui-scale=1' -PassThru; if (-not \$p.WaitForExit(380000)) { \$p.Kill(); 'timeout' } else { \$p.ExitCode }" | tr -d '\r')
    echo "$(date +%H:%M:%S) $drv run $i exit=$code"
  done
done
