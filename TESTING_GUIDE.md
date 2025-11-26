# Testing Guide - Blackhorse311-KeepStartingGear

## Test #1: Basic Mod Loading & Configuration

### Purpose
Verify that the mod loads into SPT without errors and initializes correctly.

---

## Pre-Test Setup

### 1. Build the Mod (if not already done)
```bash
cd I:\spt-dev\Blackhorse311.KeepStartingGear4\Blackhorse311.KeepStartingGear\src\server
dotnet build
```

### 2. Install to SPT
Copy the built files to your SPT installation:

```bash
# Create mod directory
mkdir "H:\SPT\SPT\user\mods\Blackhorse311-KeepStartingGear"

# Copy DLL
cp "I:\spt-dev\Blackhorse311.KeepStartingGear4\Blackhorse311.KeepStartingGear\bin\server\Blackhorse311.KeepStartingGear.dll" "H:\SPT\SPT\user\mods\Blackhorse311-KeepStartingGear\"

# Copy config
mkdir "H:\SPT\SPT\user\mods\Blackhorse311-KeepStartingGear\config"
cp "I:\spt-dev\Blackhorse311.KeepStartingGear4\Blackhorse311.KeepStartingGear\bin\server\config\config.json" "H:\SPT\SPT\user\mods\Blackhorse311-KeepStartingGear\config\"
```

---

## Test Procedure

### Step 1: Start SPT Server
1. Navigate to: `H:\SPT\SPT`
2. Run: `SPT.Server.exe`
3. Watch the console output

### Step 2: Check for Mod Loading Messages
Look for these messages in the console:

✅ **Expected Output:**
```
[Blackhorse311-KeepStartingGear] Version 1.0.0 loading...
[Blackhorse311-KeepStartingGear] Mod path: H:\SPT\SPT\user\mods\Blackhorse311-KeepStartingGear
[Blackhorse311-KeepStartingGear] Configuration loaded successfully
[Blackhorse311-KeepStartingGear] Mod enabled: True
[Blackhorse311-KeepStartingGear] Debug mode: False
[Blackhorse311-KeepStartingGear] Snapshot service initialized
[Blackhorse311-KeepStartingGear] Created snapshot directory: H:\SPT\SPT\user\mods\Blackhorse311-KeepStartingGear\snapshots
[Blackhorse311-KeepStartingGear] Version 1.0.0 loaded successfully!
[Blackhorse311-KeepStartingGear] Core services initialized. Waiting for raid events...
```

### Step 3: Verify Snapshot Directory Created
Check that this folder was created:
```
H:\SPT\SPT\user\mods\Blackhorse311-KeepStartingGear\snapshots\
```

---

## Expected Results

### ✅ Success Criteria
- [ ] SPT server starts without errors
- [ ] Mod loading messages appear in console
- [ ] Configuration file is loaded successfully
- [ ] Snapshot directory is created
- [ ] No error messages or exceptions

### ❌ Failure Indicators
- Red error messages in console
- SPT server crashes or fails to start
- Missing mod loading messages
- Exception stack traces

---

## Troubleshooting

### Problem: Mod doesn't load at all
**Check:**
- DLL is in correct location: `H:\SPT\SPT\user\mods\Blackhorse311-KeepStartingGear\Blackhorse311.KeepStartingGear.dll`
- DLL was built for correct .NET version (net9.0)

### Problem: "config.json not found" warning
**Check:**
- Config file location: `H:\SPT\SPT\user\mods\Blackhorse311-KeepStartingGear\config\config.json`
- **Note:** This is OK! Mod will use defaults if config is missing

### Problem: Errors during load
**Action:**
- Copy the full error message
- Check the stack trace
- Report back for debugging

---

## Test Results Log

**Date:** _____________
**SPT Version:** 4.0.6
**Mod Version:** 1.0.0-dev

### Checklist
- [ ] Server started successfully
- [ ] Mod loaded without errors
- [ ] Configuration loaded: YES / NO / DEFAULT
- [ ] Snapshot directory created: YES / NO
- [ ] Console output matches expected: YES / NO

### Notes:
```
(Write any observations, errors, or unexpected behavior here)
```

---

## What This Test Validates

✅ **Proven Working:**
- C# compilation and .NET 9 compatibility
- SPT 4.0.6 mod loading system
- Configuration deserialization
- File system access (snapshot directory creation)
- Basic mod infrastructure

❌ **Not Yet Tested:**
- Snapshot save/load functionality (no raid events yet)
- Inventory restoration (no InraidController hooks yet)
- Client-side keybind detection (not implemented)
- Toast notifications (not implemented)

---

## Next Steps After Successful Test

Once this test passes, we'll continue with:
1. Hooking into InraidController for raid exit events
2. Implementing actual inventory snapshot capture
3. Implementing inventory restoration on death
4. Testing with actual raids

---

## Quick Copy-Paste Install Script

For easy installation, run this in PowerShell:

```powershell
# Build
cd "I:\spt-dev\Blackhorse311.KeepStartingGear4\Blackhorse311.KeepStartingGear\src\server"
dotnet build

# Install
$modDir = "H:\SPT\SPT\user\mods\Blackhorse311-KeepStartingGear"
New-Item -ItemType Directory -Force -Path $modDir
New-Item -ItemType Directory -Force -Path "$modDir\config"

Copy-Item "..\..\bin\server\Blackhorse311.KeepStartingGear.dll" -Destination $modDir
Copy-Item "..\..\bin\server\config\config.json" -Destination "$modDir\config\"

Write-Host "Mod installed to: $modDir" -ForegroundColor Green
```
