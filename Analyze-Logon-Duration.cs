// TARGET:dummy.exe
// START_IN:

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using LoginPI.Engine.ScriptBase;

public class LogonPhases : ScriptBase
{
    public void Execute()
    {
        var cupPs1 = @"C:\Scripts\Analyze-Logon-Duration.ps1";  // ControlUp script path
        var user   = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var sessId = Process.GetCurrentProcess().SessionId;

        if (!File.Exists(cupPs1))
        {
            Console.WriteLine("ERROR: CUP script not found: " + cupPs1);
            SetTimer("LogonAnalysis_Failed", 0);
            return;
        }

        // Prefer x64 PowerShell
        var psExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                 @"SysNative\WindowsPowerShell\v1.0\powershell.exe");
        if (!File.Exists(psExe))
            psExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                 @"System32\WindowsPowerShell\v1.0\powershell.exe");

        // Allow CUP warnings/errors, force formatted output
        var cmd = $@"
$ErrorActionPreference='Continue';
& '{cupPs1.Replace("'", "''")}' -DomainUser '{user.Replace("'", "''")}' | Out-String -Width 400
";

        var (code, stdout, stderr) = RunPS(psExe, cmd);

        // Print stderr for visibility
        if (!string.IsNullOrWhiteSpace(stderr))
            Console.WriteLine("CUP stderr (non-fatal):\n" + stderr.Trim());

        var text = stdout;
        if (string.IsNullOrWhiteSpace(text))
        {
            Console.WriteLine("No CUP output captured on stdout.");
            SetTimer("LogonAnalysis_Failed", 0);
            return;
        }

        Console.WriteLine(text); // Show raw CUP output in LE debug console

        int timers = ParsePhases(text);

        if (timers == 0)
        {
            Console.WriteLine("No timers parsed, flagging failure.");
            SetTimer("LogonAnalysis_Failed", 0);
        }
        else
        {
            Console.WriteLine($"Finished: emitted {timers} timers.");
        }
    }

    // ---- PowerShell runner ----
    private static (int code, string stdout, string stderr) RunPS(string psExe, string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = psExe,
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "`\"") + "\"",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        var outSb = new StringBuilder();
        var errSb = new StringBuilder();
        p.OutputDataReceived += (s, e) => { if (e.Data != null) outSb.AppendLine(e.Data); };
        p.ErrorDataReceived  += (s, e) => { if (e.Data != null) errSb.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit();
        return (p.ExitCode, outSb.ToString(), errSb.ToString());
    }

    // ---- Phase parser ----
    private int ParsePhases(string text)
    {
        // Narrow scope: only parse from the phase table section onward
        var tableStart = text.IndexOf("Source  Phase", StringComparison.OrdinalIgnoreCase);
        if (tableStart < 0)
            return 0;

        var tableText = text.Substring(tableStart);

        // Regex for CUP logon phase table rows
        var rowRegex = new Regex(
            @"^(?<source>\S+)?\s+(?<phase>[A-Za-z][A-Za-z0-9 \-:]+?)\s+(?<duration>\d+(\.\d+)?)\s",
            RegexOptions.Multiline);

        var labelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Windows Logon Time", "WinLogon" },
            { "User Profile",       "UserProfile" },
            { "Group Policy",       "GroupPolicy" },
            { "ActiveSetup",        "ActiveSetup" },
            { "Windows Duration",   "WinDuration" }
        };

        int count = 0;
        foreach (Match m in rowRegex.Matches(tableText))
        {
            var phase = m.Groups["phase"].Value.Trim();
            var seconds = double.TryParse(m.Groups["duration"].Value, out var s) ? s : 0;
            var ms = (int)(seconds * 1000);

            string timerName;
            if (labelMap.ContainsKey(phase))
            {
                timerName = labelMap[phase];
            }
            else
            {
                // Safe name: underscores only, strip junk, max 32 chars
                timerName = Regex.Replace(phase, @"[\s\-:]+", "_");
                timerName = Regex.Replace(timerName, @"[^A-Za-z0-9_]", "");
                if (string.IsNullOrWhiteSpace(timerName))
                    continue;
                if (timerName.Length > 32)
                    timerName = timerName.Substring(0, 32);
            }

            SetTimer(timerName, ms);
            Console.WriteLine($"SetTimer('{timerName}', {ms})");
            count++;
        }
        return count;
    }
}
