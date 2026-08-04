#Requires AutoHotkey v2.0
#SingleInstance Force

; Warcraft III / custom-map helper macro template.
; Use only for manual hotkeys you trigger yourself.
; If your game/platform forbids macros, do not use this online.

CoordMode "Mouse", "Screen"
SendMode "Input"
SetKeyDelay 20, 20
SetMouseDelay 30

enabled := true

; Add or change process/window names if your Warcraft build is different.
gameMatchers := [
    "ahk_exe War3.exe",
    "ahk_exe war3.exe",
    "ahk_exe Warcraft III.exe",
    "ahk_exe Warcraft III Launcher.exe",
    "Warcraft III",
    "魔兽争霸"
]

; -----------------------------
; Jiban 1 challenge config
; -----------------------------
; The current application no longer reserves Warcraft camera-save keys.
homeCameraKey := ""
miaomuCameraKey := ""
cameraDelayMs := 180
menuDelayMs := 140
betweenChallengeDelayMs := 220

; If the NPC command card has a hotkey, prefer StepKey("q") / StepKey("w").
; If there is no stable hotkey, replace StepKey(...) with StepClick(buttonX, buttonY).
homeChallengeSteps := [
    StepClick(1000, 500),   ; home challenge NPC position
    StepSleep(menuDelayMs),
    StepKey("q")            ; "challenge self" command hotkey or replace with StepClick(...)
]

miaomuChallengeSteps := [
    StepClick(1000, 500),   ; Mount Myoboku challenge NPC position
    StepSleep(menuDelayMs),
    StepKey("q")            ; "challenge self" command hotkey or replace with StepClick(...)
]

; -----------------------------
; Hotkeys
; -----------------------------
; F12: enable / disable all action macros.
F12::ToggleEnabled()

#HotIf IsGameActive()

; Jiban 1: one manual keypress = one fixed interaction.
F3::RunChallenge(homeCameraKey, homeChallengeSteps)
F4::RunChallenge(miaomuCameraKey, miaomuChallengeSteps)
F7::RunBothChallenges()

; Example 1: click NPC, wait for menu, then click a function button.
; Replace the x/y values with positions captured by F10.
^!1::RunSequence([
    StepClick(1000, 500),   ; NPC position
    StepSleep(120),         ; wait for NPC panel/menu
    StepClick(1420, 780)    ; function button position
])

; Example 2: click NPC, then press a command-card hotkey.
; This is usually more stable than clicking the command button if the map supports it.
^!2::RunSequence([
    StepClick(1000, 500),   ; NPC position
    StepSleep(120),
    StepKey("q")            ; NPC function hotkey, e.g. q/w/e/r/a/s/d/f
])

; Example 3: mouse side button mapped to another NPC function.
^!3::RunSequence([
    StepClick(1000, 500),
    StepSleep(120),
    StepKey("w")
])

; Example 4: click two menu buttons in sequence.
^!4::RunSequence([
    StepClick(1000, 500),
    StepSleep(120),
    StepClick(1420, 780),
    StepSleep(80),
    StepClick(1510, 780)
])

#HotIf

; -----------------------------
; Sequence helpers
; -----------------------------
StepClick(x, y, button := "Left", count := 1) {
    return Map("type", "click", "x", x, "y", y, "button", button, "count", count)
}

StepKey(key) {
    return Map("type", "key", "key", key)
}

StepSleep(ms) {
    return Map("type", "sleep", "ms", ms)
}

RunChallenge(cameraKey, steps) {
    global cameraDelayMs

    if !CanRunMacro() {
        return
    }

    if cameraKey != "" {
        SendKey(cameraKey)
        Sleep cameraDelayMs
    }

    RunSequence(steps, false)
}

RunBothChallenges() {
    global homeCameraKey, homeChallengeSteps
    global miaomuCameraKey, miaomuChallengeSteps
    global betweenChallengeDelayMs

    RunChallenge(homeCameraKey, homeChallengeSteps)
    Sleep betweenChallengeDelayMs
    RunChallenge(miaomuCameraKey, miaomuChallengeSteps)
}

RunSequence(steps, checkReady := true) {
    if checkReady && !CanRunMacro() {
        return
    }

    for step in steps {
        switch step["type"] {
            case "click":
                MouseMove step["x"], step["y"], 0
                Click step["button"] " " step["count"]
            case "key":
                SendKey(step["key"])
            case "sleep":
                Sleep step["ms"]
        }
    }
}

CanRunMacro() {
    global enabled

    if !enabled {
        QuietTip("Macros disabled")
        return false
    }

    if !IsGameActive() {
        QuietTip("Game window not active")
        return false
    }

    return true
}

SendKey(key) {
    if InStr(key, "{") {
        Send key
    } else {
        Send "{" key "}"
    }
}

IsGameActive(*) {
    global gameMatchers

    for matcher in gameMatchers {
        if WinActive(matcher) {
            return true
        }
    }
    return false
}

ToggleEnabled() {
    global enabled
    enabled := !enabled
    QuietTip(enabled ? "Macros enabled" : "Macros disabled")
    SoundBeep enabled ? 1200 : 500, 80
}

CaptureMousePosition() {
    MouseGetPos &x, &y
    text := "StepClick(" x ", " y ")"
    A_Clipboard := text
    QuietTip("Copied: " text)
}

CopyActiveWindowInfo() {
    title := WinGetTitle("A")
    exe := WinGetProcessName("A")
    text := "Title: " title "`nExe: " exe
    A_Clipboard := text
    QuietTip("Copied active window info")
}

QuietTip(text, ms := 900) {
    ToolTip text
    SetTimer () => ToolTip(), -ms
}
