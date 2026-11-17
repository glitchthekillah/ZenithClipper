using System.Diagnostics;

namespace ZenithClipper.Windows;

/// <summary>
/// Portable copies can't overwrite themselves while running, so a short hidden PowerShell script waits for every
/// ZenithClipper process in this folder to exit, unpacks the verified zip over it (keeping data\ and portable.flag)
/// and reopens the panel.
/// </summary>
static class PortableUpdate
{
    const string Script = """
        param([string]$Zip, [string]$Root, [string]$Exe)
        $ErrorActionPreference = 'Stop'
        function Running { Get-Process | Where-Object { try { $_.Path -and $_.Path.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase) } catch { $false } } }
        $deadline = (Get-Date).AddSeconds(30)
        while ((Running) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
        Running | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
        $stage = Join-Path $env:TEMP ('ZenithClipper-stage-' + [guid]::NewGuid())
        try {
            Expand-Archive -LiteralPath $Zip -DestinationPath $stage -Force
            $src = Join-Path $stage 'ZenithClipper'
            foreach ($sub in 'windows', 'editor') {
                robocopy (Join-Path $src $sub) (Join-Path $Root $sub) /MIR /XD data /XF portable.flag /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
                if ($LASTEXITCODE -ge 8) { throw "Copying $sub failed ($LASTEXITCODE)." }
            }
            Get-ChildItem -LiteralPath $src -File | Copy-Item -Destination $Root -Force
        } catch {
            Add-Content -LiteralPath (Join-Path $Root 'windows\data\update.log') -Value ((Get-Date).ToString('s') + ' ' + $_)
        } finally {
            Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $Zip -Force -ErrorAction SilentlyContinue
        }
        Start-Process -FilePath $Exe -ArgumentList 'panel'
        """;

    public static void Start(string zip)
    {
        string root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!;
        string script = Path.Combine(Path.GetTempPath(), "ZenithClipper-portable-update.ps1");
        File.WriteAllText(script, Script);
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script, "-Zip", zip, "-Root", root, "-Exe", Environment.ProcessPath! }) info.ArgumentList.Add(arg);
        using (Process.Start(info)) { }
    }
}
