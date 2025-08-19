// TARGET:dummy.exe
// START_IN:

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LoginPI.Engine.ScriptBase;

public class SimpleInitializationTest : ScriptBase
{
    public void Execute()
    {
        var cupPs1 = @"C:\Scripts\GPOScriptCUP.ps1";  // ControlUp script path
        var user   = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var sessId = Process.GetCurrentProcess().SessionId;

        if (!File.Exists(cupPs1))
        {
            Console.WriteLine("ERROR: CUP script not found: " + cupPs1);
            return;
        }

        // Prefer x64 PowerShell (covers 32-bit runner)
        var psExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                 @"SysNative\WindowsPowerShell\v1.0\powershell.exe");
        if (!File.Exists(psExe))
            psExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                 @"System32\WindowsPowerShell\v1.0\powershell.exe");

        // 1) Try stdout capture (script uses Write-Output + Out-String)
        var cmd1 = $@"
$ErrorActionPreference='Stop';
& '{cupPs1.Replace("'", "''")}' -User '{user.Replace("'", "''")}' -SessionId {sessId} | Out-String -Width 400
";
        var (code1, out1, err1) = RunPS(psExe, cmd1);
        if (!string.IsNullOrWhiteSpace(err1))
            Console.WriteLine("CUP stderr:\n" + err1.Trim());

        string text = out1;

        // 2) Fallback to transcript if stdout is empty
        if (string.IsNullOrWhiteSpace(text))
        {
            var transcript = Path.Combine(Path.GetTempPath(), $"cup-gpo-{Guid.NewGuid():N}.log");
            var cmd2 = $@"
$ErrorActionPreference='Stop';
$trans='{transcript.Replace("'", "''")}';
Start-Transcript -Path $trans -Force | Out-Null;
try {{
  & '{cupPs1.Replace("'", "''")}' -User '{user.Replace("'", "''")}' -SessionId {sessId}
}} finally {{
  Stop-Transcript | Out-Null;
}}
Get-Content -Path $trans -Raw
";
            var (code2, out2, err2) = RunPS(psExe, cmd2);
            if (!string.IsNullOrWhiteSpace(err2))
                Console.WriteLine("CUP stderr (transcript run):\n" + err2.Trim());
            text = StripTranscriptBoilerplate(out2);
            try { File.Delete(transcript); } catch {}
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            Console.WriteLine("No CUP output captured.");
            return;
        }

        // Parse & emit timers (User scope)
        int timers = 0;
        timers += ParseInit(text,  "User");
        timers += ParseBreakdown(text, "User");
        timers += ParseTotals(text, "User");

        Console.WriteLine($"Finished: emitted {timers} timers.");
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

    private static string StripTranscriptBoilerplate(string s)
    {
        var t = Regex.Replace(s ?? "", @"(?m)^\*{5,}.*\r?\n?", "");
        t = Regex.Replace(t, @"(?mi)^\s*Transcript (started|stopped).*\r?\n?", "");
        return t.Trim();
    }

    // ---- Parsers wired to SHORT names (<= 32 chars) ----

    // Section: "GPP Initialization Duration"
    // Rows:  Time | Duration (ms) | GPExtension
    private int ParseInit(string text, string policyType)
    {
        var block = ExtractAfterTitle(text, "GPP Initialization Duration");
        if (block.Length == 0) return 0;

        int count = 0;
        foreach (Match m in Regex.Matches(block,
            @"(?m)^\s*\d{1,2}/\d{1,2}/\d{4}\s+\d{1,2}:\d{2}:\d{2}\s*(AM|PM)?\s+(?<ms>\d+)\s+(?<ext>.+?)\s*$"))
        {
            var ms  = SafeInt(m.Groups["ms"].Value);
            var ext = m.Groups["ext"].Value.Trim();
            var timerName = BuildInitTimerName(policyType, ext); // e.g., U_INIT_GPDM
            SetTimer(timerName, ms);
            Console.WriteLine($"SetTimer('{timerName}', {ms})");
            count++;
        }
        return count;
    }

    // Section: "GPO Processing Breakdown per GPExtension"
    // Rows:  Time | Duration (ms) | GPO | GPExtension
    private int ParseBreakdown(string text, string policyType)
    {
        var block = ExtractAfterTitle(text, "GPO Processing Breakdown per GPExtension");
        if (block.Length == 0) return 0;

        int count = 0;
        foreach (Match m in Regex.Matches(block,
            @"(?m)^\s*\d{1,2}/\d{1,2}/\d{4}\s+\d{1,2}:\d{2}:\d{2}\s*(AM|PM)?\s+(?<ms>\d+)\s+(?<gpo>.+?)\s+(?<ext>.+?)\s*$"))
        {
            var ms  = SafeInt(m.Groups["ms"].Value);
            var gpo = m.Groups["gpo"].Value.Trim();
            var ext = m.Groups["ext"].Value.Trim();
            var timerName = BuildPerExtTimerName(policyType, gpo, ext); // e.g., U_TestGPLogging_GPDM
            SetTimer(timerName, ms);
            Console.WriteLine($"SetTimer('{timerName}', {ms})");
            count++;
        }
        return count;
    }

    // Section: "GPO Total Processing Duration"
    // Rows:  Duration (ms) | GPO | GPExtensions
    private int ParseTotals(string text, string policyType)
    {
        var block = ExtractAfterTitle(text, "GPO Total Processing Duration");
        if (block.Length == 0) return 0;

        int count = 0;
        foreach (Match m in Regex.Matches(block,
            @"(?m)^\s*(?<ms>\d+)\s+(?<gpo>.+?)\s+.+?$"))
        {
            var ms  = SafeInt(m.Groups["ms"].Value);
            var gpo = m.Groups["gpo"].Value.Trim();
            var timerName = BuildTotalTimerName(policyType, gpo); // e.g., U_TestGPLogging_TOT
            SetTimer(timerName, ms);
            Console.WriteLine($"SetTimer('{timerName}', {ms})");
            count++;
        }
        return count;
    }

    // ---- Section extractor & sanitization ----

    private static string ExtractAfterTitle(string text, string title)
    {
        int i = text.IndexOf(title, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return string.Empty;
        var tail = text.Substring(i + title.Length);
        var m = Regex.Match(tail, @"(?s)^\s*\r?\n(?<body>.*?)(?:\r?\n{2,}(?=\S)|\z)");
        return m.Success ? m.Groups["body"].Value : tail;
    }

    private static string SanitizeToken(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "Unknown";
        var t = s.Trim();
        t = Regex.Replace(t, @"\s+", "_");           // spaces -> underscore
        t = Regex.Replace(t, @"[^A-Za-z0-9_\-]", ""); // strip non-safe
        return t;
    }

    private static int SafeInt(string s) => int.TryParse(s.Trim(), out var v) ? v : 0;

    // ---- Naming helpers (≤ 32 chars) ----

    // Policy prefix: "U_" or "C_"
    private static string PolicyPrefix(string policyType) =>
        string.Equals(policyType, "Computer", StringComparison.OrdinalIgnoreCase) ? "C_" : "U_";

    // EXT acronym from first letters of words (Group Policy Drive Maps -> GPDM)
    private static string ExtAcronym(string extension)
    {
        var words = Regex.Split(extension.Trim(), @"\s+")
                         .Where(w => !string.IsNullOrWhiteSpace(w));
        var letters = words.Select(w => char.ToUpperInvariant(w[0]));
        var acro = new string(letters.ToArray());
        // Edge case: single token without spaces — cap at 6 chars
        if (acro.Length == 1 && extension.Length > 6)
            acro = SanitizeToken(extension).Substring(0, Math.Min(6, SanitizeToken(extension).Length)).ToUpperInvariant();
        return acro.Length == 0 ? "EXT" : acro;
    }

    // Shorten GPO but keep it readable; sanitize and trim on demand
    private static string ShortGpo(string gpo, int maxLen)
    {
        var safe = SanitizeToken(gpo);
        if (safe.Length <= maxLen) return safe;
        // Prefer acronym + tail: take first letters + last few chars to keep uniqueness
        var acro = new string(Regex.Split(safe, "_+").Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0])).ToArray());
        if (acro.Length >= 3 && acro.Length <= maxLen) return acro;
        return safe.Substring(0, maxLen); // fallback hard trim
    }

    // Build: U_INIT_<EXT>  (<=32)
    private static string BuildInitTimerName(string policyType, string extension)
    {
        var prefix = PolicyPrefix(policyType);                     // "U_" or "C_"
        var ext    = ExtAcronym(extension);                        // e.g., GPDM
        var name   = $"{prefix}INIT_{ext}";
        return name.Length <= 32 ? name : name.Substring(0, 32);   // should rarely trim
    }

    // Build: U_<GPO>_<EXT>  (<=32)
    private static string BuildPerExtTimerName(string policyType, string gpo, string extension)
    {
        var prefix = PolicyPrefix(policyType);   // "U_" or "C_"
        var ext    = ExtAcronym(extension);      // e.g., GPDM
        // Reserve space: prefix(2) + "_" + "_" + ext(len)
        var reserved = prefix.Length + 1 + 1 + ext.Length;
        var maxGpo   = Math.Max(0, 32 - reserved);
        var gpoShort = ShortGpo(gpo, maxGpo);
        var name     = $"{prefix}{gpoShort}_{ext}";
        return name.Length <= 32 ? name : name.Substring(0, 32);
    }

    // Build: U_<GPO>_TOT  (<=32)
    private static string BuildTotalTimerName(string policyType, string gpo)
    {
        var prefix = PolicyPrefix(policyType); // "U_" or "C_"
        const string suffix = "_TOT";
        var reserved = prefix.Length + suffix.Length;
        var maxGpo   = Math.Max(0, 32 - reserved);
        var gpoShort = ShortGpo(gpo, maxGpo);
        var name     = $"{prefix}{gpoShort}{suffix}";
        return name.Length <= 32 ? name : name.Substring(0, 32);
    }
}
