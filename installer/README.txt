Asus Fan Control
================

Start
-----
Installed with Setup:
  Use the "Asus Fan Control" shortcut in the Start menu and allow the
  administrator prompt.

From the zip:
  1. Unzip this folder somewhere permanent (for example C:\Asus Fan Control).
  2. Double-click run.bat and allow the administrator prompt.
     (Or right-click AsusFanControlGUI.exe > Run as administrator.
     Some laptops only allow fan control through run.bat, which uses PsExec.)

Then tick "Control fans" and choose Auto (fan curve) or Manual.

Start with Windows
------------------
Settings > Start with Windows (Setup offers it too). The app then starts in
the notification area when you sign in, also on battery, without a console
window. If you use the zip, keep the folder where it is afterwards, or turn
the option off and on again after moving it.

Good to know
------------
- 0% stops the fans. Untick "Control fans" to hand them back to the BIOS.
- Profiles: Balanced, Quiet and Performance are included. "New" copies the
  current profile so you can edit it.
- Edit a curve on the graph: drag a point (left/right for temperature,
  up/down for fan speed), double-click to add a point, double-click a point
  to remove it, then click Save. Cancel or Esc discards the changes.
- Calibrate RPM (under the graph) measures your fans so the graph shows RPM.
- Themes: the gear button at the top right > Theme.
- Settings and Help are the gear and ? buttons at the top right of the window.
- Sleep: Settings > Hand fans to the BIOS during sleep (on by default).
- Settings and the log are in C:\ProgramData\AsusFanControl
  (Settings > Open settings and log folder). Your settings stay when you
  update to a new release. If something goes wrong, please attach log.txt
  to your report.
- Uninstall from Windows Settings > Apps. It closes the app (fans go back to
  the BIOS), removes Start with Windows, and asks whether to keep your
  settings.

Requirements
------------
- 64-bit Windows 10 or 11 with .NET Framework 4.7.2 or later (included in
  current Windows 10 and 11)
- The "ASUS System Analysis" service (installed with the MyASUS app)

Files
-----
AsusFanControlGUI.exe (+ .exe.config)  the app
AsusFanControl.exe (+ .exe.config)     command-line tool
AsusWinIO64.dll                        ASUS fan library (c) ASUSTek COMPUTER INC.
run.bat, PsExec.exe                    start the app as SYSTEM (PsExec by Sysinternals)
