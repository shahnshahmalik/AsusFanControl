# Asus Fan Control

### Download
Go to [releases](../../releases)

### Run

<details>
    <summary>Command line: `AsusFanControl.exe`</summary>
    
    AsusFanControl.exe <args>
        --get-fan-speeds
        --set-fan-speeds=0-100 (percent value, 0 for turning off test mode)
        --get-fan-count
        --get-fan-speed=fanId (comma separated)
        --set-fan-speed=fanId:0-100 (comma separated, percent value, 0 for turning off test mode)
        --get-cpu-temp
</details>

GUI: `AsusFanControlGUI.exe`  

After building (e.g. Release | x64), the output folder `bin\x64\Release\` contains the exes and `run.bat`. Double‑click `run.bat` to launch the GUI with admin rights (it uses PsExec if present). To have PsExec copied automatically, place `PsExec.exe` in the `AsusFanControlGUI` project folder once; then every build will copy it to the output folder.

![AsusFanControlGUI](https://github.com/Karmel0x/AsusFanControl/assets/25367564/fe197ad0-7079-4d51-ae78-177cb6369e96)

### Why need it?
My laptop does not support the [Fan Profile](https://github.com/Karmel0x/AsusFanControl/assets/25367564/924d990a-bf20-4b8d-bf9d-56c460174d99) option, but it often overheats. Looked for apps to control fans, but none is working.

### Compatibility
This program should work on any laptop with x64 windows where [Fan Diagnosis](https://github.com/Karmel0x/AsusFanControl/assets/25367564/7129833b-97af-4da8-9148-b71e49552ea4) in [MyASUS](https://apps.microsoft.com/store/detail/myasus/9N7R5S6B0ZZH) application is working as it is using same library.

[ASUS System Control Interface](https://www.asus.com/support/faq/1047338/) is necessary for this software to work - `ASUS System Analysis` service [must be running](../../issues/16). It's automatically installed with `MyASUS` app.

**If fan control has no effect:** Run the app **as Administrator** (right‑click → Run as administrator). The GUI is built to request admin elevation. Also ensure the ASUS System Analysis service is running (Services → "ASUS System Analysis" or "AsusSystemAnalysis") and that your model supports [Fan Diagnosis](https://github.com/Karmel0x/AsusFanControl/assets/25367564/7129833b-97af-4da8-9148-b71e49552ea4) in MyASUS. You can test from command line (as admin): `AsusFanControl.exe --get-fan-count` then `AsusFanControl.exe --set-fan-speeds=80`.

Included `AsusWinIO64.dll` is licenced to `(c) ASUSTek COMPUTER INC.` which can be found in `C:\Windows\System32\DriverStore\FileRepository\asussci2.inf_amd64_-\ASUSSystemAnalysis\` if you have MyASUS installed.

[Works on](../../issues/13): 
- ASUS: VivoBook, ZenBook, TUF Gaming, ROG Strix, ROG Zephyrus, ROG Flow

---

### Modifications

This fork adds temperature-based fan control and UI improvements on top of the original Asus Fan Control:

- **Manual vs Auto mode**  
  - **Manual:** Fan speed is set by the trackbar (unchanged).  
  - **Auto:** Fan speed is chosen from fixed temperature ranges; the current CPU temp and resulting fan % are shown in the UI.

- **Fixed temperature ranges (Auto mode)**  
  - &lt; 35°C → 0% (fans off)  
  - 35–55°C → 45%  
  - 55–75°C → 80%  
  - ≥ 75°C → 100%  
  "Forbid unsafe settings" (Advanced menu) still applies a 40–99% clamp when enabled.

- **5-second debounce**  
  In Auto mode, when temperature moves to a new range, the new fan speed is applied only after it has stayed in that range for 5 seconds, to avoid reacting to short spikes.

- **Performance**  
  Hardware reads (CPU temp, fan RPM) run on a background thread and the refresh timer runs every 5 seconds to reduce system load and keep the UI responsive.

- **UI**  
  - Temp ranges are shown on separate lines (no truncation).  
  - Window size is reduced to fit content with less empty space.  
  - Fixed single-window layout; form does not maximize.
