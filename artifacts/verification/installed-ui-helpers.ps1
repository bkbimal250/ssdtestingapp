Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class VerifyWindow {
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int n);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int ht,bool repaint);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [StructLayout(LayoutKind.Sequential)] public struct RECT {public int Left,Top,Right,Bottom;}
}
"@
$p=Get-Process -Id 6388
$h=$p.MainWindowHandle
$w=[Windows.Automation.AutomationElement]::FromHandle($h)
function Find-Control($name,$type) {
 $conditions=[Windows.Automation.AndCondition]::new([Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$name),[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,$type))
 $w.FindFirst([Windows.Automation.TreeScope]::Descendants,$conditions)
}
function Select-Tab($name) { $e=Find-Control $name ([Windows.Automation.ControlType]::TabItem); if(!$e){throw "Missing tab $name"}; ([Windows.Automation.SelectionItemPattern]$e.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)).Select(); Start-Sleep -Milliseconds 250 }
function Capture-Window($name,$width,$height) {
 [VerifyWindow]::ShowWindow($h,9)|Out-Null; [VerifyWindow]::MoveWindow($h,25,25,$width,$height,$true)|Out-Null; [VerifyWindow]::SetForegroundWindow($h)|Out-Null; Start-Sleep -Milliseconds 650
 $rect=[VerifyWindow+RECT]::new(); [VerifyWindow]::GetWindowRect($h,[ref]$rect)|Out-Null
 $bmp=[Drawing.Bitmap]::new($rect.Right-$rect.Left,$rect.Bottom-$rect.Top); $g=[Drawing.Graphics]::FromImage($bmp)
 try {$g.CopyFromScreen($rect.Left,$rect.Top,0,0,$bmp.Size); $bmp.Save((Join-Path (Resolve-Path docs/screenshots) $name),[Drawing.Imaging.ImageFormat]::Png)} finally {$g.Dispose();$bmp.Dispose()}
 "Captured $name : $width x $height; DPI=$([VerifyWindow]::GetDpiForWindow($h))"
}
function Text-State { $w.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Text)) | ForEach-Object {$_.Current.Name} }

