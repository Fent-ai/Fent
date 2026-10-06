# ECU Manager Sidecar, step 1: live capture (read-only)

This opens Haltech ECU Manager 1.14.0 with a sidecar window docked to its right. The sidecar lists the live sensor channels ECU Manager receives from your Platinum Pro, shows their values, and records sessions to CSV files.

It never writes to the ECU or to your map, and it doesn't modify any Haltech files. Delete the two files below and everything is back to normal.

## Build it on GitHub (no Visual Studio needed)

1. Create a free account at github.com, then click **New repository**. Name it something like `ecumanager-sidecar` and make it **Public**.
2. On the new repository's page, click **uploading an existing file**. Drag in everything inside this `sidecar` folder, including the `.github` folder, and click **Commit changes**.
3. Open the **Actions** tab. The build takes about two or three minutes. A green tick means it worked; a red cross means it failed, so click it and copy the error text to Claude.
4. Open **Releases** on the right side of the repository page and download `ECUManagerSidecar.zip`. It contains the two files you need for **Install it** below.

Only upload this sidecar code. Don't upload Haltech's files, your tune files, or (later) your Claude API key.

## Or build it with Visual Studio

1. Install **Visual Studio 2022 Community** (free) and tick the **.NET desktop development** workload during setup.
2. Unzip this folder, then double-click `ECUManagerSidecar.csproj` to open it in Visual Studio.
3. At the top, set the build configuration to **Release**, then choose **Build → Build Solution**.
4. The finished files are in `bin\Release\net48\`:
   - `ECUManagerSidecar.exe`
   - `ECUManagerSidecar.exe.config`

## Install it

1. Close ECU Manager if it's open. (It only allows one copy to run.)
2. Find ECU Manager's folder: right-click your ECU Manager shortcut and choose **Open file location**. It's the folder containing `ECUManager.exe`.
3. Copy both files from step 4 above into that folder. Windows may ask for administrator permission.
4. Make a desktop shortcut to `ECUManagerSidecar.exe` and use that to open ECU Manager from now on.

## Test it (no engine running needed)

You can test this on the car as it is now, with the key on and the engine off.

1. Start `ECUManagerSidecar.exe`. ECU Manager opens as usual, and the sidecar window appears on its right.
2. Connect to the ECU as you normally would.
3. The sidecar should say **Connected to ECU** and fill with channels. Values like battery voltage and coolant temperature should change, and throttle position should follow the pedal.
4. Click **Start recording**, press the throttle slowly to the floor and back a few times, then click **Stop recording**.
5. Click **Open folder**. Please send me:
   - `channels.txt` (every channel your ECU reports)
   - the `session_....csv` file you just recorded
   - a note of what ECU Manager's own gauges showed for a few channels (for example, coolant temperature and battery voltage), so the raw numbers can be converted into real units.

## Good to know

- ECU Manager's preferences (window layout, recent files, language) are stored per program, so when it's opened through the sidecar it may ask for the language again and start with default window positions. Your maps and settings in the ECU are unaffected.
- Watching more channels means each one updates less often, because they share the same USB connection. The default list covers what tuning needs.
- If something goes wrong, details are saved to `Documents\ECU Manager Sidecar\sidecar-error.txt`, and ECU Manager keeps running without the sidecar.

## What comes next

- Step 1b: convert raw values to real units, then add the local safety watchdog (lean under boost, knock, overboost, temperatures) and the map coverage view.
- Step 2: the Claude panel. Run reviews, questions, and next-run suggestions, using your own API key.
- Step 3: fuel correction proposals, then the optional autonomous mode with the limits we discussed.
