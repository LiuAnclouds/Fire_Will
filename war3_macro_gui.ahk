#Requires AutoHotkey v2.0
#SingleInstance Force

; Warcraft III / 羁绊一 visual macro configurator.
; Scope: manual hotkey-triggered sequences only. The configured stop key stops current sequence.

CoordMode "Mouse", "Screen"
CoordMode "ToolTip", "Screen"
SetTitleMatchMode 2
SendMode "Input"
SetKeyDelay -1, -1
SetMouseDelay -1

appTitle := "羁绊I丶无限月读宏"
appIconPath := A_ScriptDir "\icon.ico"
appIconSmall := 0
appIconLarge := 0
configPath := A_ScriptDir "\war3_macro_gui.ini"
sessionPath := A_ScriptDir "\war3_session.ini"
initializeRequestPath := A_ScriptDir "\war3_initialize.request"
shutdownRequestPath := A_ScriptDir "\war3_shutdown.request"
profileDir := A_ScriptDir "\profiles"
flowCount := 8
groupCount := 8
skillSlotCount := 12
itemSlotCount := 6
globalEnabled := true
isRunning := false
stopRequested := false
runWarning := ""
registeredHotkeys := []
gameWindowMatcher := ""
skipGameCheck := false
defaultStopHotkey := "Z"
stopHotkey := defaultStopHotkey
stopHotkeyLastTick := 0
stopHotkeyDoubleTapMs := 350
pausedStopHotkeyWasDown := false
defaultKeyDelayMs := 40
defaultSkillKeyDelayMs := 100
defaultHeroSelectDelayMs := 80
defaultNpcClickDelayMs := 100
defaultChatDelayMs := 500
defaultTeleportKeyDelayMs := 200
defaultMouseMoveDelayMs := 30
defaultReleaseMouseMoveDelayMs := 80
keyDelayMs := defaultKeyDelayMs
skillKeyDelayMs := defaultSkillKeyDelayMs
heroSelectDelayMs := defaultHeroSelectDelayMs
npcClickDelayMs := defaultNpcClickDelayMs
chatDelayMs := defaultChatDelayMs
teleportKeyDelayMs := defaultTeleportKeyDelayMs
mouseMoveDelayMs := defaultMouseMoveDelayMs
releaseMouseMoveDelayMs := defaultReleaseMouseMoveDelayMs
activeKeyDelayMs := defaultKeyDelayMs
activeSkillKeyDelayMs := defaultSkillKeyDelayMs
activeHeroSelectDelayMs := defaultHeroSelectDelayMs
activeNpcClickDelayMs := defaultNpcClickDelayMs
activeChatDelayMs := defaultChatDelayMs
activeTeleportKeyDelayMs := defaultTeleportKeyDelayMs
activeMouseMoveDelayMs := defaultMouseMoveDelayMs
activeReleaseMouseMoveDelayMs := defaultReleaseMouseMoveDelayMs
currentProfileName := "默认/未读取"
currentProfilePath := ""
currentProfileText := ""

; Per-game values are intentionally transient. NPC world coordinates and the
; projection constants are stable configuration; PID/HWND/client geometry are
; rebuilt once when the user presses "绑定游戏窗口并初始化".
gameSession := Map(
    "bound", false,
    "ready", false,
    "projectionReady", false,
    "hwnd", 0,
    "pid", 0,
    "clientLeft", 0,
    "clientTop", 0,
    "clientWidth", 0,
    "clientHeight", 0,
    "dpi", 96,
    "gameBase", 0,
    "gameModuleName", "",
    "state", "未初始化",
    "message", "请先绑定并初始化游戏窗口。",
    "active", false,
    "cameraX", 0.0,
    "cameraY", 0.0,
    "cameraZoom", 1.0
)
worldProjection := Map(
    "cameraXOffset", "",
    "cameraYOffset", "",
    "cameraZoomOffset", "",
    "cameraXIndirect", 0,
    "cameraYIndirect", 0,
    "cameraZoomIndirect", 0,
    "axisX", 0.70710678,
    "axisY", 0.70710678,
    "verticalScale", 0.5,
    "pixelsPerWorld", "",
    "snapRadius1080", 24,
    "f1SettleMs", 40,
    "clickAnchorX", 0,
    "clickAnchorY", 0
)

gameMatchers := [
    "ahk_exe War3.exe",
    "ahk_exe war3.exe",
    "ahk_exe Warcraft III.exe",
    "ahk_exe Warcraft III Launcher.exe",
    "Warcraft III",
    "魔兽争霸"
]

npcNames := ["妙木山大蛤蟆", "妙木山挑战自我NPC", "家里挑战自我NPC", "家里追捕逃忍NPC", "尾兽处追捕逃忍NPC"]
farmNames := ["妙木山挑战自我x20", "妙木山挑战自我x5", "家里挑战自我x10", "家里挑战自我x5", "家里追捕逃忍", "去尾兽处", "尾兽处追捕逃忍"]
flowNames := ["自定义流程1", "自定义流程2", "自定义流程3", "自定义流程4", "自定义流程5", "自定义流程6", "自定义流程7", "自定义流程8"]

preTypeOptions := ["无", "按键", "公屏"]
npcActionOptions := ["无", "x20", "x10", "x5", "追捕", "去尾兽处", "命令键", "只点击NPC"]
releaseTypeOptions := ["无", "技能按键", "装备按键", "技能槽位", "装备槽位"]
skillDefaultKeys := ["", "", "", "", "", "", "", "", "", "", "", ""]

npcs := Map()
farms := Map()
farmMeta := Map()
flows := Map()
keyMap := Map()
farmRows := Map()
groupRows := []
currentFlowSlot := 1
flowNameEdit := ""
farmTargetDDL := ""
tuneGroupDDL := ""
tuneStepEdit := ""
tuneMaxEdit := ""
tuneInfoText := ""
keyDelayEdit := ""
skillKeyDelayEdit := ""
heroSelectDelayEdit := ""
npcClickDelayEdit := ""
chatDelayEdit := ""
teleportKeyDelayEdit := ""
mouseMoveDelayEdit := ""
releaseMouseMoveDelayEdit := ""
currentActionElapsedMs := 0
cameraLocked := false
suppressDurationRefresh := false
suppressFlowChange := false
chatInputOpen := false
sessionActiveLast := -1
sessionBoundsLast := ""
initializeRequestSeen := ""
shutdownRequestSeen := ""
autoInitLastAttempt := 0
autoInitRetryMs := 5000
autoInitPid := 0
autoInitAttempts := 0
autoInitMaxAttempts := 12
parentPid := ToInt(GetCommandLineArgValue("--parent-pid"), 0)
activeKeyCapture := ""
keyCaptureMouseHotkeys := ["*XButton1", "*XButton2", "*MButton"]
headlessMode := HasCommandLineArg("--background") || HasCommandLineArg("--initialize")
configSnapshot := ""
configReloading := false

ApplyTrayIcon()
BuildDefaults()
LoadConfig()
configSnapshot := ReadConfigSnapshot()
if !headlessMode {
    BuildGui()
} else {
    SetTimer PollConfigChanges, 250
    SetTimer PollGameWindowState, 250
    SetTimer PollInitializeRequest, 100
    SetTimer AutoInitializeGameSession, 1000
    SetTimer PollShutdownRequest, 100
    SetTimer PollParentProcess, 500
}
ApplyFlowHotkeys()
OnExit HandleScriptExit
if HasCommandLineArg("--initialize") {
    SetTimer InitializeGameSession, -350
}

#HotIf IsGameActive()
^!b::BindActiveWindowAsGame()
~$Enter::OpenSkillChatInput()
~$Escape::CloseSkillChatInput()
#HotIf

; F9 is intentionally global so the Warcraft shortcut cannot receive it when
; the game window matcher is stale. Electron normally captures F9 first and
; writes the request consumed by the already-running elevated backend.
F9::InitializeGameSession()

ApplyTrayIcon() {
    global appIconPath
    if FileExist(appIconPath) {
        try TraySetIcon appIconPath
    }
}

ApplyGuiIcon(guiObj) {
    global appIconPath, appIconSmall, appIconLarge
    if !FileExist(appIconPath) {
        return
    }
    try {
        appIconSmall := LoadPicture(appIconPath, "Icon1 w16 h16", &imgTypeSmall)
        appIconLarge := LoadPicture(appIconPath, "Icon1 w32 h32", &imgTypeLarge)
        if appIconSmall {
            SendMessage 0x80, 0, appIconSmall, , "ahk_id " guiObj.Hwnd
        }
        if appIconLarge {
            SendMessage 0x80, 1, appIconLarge, , "ahk_id " guiObj.Hwnd
        }
    }
}

BuildDefaults() {
    global npcs, farms, farmMeta, flows, keyMap
    global npcNames, farmNames, flowNames, flowCount, groupCount, skillSlotCount, itemSlotCount, skillDefaultKeys
    global defaultKeyDelayMs, defaultSkillKeyDelayMs, defaultHeroSelectDelayMs, defaultNpcClickDelayMs, defaultChatDelayMs, defaultTeleportKeyDelayMs, defaultMouseMoveDelayMs, defaultReleaseMouseMoveDelayMs

    ResetFlowNames()

    for _, name in npcNames {
        npcs[name] := Map(
            "camera", "",
            "x", "", "y", "",
            "worldX", "", "worldY", "",
            "npcId", ""
        )
    }
    npcs["妙木山大蛤蟆"]["x"] := 845
    npcs["妙木山大蛤蟆"]["y"] := 390
    npcs["妙木山挑战自我NPC"]["x"] := 1172
    npcs["妙木山挑战自我NPC"]["y"] := 689
    npcs["尾兽处追捕逃忍NPC"]["x"] := 977
    npcs["尾兽处追捕逃忍NPC"]["y"] := 509

    ; Aliases keep the old workflow names usable while the click itself uses
    ; the verified map/world coordinates. The screen x/y fields remain only
    ; for migration and are no longer read by ExecuteNpcAction.
    SetNpcWorldDefaults(npcs["妙木山大蛤蟆"], -5056, 2368, "n012-01")
    SetNpcWorldDefaults(npcs["妙木山挑战自我NPC"], 3968, 3520, "n00A-01")
    SetNpcWorldDefaults(npcs["家里挑战自我NPC"], -4416, 1792, "n011-01")
    SetNpcWorldDefaults(npcs["家里追捕逃忍NPC"], -2432, -3648, "n01E-01")
    SetNpcWorldDefaults(npcs["尾兽处追捕逃忍NPC"], 3520, 4480, "n01E-02")

    farmMeta["妙木山挑战自我x20"] := Map("npc", "妙木山挑战自我NPC", "action", "x20")
    farmMeta["妙木山挑战自我x5"] := Map("npc", "妙木山挑战自我NPC", "action", "x5")
    farmMeta["家里挑战自我x10"] := Map("npc", "家里挑战自我NPC", "action", "x10")
    farmMeta["家里挑战自我x5"] := Map("npc", "家里挑战自我NPC", "action", "x5")
    farmMeta["家里追捕逃忍"] := Map("npc", "家里追捕逃忍NPC", "action", "追捕")
    farmMeta["去尾兽处"] := Map("npc", "妙木山大蛤蟆", "action", "去尾兽处")
    farmMeta["尾兽处追捕逃忍"] := Map("npc", "尾兽处追捕逃忍NPC", "action", "追捕")

    for _, name in farmNames {
        farms[name] := Map(
            "actionKey", "",
            "releaseType", "无",
            "releaseKey", "",
            "targetX", "",
            "targetY", ""
        )
    }

    Loop flowCount {
        slot := A_Index
        flows[slot] := Map(
            "name", flowNames[slot],
            "enabled", 0,
            "hotkey", "",
            "keyDelay", defaultKeyDelayMs,
            "skillKeyDelay", defaultSkillKeyDelayMs,
            "heroSelectDelay", defaultHeroSelectDelayMs,
            "npcClickDelay", defaultNpcClickDelayMs,
            "chatDelay", defaultChatDelayMs,
            "teleportKeyDelay", defaultTeleportKeyDelayMs,
            "mouseMoveDelay", defaultMouseMoveDelayMs,
            "releaseMouseMoveDelay", defaultReleaseMouseMoveDelayMs,
            "groups", []
        )
        Loop groupCount {
            flows[slot]["groups"].Push(DefaultGroup())
        }
    }

    Loop skillSlotCount {
        keyMap["skill" A_Index] := skillDefaultKeys[A_Index]
    }
    Loop itemSlotCount {
        keyMap["item" A_Index] := ""
    }
}

DefaultGroup() {
    return Map(
        "enabled", 0,
        "preType", "无",
        "preValue", "",
        "farm", "无",
        "wait", 0,
        "duration", 0
    )
}

SetNpcWorldDefaults(npc, x, y, npcId := "") {
    npc["worldX"] := x
    npc["worldY"] := y
    npc["npcId"] := npcId
}

DefaultFlowName(slot) {
    return "自定义流程" slot
}

ResetFlowNames() {
    global flowNames, flowCount
    flowNames := []
    Loop flowCount {
        flowNames.Push(DefaultFlowName(A_Index))
    }
}

LoadConfig() {
    global configPath, profileDir, npcs, farms, flows, keyMap, gameWindowMatcher, skipGameCheck, stopHotkey, defaultStopHotkey
    global keyDelayMs, skillKeyDelayMs, heroSelectDelayMs, npcClickDelayMs, chatDelayMs, teleportKeyDelayMs, mouseMoveDelayMs, releaseMouseMoveDelayMs
    global defaultKeyDelayMs, defaultSkillKeyDelayMs, defaultHeroSelectDelayMs, defaultNpcClickDelayMs, defaultChatDelayMs, defaultTeleportKeyDelayMs, defaultMouseMoveDelayMs, defaultReleaseMouseMoveDelayMs
    global currentProfileName, currentProfilePath, worldProjection
    global npcNames, farmNames, flowNames, flowCount, groupCount, skillSlotCount, itemSlotCount

    gameWindowMatcher := IniRead(configPath, "General", "gameWindowMatcher", gameWindowMatcher)
    skipGameCheck := ToInt(IniRead(configPath, "General", "skipGameCheck", skipGameCheck ? 1 : 0), 0) = 1
    stopHotkey := NormalizeHotkey(IniRead(configPath, "General", "stopHotkey", defaultStopHotkey))
    if stopHotkey = "" {
        stopHotkey := defaultStopHotkey
    }
    legacyGeneralDelay := NormalizeDelay(IniRead(configPath, "General", "commandDelayMs", defaultKeyDelayMs), defaultKeyDelayMs, 1000)
    keyDelayMs := NormalizeDelay(IniRead(configPath, "General", "keyDelayMs", legacyGeneralDelay), legacyGeneralDelay, 1000)
    skillKeyDelayMs := NormalizeDelay(IniRead(configPath, "General", "skillKeyDelayMs", defaultSkillKeyDelayMs), defaultSkillKeyDelayMs, 1000)
    heroSelectDelayMs := NormalizeDelay(IniRead(configPath, "General", "heroSelectDelayMs", defaultHeroSelectDelayMs), defaultHeroSelectDelayMs, 1000)
    legacyGeneralClickDelay := NormalizeDelay(IniRead(configPath, "General", "clickDelayMs", defaultNpcClickDelayMs), defaultNpcClickDelayMs, 1000)
    npcClickDelayMs := NormalizeDelay(IniRead(configPath, "General", "npcClickDelayMs", legacyGeneralClickDelay), legacyGeneralClickDelay, 1000)
    chatDelayMs := NormalizeDelay(IniRead(configPath, "General", "chatDelayMs", defaultChatDelayMs), defaultChatDelayMs, 5000)
    teleportKeyDelayMs := NormalizeDelay(IniRead(configPath, "General", "teleportKeyDelayMs", defaultTeleportKeyDelayMs), defaultTeleportKeyDelayMs, 5000)
    mouseMoveDelayMs := NormalizeDelay(IniRead(configPath, "General", "mouseMoveDelayMs", defaultMouseMoveDelayMs), defaultMouseMoveDelayMs, 1000)
    releaseMouseMoveDelayMs := NormalizeDelay(IniRead(configPath, "General", "releaseMouseMoveDelayMs", defaultReleaseMouseMoveDelayMs), defaultReleaseMouseMoveDelayMs, 1000)
    currentProfileName := IniRead(configPath, "General", "currentProfileName", currentProfileName)
    currentProfilePath := IniRead(configPath, "General", "currentProfilePath", currentProfilePath)
    if IsDefaultProfileName(currentProfileName) {
        currentProfilePath := ""
    } else if currentProfilePath = "" {
        currentProfilePath := GetProfilePathForName(currentProfileName)
    }

    for _, name in npcNames {
        section := "NPC." name
        npc := npcs[name]
        npc["camera"] := NormalizeNpcCamera(name, IniRead(configPath, section, "camera", npc["camera"]))
        rawX := IniRead(configPath, section, "x", npc["x"])
        rawY := IniRead(configPath, section, "y", npc["y"])
        npc["x"] := NormalizeCoord(rawX)
        npc["y"] := NormalizeCoord(rawY)
        npc["worldX"] := NormalizeWorldCoord(IniRead(configPath, section, "worldX", npc["worldX"]))
        npc["worldY"] := NormalizeWorldCoord(IniRead(configPath, section, "worldY", npc["worldY"]))
        npc["npcId"] := Trim(IniRead(configPath, section, "npcId", npc["npcId"]))
        if npc["x"] = "" || npc["y"] = "" {
            oldX1 := NormalizeCoord(IniRead(configPath, section, "x1", ""))
            oldY1 := NormalizeCoord(IniRead(configPath, section, "y1", ""))
            oldX2 := NormalizeCoord(IniRead(configPath, section, "x2", ""))
            oldY2 := NormalizeCoord(IniRead(configPath, section, "y2", ""))
            if oldX1 != "" && oldY1 != "" && oldX2 != "" && oldY2 != "" {
                npc["x"] := Round((oldX1 + oldX2) / 2)
                npc["y"] := Round((oldY1 + oldY2) / 2)
            }
        }
    }

    worldProjection["cameraXOffset"] := Trim(IniRead(configPath, "WorldProjection", "cameraXOffset", worldProjection["cameraXOffset"]))
    worldProjection["cameraYOffset"] := Trim(IniRead(configPath, "WorldProjection", "cameraYOffset", worldProjection["cameraYOffset"]))
    worldProjection["cameraZoomOffset"] := Trim(IniRead(configPath, "WorldProjection", "cameraZoomOffset", worldProjection["cameraZoomOffset"]))
    worldProjection["cameraXIndirect"] := ToInt(IniRead(configPath, "WorldProjection", "cameraXIndirect", worldProjection["cameraXIndirect"]), worldProjection["cameraXIndirect"])
    worldProjection["cameraYIndirect"] := ToInt(IniRead(configPath, "WorldProjection", "cameraYIndirect", worldProjection["cameraYIndirect"]), worldProjection["cameraYIndirect"])
    worldProjection["cameraZoomIndirect"] := ToInt(IniRead(configPath, "WorldProjection", "cameraZoomIndirect", worldProjection["cameraZoomIndirect"]), worldProjection["cameraZoomIndirect"])
    worldProjection["axisX"] := ToFloat(IniRead(configPath, "WorldProjection", "axisX", worldProjection["axisX"]), worldProjection["axisX"])
    worldProjection["axisY"] := ToFloat(IniRead(configPath, "WorldProjection", "axisY", worldProjection["axisY"]), worldProjection["axisY"])
    worldProjection["verticalScale"] := ToFloat(IniRead(configPath, "WorldProjection", "verticalScale", worldProjection["verticalScale"]), worldProjection["verticalScale"])
    worldProjection["pixelsPerWorld"] := Trim(IniRead(configPath, "WorldProjection", "pixelsPerWorld", worldProjection["pixelsPerWorld"]))
    worldProjection["snapRadius1080"] := Clamp(ToInt(IniRead(configPath, "WorldProjection", "snapRadius1080", worldProjection["snapRadius1080"]), worldProjection["snapRadius1080"]), 4, 200)
    worldProjection["f1SettleMs"] := Clamp(ToInt(IniRead(configPath, "WorldProjection", "f1SettleMs", worldProjection["f1SettleMs"]), worldProjection["f1SettleMs"]), 0, 500)
    worldProjection["clickAnchorX"] := ToFloat(IniRead(configPath, "WorldProjection", "clickAnchorX", worldProjection["clickAnchorX"]), worldProjection["clickAnchorX"])
    worldProjection["clickAnchorY"] := ToFloat(IniRead(configPath, "WorldProjection", "clickAnchorY", worldProjection["clickAnchorY"]), worldProjection["clickAnchorY"])

    for _, name in farmNames {
        section := "Farm." name
        legacySection := name = "家里挑战自我x10" ? "Farm.家里挑战自我x20" : section
        farm := farms[name]
        farm["actionKey"] := NormalizeKey(IniRead(configPath, section, "actionKey", IniRead(configPath, legacySection, "actionKey", farm["actionKey"])))
        farm["releaseType"] := NormalizeReleaseType(IniRead(configPath, section, "releaseType", IniRead(configPath, legacySection, "releaseType", farm["releaseType"])))
        farm["releaseKey"] := NormalizeReleaseKeyForType(farm["releaseType"], IniRead(configPath, section, "releaseKey", IniRead(configPath, legacySection, "releaseKey", farm["releaseKey"])))
        farm["targetX"] := ToInt(IniRead(configPath, section, "targetX", IniRead(configPath, legacySection, "targetX", farm["targetX"])), farm["targetX"])
        farm["targetY"] := ToInt(IniRead(configPath, section, "targetY", IniRead(configPath, legacySection, "targetY", farm["targetY"])), farm["targetY"])
    }

    legacyFlowLayout := IsLegacyFiveFlowConfig()
    Loop flowCount {
        slot := A_Index
        flow := flows[slot]
        sourceSlot := GetFlowSourceSlot(slot, legacyFlowLayout)
        if sourceSlot = 0 {
            continue
        }
        section := "Flow." sourceSlot
        flow["name"] := NormalizeFlowName(IniRead(configPath, section, "name", flow["name"]))
        flow["enabled"] := ToInt(IniRead(configPath, section, "enabled", flow["enabled"]), flow["enabled"])
        flow["hotkey"] := NormalizeHotkey(IniRead(configPath, section, "hotkey", flow["hotkey"]))
        legacyDelay := NormalizeDelay(IniRead(configPath, section, "commandDelay", defaultKeyDelayMs), defaultKeyDelayMs, 1000)
        flow["keyDelay"] := NormalizeDelay(IniRead(configPath, section, "keyDelay", legacyDelay), legacyDelay, 1000)
        flow["skillKeyDelay"] := NormalizeDelay(IniRead(configPath, section, "skillKeyDelay", skillKeyDelayMs), skillKeyDelayMs, 1000)
        flow["heroSelectDelay"] := NormalizeDelay(IniRead(configPath, section, "heroSelectDelay", heroSelectDelayMs), heroSelectDelayMs, 1000)
        legacyClickDelay := NormalizeDelay(IniRead(configPath, section, "clickDelay", defaultNpcClickDelayMs), defaultNpcClickDelayMs, 1000)
        flow["npcClickDelay"] := NormalizeDelay(IniRead(configPath, section, "npcClickDelay", legacyClickDelay), legacyClickDelay, 1000)
        flow["chatDelay"] := NormalizeDelay(IniRead(configPath, section, "chatDelay", defaultChatDelayMs), defaultChatDelayMs, 5000)
        flow["teleportKeyDelay"] := NormalizeDelay(IniRead(configPath, section, "teleportKeyDelay", defaultTeleportKeyDelayMs), defaultTeleportKeyDelayMs, 5000)
        flow["mouseMoveDelay"] := NormalizeDelay(IniRead(configPath, section, "mouseMoveDelay", defaultMouseMoveDelayMs), defaultMouseMoveDelayMs, 1000)
        flow["releaseMouseMoveDelay"] := NormalizeDelay(IniRead(configPath, section, "releaseMouseMoveDelay", defaultReleaseMouseMoveDelayMs), defaultReleaseMouseMoveDelayMs, 1000)
        flowNames[slot] := flow["name"]

        Loop groupCount {
            gSection := "Flow." sourceSlot ".Group." A_Index
            g := flow["groups"][A_Index]
            g["enabled"] := ToInt(IniRead(configPath, gSection, "enabled", g["enabled"]), g["enabled"])
            g["preType"] := IniRead(configPath, gSection, "preType", g["preType"])
            g["preValue"] := NormalizeFlowPreValue(g["preType"], IniRead(configPath, gSection, "preValue", g["preValue"]))
            g["farm"] := NormalizeFarmName(IniRead(configPath, gSection, "farm", g["farm"]))
            if g["farm"] = "无" {
                oldNpc := IniRead(configPath, gSection, "npc", "")
                oldAction := IniRead(configPath, gSection, "npcAction", "")
                migratedFarm := FindFarmNameForNpcAction(oldNpc, oldAction)
                if migratedFarm != "" {
                    g["farm"] := migratedFarm
                }
            }
            legacyGroupDelay := Clamp(ToInt(IniRead(configPath, gSection, "delay", 0), 0), 0, 30000)
            g["duration"] := Clamp(ToInt(IniRead(configPath, gSection, "duration", legacyGroupDelay), legacyGroupDelay), 0, 30000)
            rawWait := IniRead(configPath, gSection, "wait", "__MISSING__")
            if rawWait != "__MISSING__" {
                g["wait"] := Clamp(ToInt(rawWait, 0), 0, 30000)
            } else {
                try g.Delete("wait")
            }
        }
    }

    Loop skillSlotCount {
        keyMap["skill" A_Index] := NormalizeKey(IniRead(configPath, "KeyMap", "skill" A_Index, keyMap["skill" A_Index]))
    }
    Loop itemSlotCount {
        keyMap["item" A_Index] := NormalizeKey(IniRead(configPath, "KeyMap", "item" A_Index, keyMap["item" A_Index]))
    }
}

ReadConfigSnapshot() {
    global configPath
    try {
        return FileRead(configPath)
    } catch {
        return ""
    }
}

PollConfigChanges(*) {
    ReloadConfigIfChanged()
}

PollGameWindowState(*) {
    global gameSession, chatInputOpen, sessionActiveLast, sessionBoundsLast
    if !gameSession["bound"] {
        chatInputOpen := false
        return
    }
    if !RefreshBoundWindowState() {
        return
    }

    active := IsGameActive() ? 1 : 0
    if !active {
        chatInputOpen := false
    }
    bounds := gameSession["clientLeft"] "|" gameSession["clientTop"] "|" gameSession["clientWidth"] "|" gameSession["clientHeight"]
    if active = sessionActiveLast && bounds = sessionBoundsLast {
        return
    }
    WriteGameSession(gameSession["state"], gameSession["message"], gameSession["projectionReady"])
}

PollInitializeRequest(*) {
    global initializeRequestPath, initializeRequestSeen
    try request := Trim(FileRead(initializeRequestPath))
    catch {
        return
    }
    if request = "" || request = initializeRequestSeen {
        return
    }
    initializeRequestSeen := request
    InitializeGameSession()
}

AutoInitializeGameSession(*) {
    global gameSession, autoInitLastAttempt, autoInitRetryMs, autoInitPid, autoInitAttempts, autoInitMaxAttempts

    if gameSession["ready"] || (autoInitAttempts > 0 && !IsProjectionConfigured()) {
        return
    }

    hwnd := FindExistingGameWindow()
    if !hwnd {
        return
    }

    pid := GetWindowPid(hwnd)
    if !pid {
        return
    }
    if pid != autoInitPid {
        autoInitPid := pid
        autoInitAttempts := 0
        autoInitLastAttempt := 0
    }
    if autoInitAttempts >= autoInitMaxAttempts {
        return
    }

    now := A_TickCount
    if (now - autoInitLastAttempt) < autoInitRetryMs {
        return
    }

    if !FindRemoteModuleBase(pid, "Game.dll") {
        return
    }

    autoInitLastAttempt := now
    autoInitAttempts += 1
    InitializeGameSession()
}

PollShutdownRequest(*) {
    global shutdownRequestPath, shutdownRequestSeen
    try request := Trim(FileRead(shutdownRequestPath))
    catch {
        return
    }
    if request = "" || request = shutdownRequestSeen {
        return
    }
    shutdownRequestSeen := request
    ExitApp
}

PollParentProcess(*) {
    global parentPid
    if parentPid && !ProcessExist(parentPid) {
        ExitApp
    }
}

ReloadConfigIfChanged() {
    global headlessMode, isRunning, configSnapshot, configReloading
    if !headlessMode || isRunning || configReloading {
        return false
    }

    snapshot := ReadConfigSnapshot()
    if snapshot = configSnapshot {
        return false
    }

    configReloading := true
    try {
        LoadConfig()
        configSnapshot := ReadConfigSnapshot()
        ApplyFlowHotkeys()
        return true
    } catch as err {
        SetStatus("后台重载配置失败：" err.Message "。")
        return false
    } finally {
        configReloading := false
    }
}

SaveConfig() {
    global configPath, npcs, farms, flows, keyMap, gameWindowMatcher, skipGameCheck, stopHotkey
    global keyDelayMs, skillKeyDelayMs, heroSelectDelayMs, npcClickDelayMs, chatDelayMs, teleportKeyDelayMs, mouseMoveDelayMs, releaseMouseMoveDelayMs
    global currentProfileName, currentProfilePath, worldProjection
    global npcNames, farmNames, flowCount, groupCount, skillSlotCount, itemSlotCount

    IniWrite(gameWindowMatcher, configPath, "General", "gameWindowMatcher")
    IniWrite(skipGameCheck ? 1 : 0, configPath, "General", "skipGameCheck")
    IniWrite(stopHotkey, configPath, "General", "stopHotkey")
    IniWrite(keyDelayMs, configPath, "General", "keyDelayMs")
    IniWrite(skillKeyDelayMs, configPath, "General", "skillKeyDelayMs")
    IniWrite(heroSelectDelayMs, configPath, "General", "heroSelectDelayMs")
    IniWrite(npcClickDelayMs, configPath, "General", "npcClickDelayMs")
    IniWrite(chatDelayMs, configPath, "General", "chatDelayMs")
    IniWrite(teleportKeyDelayMs, configPath, "General", "teleportKeyDelayMs")
    IniWrite(mouseMoveDelayMs, configPath, "General", "mouseMoveDelayMs")
    IniWrite(releaseMouseMoveDelayMs, configPath, "General", "releaseMouseMoveDelayMs")
    IniWrite(currentProfileName, configPath, "General", "currentProfileName")
    IniWrite(currentProfilePath, configPath, "General", "currentProfilePath")

    for _, name in npcNames {
        section := "NPC." name
        for key, value in npcs[name] {
            IniWrite(value, configPath, section, key)
        }
    }

    for key, value in worldProjection {
        IniWrite(value, configPath, "WorldProjection", key)
    }

    for _, name in farmNames {
        section := "Farm." name
        for key, value in farms[name] {
            IniWrite(value, configPath, section, key)
        }
        try IniDelete(configPath, section, "useTarget")
    }

    Loop flowCount {
        slot := A_Index
        section := "Flow." slot
        flow := flows[slot]
        IniWrite(flow["name"], configPath, section, "name")
        IniWrite(flow["enabled"], configPath, section, "enabled")
        IniWrite(flow["hotkey"], configPath, section, "hotkey")
        IniWrite(flow["keyDelay"], configPath, section, "keyDelay")
        IniWrite(flow["skillKeyDelay"], configPath, section, "skillKeyDelay")
        IniWrite(flow["heroSelectDelay"], configPath, section, "heroSelectDelay")
        IniWrite(flow["npcClickDelay"], configPath, section, "npcClickDelay")
        IniWrite(flow["chatDelay"], configPath, section, "chatDelay")
        IniWrite(flow["teleportKeyDelay"], configPath, section, "teleportKeyDelay")
        IniWrite(flow["mouseMoveDelay"], configPath, section, "mouseMoveDelay")
        IniWrite(flow["releaseMouseMoveDelay"], configPath, section, "releaseMouseMoveDelay")
        Loop groupCount {
            gSection := "Flow." slot ".Group." A_Index
            for key, value in flow["groups"][A_Index] {
                IniWrite(value, configPath, gSection, key)
            }
        }
    }

    Loop skillSlotCount {
        IniWrite(keyMap["skill" A_Index], configPath, "KeyMap", "skill" A_Index)
    }
    Loop itemSlotCount {
        IniWrite(keyMap["item" A_Index], configPath, "KeyMap", "item" A_Index)
    }
}

SaveProfileAs(*) {
    SaveProfileAsNew()
}

SaveCurrentProfile(*) {
    global currentProfileName, currentProfilePath
    targetPath := GetCurrentProfileSavePath()
    if targetPath = "" {
        SaveProfileAsNew()
        return
    }
    SaveProfileToPath(targetPath, currentProfileName, "已保存当前配置英雄：")
}

SaveProfileAsNew(*) {
    global currentProfileName, currentProfilePath

    profileName := InputBox("请输入英雄名称。建议直接用英雄名命名，例如：鸣人、佐助、初音。", "保存为新英雄")
    if profileName.Result != "OK" {
        return
    }
    safeName := SanitizeProfileName(profileName.Value)
    if safeName = "" {
        SetStatus("保存为新英雄失败：名称不能为空。")
        return
    }

    oldName := currentProfileName
    oldPath := currentProfilePath
    currentProfileName := safeName
    currentProfilePath := GetProfilePathForName(safeName)

    if !SaveProfileToPath(currentProfilePath, currentProfileName, "已保存为新英雄配置：") {
        currentProfileName := oldName
        currentProfilePath := oldPath
        UpdateCurrentProfileLabel()
    }
}

SaveProfileToPath(targetPath, profileName, successPrefix) {
    global configPath
    try {
        SaveAllVisibleStateToMemory()
        EnsureProfileDir()
        SaveConfig()
        if !SamePath(configPath, targetPath) {
            try FileSetAttrib("-R", targetPath)
            FileCopy(configPath, targetPath, true)
        }
        ApplyFlowHotkeys()
        UpdateCurrentProfileLabel()
        SetStatus(successPrefix profileName "。文件：" targetPath)
        return true
    } catch as err {
        try SaveConfig()
        SetStatus("保存配置失败：" err.Message "。源：" configPath "；目标：" targetPath)
        return false
    }
}

SaveAllVisibleStateToMemory() {
    SaveGeneralSettings()
    SaveAllFarmRows()
    SaveCurrentFlowToMemory()
    SaveCurrentNpcToMemory()
}

LoadProfileFromFile(*) {
    global configPath, profileDir, npcNames, flowDDL, npcDDL, skipGameCheck, skipGameCheckCB, currentProfileName, currentProfilePath
    if !DirExist(profileDir) {
        SetStatus("还没有配置文件夹。第一次点“保存为新英雄”时会自动创建 profiles 文件夹。")
        return
    }
    selected := FileSelect(1, profileDir, "读取配置", "INI 配置 (*.ini)")
    if selected = "" {
        return
    }

    if !FileExist(selected) {
        SetStatus("读取配置失败：文件不存在。文件：" selected)
        return
    }

    loadedProfileName := GetProfileNameFromPath(selected)
    syncWarning := ""
    try {
        BuildDefaults()
        LoadConfigFromPath(selected)
    } catch as err {
        SetStatus("读取配置失败：" err.Message "。文件：" selected)
        return
    }

    currentProfileName := loadedProfileName
    currentProfilePath := selected
    if !TrySyncRuntimeConfig(selected, &syncWarning) {
        ; If the program folder is not writable, keep using the selected profile
        ; as the active config so loading and saving can still work.
        configPath := selected
    }
    RefreshFlowNamesControl(1)
    npcDDL.Choose(1)
    skipGameCheckCB.Value := skipGameCheck ? 1 : 0
    LoadFarmRowsToControls()
    LoadFlowToControls(1)
    LoadNpcToControls(npcNames[1])
    stopHotkeyEdit.Text := stopHotkey
    UpdateCurrentProfileLabel()
    ApplyFlowHotkeys()
    suffix := syncWarning != "" ? "。提示：运行目录不可写，已直接使用该英雄配置；" syncWarning : ""
    SetStatus("已读取配置英雄：" currentProfileName "。文件：" selected suffix)
}

LoadConfigFromPath(path) {
    global configPath
    oldConfigPath := configPath
    configPath := path
    try {
        LoadConfig()
    } finally {
        configPath := oldConfigPath
    }
}

TrySyncRuntimeConfig(sourcePath, &warning) {
    global configPath
    warning := ""
    if SamePath(sourcePath, configPath) {
        return true
    }
    try {
        try FileSetAttrib("-R", configPath)
        FileCopy(sourcePath, configPath, true)
        return true
    } catch as err {
        warning := err.Message "；源：" sourcePath "；目标：" configPath
        return false
    }
}

EnsureProfileDir() {
    global profileDir
    if !DirExist(profileDir) {
        DirCreate(profileDir)
    }
}

SanitizeProfileName(name) {
    name := Trim(name)
    name := RegExReplace(name, '[\\/:*?"<>|]', "_")
    return name
}

GetProfileNameFromPath(path) {
    SplitPath path, , , , &nameNoExt
    return nameNoExt != "" ? nameNoExt : "默认/未读取"
}

SamePath(pathA, pathB) {
    return StrLower(Trim(pathA)) = StrLower(Trim(pathB))
}

GetProfilePathForName(name) {
    global profileDir
    safeName := SanitizeProfileName(name)
    return safeName != "" ? profileDir "\" safeName ".ini" : ""
}

GetCurrentProfileSavePath() {
    global currentProfileName, currentProfilePath
    if !IsDefaultProfileName(currentProfileName) {
        if currentProfilePath != "" {
            return currentProfilePath
        }
        return GetProfilePathForName(currentProfileName)
    }
    return ""
}

IsDefaultProfileName(name) {
    return Trim(name) = "" || name = "默认/未读取"
}

GetCurrentFlowSlot() {
    global currentFlowSlot, flowDDL, flows
    ; flowDDL.Value is the user's selection input; currentFlowSlot is the loaded editor slot.
    slot := currentFlowSlot
    if slot && flows.Has(slot) {
        return slot
    }
    try slot := flowDDL.Value
    catch {
        slot := 1
    }
    return flows.Has(slot) ? slot : 1
}

IsLegacyFiveFlowConfig() {
    global configPath
    flow6Name := IniRead(configPath, "Flow.6", "name", "")
    flow3Name := IniRead(configPath, "Flow.3", "name", "")
    return flow6Name = "" && (InStr(flow3Name, "双鱼四镜像") || InStr(flow3Name, "预留"))
}

GetFlowSourceSlot(slot, legacyFlowLayout) {
    if legacyFlowLayout {
        if slot = 3 || slot = 7 {
            return 0
        }
        if slot >= 4 && slot <= 6 {
            return slot - 1
        }
    }
    return slot
}

NormalizeFlowName(name) {
    name := Trim(name)
    name := StrReplace(name, "（预留）", "")
    name := StrReplace(name, "(预留)", "")
    name := Trim(name)
    return name
}

BuildGui() {
    global mainGui, statusText, currentProfileText
    global farmRows, farmTargetDDL
    global flowDDL, flowNameEdit, flowEnabledCB, flowHotkeyEdit, groupRows
    global tuneGroupDDL, tuneStepEdit, tuneMaxEdit, tuneInfoText
    global npcDDL, npcXEdit, npcYEdit, stopHotkeyEdit
    global keyDelayEdit, skillKeyDelayEdit, heroSelectDelayEdit, npcClickDelayEdit, chatDelayEdit, teleportKeyDelayEdit, mouseMoveDelayEdit, releaseMouseMoveDelayEdit
    global skipGameCheck, skipGameCheckCB
    global farmNames, flowNames, npcNames, releaseTypeOptions, preTypeOptions, stopHotkey, currentProfileName, appTitle

    mainGui := Gui("+Resize", appTitle)
    mainGui.SetFont("s9", "Microsoft YaHei UI")
    ApplyGuiIcon(mainGui)

    mainGui.AddText("x16 y10 w720", "原则：NPC菜单、技能、装备都用按键；坐标只用于 NPC点击点、技能/装备鼠标点。F1仅在释放类型不为无时用于选英雄。")
    currentProfileText := mainGui.AddText("x760 y10 w360", "当前配置英雄：" currentProfileName)

    mainGui.AddGroupBox("x16 y38 w1128 h234", "一、刷本设置（7个固定项全部独立保存）")
    mainGui.AddText("x34 y66 w170", "刷本项")
    mainGui.AddText("x224 y66 w68", "动作键/采")
    mainGui.AddText("x300 y66 w90", "释放类型")
    mainGui.AddText("x404 y66 w70", "键/槽位")
    mainGui.AddText("x486 y66 w130", "技能鼠标点X/Y")
    saveProfileBtn := mainGui.AddButton("x744 y60 w120", "保存当前配置")
    saveProfileBtn.OnEvent("Click", SaveCurrentProfile)
    saveAsProfileBtn := mainGui.AddButton("x874 y60 w128", "保存为新英雄")
    saveAsProfileBtn.OnEvent("Click", SaveProfileAsNew)
    loadProfileBtn := mainGui.AddButton("x1012 y60 w96", "读取配置")
    loadProfileBtn.OnEvent("Click", LoadProfileFromFile)
    clearFarmBtn := mainGui.AddButton("x796 y224 w112", "清空刷本设置")
    clearFarmBtn.OnEvent("Click", ClearFarmSettings)
    keyMapBtnTop := mainGui.AddButton("x924 y224 w140", "平台按键映射表")
    keyMapBtnTop.OnEvent("Click", ShowKeyMapGui)

    farmRows := Map()
    Loop farmNames.Length {
        farmName := farmNames[A_Index]
        rowY := 90 + (A_Index - 1) * 26
        row := Map()
        mainGui.AddText("x34 y" rowY+4 " w178", farmName)
        row["actionKey"] := mainGui.AddEdit("x224 y" rowY " w42", "")
        actionCapBtn := mainGui.AddButton("x270 y" rowY-1 " w24", "采")
        actionCapBtn.OnEvent("Click", StartKeyCapture.Bind(row["actionKey"], farmName "动作键"))
        row["releaseType"] := mainGui.AddDropDownList("x300 y" rowY " w96", releaseTypeOptions)
        row["releaseKey"] := mainGui.AddEdit("x404 y" rowY " w42", "")
        releaseCapBtn := mainGui.AddButton("x450 y" rowY-1 " w24", "采")
        releaseCapBtn.OnEvent("Click", StartKeyCapture.Bind(row["releaseKey"], farmName "释放键"))
        row["targetX"] := mainGui.AddEdit("x486 y" rowY " w54", "")
        row["targetY"] := mainGui.AddEdit("x546 y" rowY " w54", "")
        capBtn := mainGui.AddButton("x620 y" rowY-1 " w86", "采鼠标点")
        capBtn.OnEvent("Click", CaptureFarmTarget.Bind(farmName))
        row["actionKey"].OnEvent("Change", (*) => RefreshGroupDurationDisplay())
        row["releaseType"].OnEvent("Change", (*) => RefreshGroupDurationDisplay())
        row["releaseKey"].OnEvent("Change", (*) => RefreshGroupDurationDisplay())
        row["targetX"].OnEvent("Change", (*) => RefreshGroupDurationDisplay())
        row["targetY"].OnEvent("Change", (*) => RefreshGroupDurationDisplay())
        farmRows[farmName] := row
    }
    mainGui.AddText("x924 y92 w170", "鼠标点目标")
    farmTargetDDL := mainGui.AddDropDownList("x924 y114 w190", farmNames)
    farmTargetDDL.Choose(1)

    mainGui.AddGroupBox("x16 y276 w1128 h390", "二、执行流程（最多8个自定义流程，每个流程最多8组，按ID顺序执行）")
    mainGui.AddText("x34 y306 w70", "流程")
    flowDDL := mainGui.AddDropDownList("x104 y302 w132", flowNames)
    flowDDL.OnEvent("Change", OnFlowChanged)
    mainGui.AddText("x246 y306 w34", "名称")
    flowNameEdit := mainGui.AddEdit("x282 y302 w112", "")
    flowEnabledCB := mainGui.AddCheckBox("x404 y303 w54", "启用")
    mainGui.AddText("x468 y306 w50", "触发键")
    flowHotkeyEdit := mainGui.AddEdit("x520 y302 w64", "")
    flowHotkeyCapBtn := mainGui.AddButton("x588 y301 w30", "采")
    flowHotkeyCapBtn.OnEvent("Click", StartHotkeyCapture.Bind(flowHotkeyEdit, "流程触发键"))
    mainGui.AddText("x628 y306 w92", "停止热键(自填)")
    stopHotkeyEdit := mainGui.AddEdit("x722 y302 w68", stopHotkey)
    stopHotkeyCapBtn := mainGui.AddButton("x794 y301 w30", "采")
    stopHotkeyCapBtn.OnEvent("Click", StartHotkeyCapture.Bind(stopHotkeyEdit, "停止热键"))
    mainGui.AddText("x832 y306 w110", "默认Z，仅一个生效")
    clearFlowBtn := mainGui.AddButton("x958 y300 w82", "清空当前流程")
    clearFlowBtn.OnEvent("Click", ClearCurrentFlow)
    stopBtn := mainGui.AddButton("x1048 y300 w70", "停止")
    stopBtn.OnEvent("Click", (*) => RequestStop())

    mainGui.AddGroupBox("x28 y326 w820 h76", "耗时设置（每次 +/- 10ms）")
    mainGui.AddText("x44 y348 w78", "普通键耗时")
    keyDelayEdit := mainGui.AddEdit("x126 y344 w42", "")
    keyDelayEdit.OnEvent("Change", (*) => RefreshGroupDurationDisplay())
    mainGui.AddButton("x172 y343 w24", "-").OnEvent("Click", AdjustFlowDelayFixed.Bind("key", -1))
    mainGui.AddButton("x200 y343 w24", "+").OnEvent("Click", AdjustFlowDelayFixed.Bind("key", 1))
    mainGui.AddText("x238 y348 w78", "技能键耗时")
    skillKeyDelayEdit := mainGui.AddEdit("x320 y344 w42", "")
    skillKeyDelayEdit.OnEvent("Change", (*) => RefreshGroupDurationDisplay())
    mainGui.AddButton("x366 y343 w24", "-").OnEvent("Click", AdjustFlowDelayFixed.Bind("skillKey", -1))
    mainGui.AddButton("x394 y343 w24", "+").OnEvent("Click", AdjustFlowDelayFixed.Bind("skillKey", 1))
    mainGui.AddText("x432 y348 w72", "F2/F3耗时")
    teleportKeyDelayEdit := mainGui.AddEdit("x508 y344 w42", "")
    teleportKeyDelayEdit.OnEvent("Change", (*) => RefreshGroupDurationDisplay())
    mainGui.AddButton("x554 y343 w24", "-").OnEvent("Click", AdjustFlowDelayFixed.Bind("teleport", -1))
    mainGui.AddButton("x582 y343 w24", "+").OnEvent("Click", AdjustFlowDelayFixed.Bind("teleport", 1))
    mainGui.AddText("x620 y348 w86", "NPC点击耗时")
    npcClickDelayEdit := mainGui.AddEdit("x710 y344 w42", "")
    npcClickDelayEdit.OnEvent("Change", (*) => RefreshGroupDurationDisplay())
    mainGui.AddButton("x756 y343 w24", "-").OnEvent("Click", AdjustFlowDelayFixed.Bind("npcClick", -1))
    mainGui.AddButton("x784 y343 w24", "+").OnEvent("Click", AdjustFlowDelayFixed.Bind("npcClick", 1))
    mainGui.AddText("x496 y376 w66", "公屏耗时")
    chatDelayEdit := mainGui.AddEdit("x566 y372 w42", "")
    chatDelayEdit.OnEvent("Change", (*) => RefreshGroupDurationDisplay())
    mainGui.AddButton("x612 y371 w24", "-").OnEvent("Click", AdjustFlowDelayFixed.Bind("chat", -1))
    mainGui.AddButton("x640 y371 w24", "+").OnEvent("Click", AdjustFlowDelayFixed.Bind("chat", 1))
    mainGui.AddText("x674 y376 w68", "F1耗时")
    heroSelectDelayEdit := mainGui.AddEdit("x746 y372 w42", "")
    heroSelectDelayEdit.OnEvent("Change", (*) => RefreshGroupDurationDisplay())
    mainGui.AddButton("x792 y371 w24", "-").OnEvent("Click", AdjustFlowDelayFixed.Bind("heroSelect", -1))
    mainGui.AddButton("x820 y371 w24", "+").OnEvent("Click", AdjustFlowDelayFixed.Bind("heroSelect", 1))

    mainGui.AddText("x44 y376 w92", "NPC移鼠耗时")
    mouseMoveDelayEdit := mainGui.AddEdit("x140 y372 w42", "")
    mouseMoveDelayEdit.OnEvent("Change", (*) => RefreshGroupDurationDisplay())
    mainGui.AddButton("x186 y371 w24", "-").OnEvent("Click", AdjustFlowDelayFixed.Bind("mouse", -1))
    mainGui.AddButton("x214 y371 w24", "+").OnEvent("Click", AdjustFlowDelayFixed.Bind("mouse", 1))
    mainGui.AddText("x274 y376 w98", "技能移鼠耗时")
    releaseMouseMoveDelayEdit := mainGui.AddEdit("x376 y372 w42", "")
    releaseMouseMoveDelayEdit.OnEvent("Change", (*) => RefreshGroupDurationDisplay())
    mainGui.AddButton("x422 y371 w24", "-").OnEvent("Click", AdjustFlowDelayFixed.Bind("releaseMouse", -1))
    mainGui.AddButton("x450 y371 w24", "+").OnEvent("Click", AdjustFlowDelayFixed.Bind("releaseMouse", 1))
    mainGui.AddText("x858 y330 w262 h68", "释放类型不为无时才按F1选英雄。技能/装备释放走技能键耗时，只按键，不点击目标点。")

    mainGui.AddText("x34 y414 w34", "ID")
    mainGui.AddText("x72 y414 w42", "启用")
    mainGui.AddText("x126 y414 w80", "组前类型")
    mainGui.AddText("x220 y414 w120", "组前按键/公屏")
    mainGui.AddText("x356 y414 w210", "刷本选择")
    mainGui.AddText("x584 y414 w70", "动作占用")
    mainGui.AddText("x660 y414 w80", "组合时长ms")
    mainGui.AddText("x746 y414 w56", "等待ms")
    mainGui.AddText("x806 y414 w60", "10ms")

    groupRows := []
    farmListWithNone := ArrayWithNone(farmNames)
    Loop 8 {
        rowY := 436 + (A_Index - 1) * 28
        row := Map()
        mainGui.AddText("x34 y" rowY+4 " w26", A_Index)
        row["enabled"] := mainGui.AddCheckBox("x76 y" rowY " w30", "")
        row["preType"] := mainGui.AddDropDownList("x126 y" rowY " w80", preTypeOptions)
        row["preValue"] := mainGui.AddEdit("x220 y" rowY " w92", "")
        preCapBtn := mainGui.AddButton("x316 y" rowY-1 " w28", "采")
        preCapBtn.OnEvent("Click", StartKeyCapture.Bind(row["preValue"], "ID" A_Index "组前按键"))
        row["farm"] := mainGui.AddDropDownList("x356 y" rowY " w210", farmListWithNone)
        row["used"] := mainGui.AddEdit("x584 y" rowY " w64 Disabled", "")
        row["duration"] := mainGui.AddEdit("x660 y" rowY " w76 Disabled", "")
        row["wait"] := mainGui.AddEdit("x746 y" rowY " w52", "")
        delayDecBtn := mainGui.AddButton("x806 y" rowY-1 " w28", "-")
        delayDecBtn.OnEvent("Click", AdjustGroupDelayFixed.Bind(A_Index, -1))
        delayIncBtn := mainGui.AddButton("x838 y" rowY-1 " w28", "+")
        delayIncBtn.OnEvent("Click", AdjustGroupDelayFixed.Bind(A_Index, 1))
        row["enabled"].OnEvent("Click", (*) => RefreshGroupDurationDisplay())
        row["preType"].OnEvent("Change", (*) => RefreshGroupDurationDisplay())
        row["preValue"].OnEvent("Change", (*) => RefreshGroupDurationDisplay())
        row["farm"].OnEvent("Change", (*) => RefreshGroupDurationDisplay())
        row["wait"].OnEvent("Change", (*) => RefreshGroupDurationDisplay())
        row["wait"].OnEvent("LoseFocus", (*) => RefreshGroupDurationDisplay())
        groupRows.Push(row)
    }
    mainGui.AddText("x888 y410 w226 h72", "执行顺序固定为 ID 1 到 ID 8。公屏会快速执行 Enter -> 命令 -> Enter。")
    mainGui.AddGroupBox("x888 y486 w226 h170", "使用说明")
    mainGui.AddText("x906 y512 w200 h132", "1. 动作占用由程序按当前ID自动计算。`n2. 等待ms可手动修改。`n3. 组合时长=动作占用+等待。`n4. 坐标通过面板按钮记录。`n5. 自动施法需要开启平台内快捷施法。")

    mainGui.AddGroupBox("x16 y676 w1128 h164", "三、NPC / 坐标 / 平台按键标定")
    mainGui.AddText("x34 y706 w70", "NPC")
    npcDDL := mainGui.AddDropDownList("x104 y702 w190", npcNames)
    npcDDL.OnEvent("Change", OnNpcChanged)
    mainGui.AddText("x316 y706 w150", "NPC点击前F1两次")
    mainGui.AddText("x486 y706 w76", "点击点X/Y")
    npcXEdit := mainGui.AddEdit("x584 y702 w70", "")
    npcYEdit := mainGui.AddEdit("x662 y702 w70", "")
    capNpcBtn := mainGui.AddButton("x750 y700 w120", "记录NPC点")
    capNpcBtn.OnEvent("Click", (*) => CaptureNpcClickPoint())
    skipGameCheckCB := mainGui.AddCheckBox("x900 y702 w130", "跳过窗口检测")
    skipGameCheckCB.Value := skipGameCheck ? 1 : 0
    skipGameCheckCB.OnEvent("Click", ToggleSkipGameCheck)
    clearNpcBtn := mainGui.AddButton("x1038 y700 w86", "清空本区")
    clearNpcBtn.OnEvent("Click", ClearNpcSettings)

    mainGui.AddText("x34 y744 w430", "NPC不再使用矩形；需要屏幕点时使用“记录NPC点”按钮。")
    saveNpcBtn := mainGui.AddButton("x440 y738 w120", "保存NPC")
    saveNpcBtn.OnEvent("Click", SaveCurrentNpc)
    keyMapBtn := mainGui.AddButton("x580 y738 w150", "平台按键映射表")
    keyMapBtn.OnEvent("Click", ShowKeyMapGui)
    bindGameBtn := mainGui.AddButton("x750 y738 w150", "绑定游戏窗口")
    bindGameBtn.OnEvent("Click", BindGameWindowAfterDelay)
    copyInfoBtn := mainGui.AddButton("x920 y738 w150", "复制当前窗口")
    copyInfoBtn.OnEvent("Click", CopyActiveWindowInfo)

    mainGui.AddText("x34 y788 w1040", "F9用于绑定并初始化本局；成功后自动按F1两次锁定人物视角。")

    mainGui.AddGroupBox("x16 y850 w1128 h88", "感谢支持")
    mainGui.AddText("x34 y874 w1090 h54", "1. 此软件由WosCat@月吟开发，感谢支持。有问题请联系作者微信 xu3071744684`n2. 大力感谢航哥@远航gh的支持，感谢航哥的测试`n3. 感谢橘子哥@橘子怪的支持`n4. 感谢比奇堡@兄弟们的支持")

    statusText := mainGui.AddText("x16 y948 w1128 vStatusText", "状态：已加载配置。")

    flowDDL.Choose(1)
    npcDDL.Choose(1)
    LoadFarmRowsToControls()
    LoadFlowToControls(1)
    LoadNpcToControls(npcNames[1])

    mainGui.OnEvent("Close", (*) => ExitApp())
    mainGui.Show("w1160 h978")
}

LoadFarmRowsToControls() {
    global farms, farmRows, farmNames, releaseTypeOptions
    for _, name in farmNames {
        row := farmRows[name]
        farm := farms[name]
        row["actionKey"].Text := farm["actionKey"]
        row["releaseType"].Choose(IndexOf(releaseTypeOptions, farm["releaseType"], 1))
        row["releaseKey"].Text := farm["releaseKey"]
        row["targetX"].Text := farm["targetX"]
        row["targetY"].Text := farm["targetY"]
    }
}

SaveFarmRow(name) {
    global farms, farmRows
    row := farmRows[name]
    farm := farms[name]
    farm["actionKey"] := NormalizeKey(row["actionKey"].Text)
    farm["releaseType"] := NormalizeReleaseType(row["releaseType"].Text)
    farm["releaseKey"] := NormalizeReleaseKeyForType(farm["releaseType"], row["releaseKey"].Text)
    farm["targetX"] := NormalizeCoord(row["targetX"].Text)
    farm["targetY"] := NormalizeCoord(row["targetY"].Text)
    row["actionKey"].Text := farm["actionKey"]
    row["releaseKey"].Text := farm["releaseKey"]
    row["targetX"].Text := farm["targetX"]
    row["targetY"].Text := farm["targetY"]
}

SaveAllFarmRows() {
    global farmNames
    for _, name in farmNames {
        SaveFarmRow(name)
    }
}

ClearFarmSettings(*) {
    global farms, farmNames
    confirm := MsgBox("只清空刷本设置：动作键、释放类型、释放键、技能鼠标点都会清空。`nNPC坐标、流程、平台按键映射不会动。是否继续？", "清空刷本设置", "YesNo")
    if confirm != "Yes" {
        return
    }
    for _, name in farmNames {
        farms[name] := Map(
            "actionKey", "",
            "releaseType", "无",
            "releaseKey", "",
            "targetX", "",
            "targetY", ""
        )
    }
    LoadFarmRowsToControls()
    SaveConfig()
    SetStatus("已清空刷本设置。要写入当前英雄配置，请点“保存当前配置”。")
}

ClearCurrentFlow(*) {
    global flows, flowNames
    slot := GetCurrentFlowSlot()
    confirm := MsgBox("只清空当前流程：" DefaultFlowName(slot) "。`n刷本设置、NPC坐标、平台按键映射不会动。是否继续？", "清空当前流程", "YesNo")
    if confirm != "Yes" {
        return
    }
    flows[slot] := CreateDefaultFlow(slot)
    flowNames[slot] := flows[slot]["name"]
    RefreshFlowNamesControl(slot)
    LoadFlowToControls(slot)
    SaveConfig()
    ApplyFlowHotkeys()
    SetStatus("已清空当前流程。要写入当前英雄配置，请点“保存当前配置”。")
}

ClearNpcSettings(*) {
    global npcs, keyMap, npcNames, skillSlotCount, itemSlotCount, skillDefaultKeys, npcDDL
    confirm := MsgBox("清空本区会重置 NPC 点击点和平台按键映射。`n内置的妙木山大蛤蟆、妙木山挑战、尾兽追捕默认点位会恢复；家里两个NPC会留空。是否继续？", "清空NPC/坐标/映射", "YesNo")
    if confirm != "Yes" {
        return
    }

    npcs := Map()
    for _, name in npcNames {
        npcs[name] := Map("camera", "", "x", "", "y", "", "worldX", "", "worldY", "", "npcId", "")
    }
    npcs["妙木山大蛤蟆"]["x"] := 845
    npcs["妙木山大蛤蟆"]["y"] := 390
    npcs["妙木山挑战自我NPC"]["x"] := 1172
    npcs["妙木山挑战自我NPC"]["y"] := 689
    npcs["尾兽处追捕逃忍NPC"]["x"] := 977
    npcs["尾兽处追捕逃忍NPC"]["y"] := 509
    SetNpcWorldDefaults(npcs["妙木山大蛤蟆"], -5056, 2368, "n012-01")
    SetNpcWorldDefaults(npcs["妙木山挑战自我NPC"], 3968, 3520, "n00A-01")
    SetNpcWorldDefaults(npcs["家里挑战自我NPC"], -4416, 1792, "n011-01")
    SetNpcWorldDefaults(npcs["家里追捕逃忍NPC"], -2432, -3648, "n01E-01")
    SetNpcWorldDefaults(npcs["尾兽处追捕逃忍NPC"], 3520, 4480, "n01E-02")

    keyMap := Map()
    Loop skillSlotCount {
        keyMap["skill" A_Index] := skillDefaultKeys[A_Index]
    }
    Loop itemSlotCount {
        keyMap["item" A_Index] := ""
    }

    LoadNpcToControls(npcDDL.Text)
    SaveConfig()
    SetStatus("已清空NPC/坐标/平台按键映射区。要写入当前英雄配置，请点“保存当前配置”。")
}

CreateDefaultFlow(slot) {
    global groupCount, defaultKeyDelayMs, defaultSkillKeyDelayMs, defaultHeroSelectDelayMs, defaultNpcClickDelayMs, defaultChatDelayMs, defaultTeleportKeyDelayMs, defaultMouseMoveDelayMs, defaultReleaseMouseMoveDelayMs
    flow := Map(
        "name", DefaultFlowName(slot),
        "enabled", 0,
        "hotkey", "",
        "keyDelay", defaultKeyDelayMs,
        "skillKeyDelay", defaultSkillKeyDelayMs,
        "heroSelectDelay", defaultHeroSelectDelayMs,
        "npcClickDelay", defaultNpcClickDelayMs,
        "chatDelay", defaultChatDelayMs,
        "teleportKeyDelay", defaultTeleportKeyDelayMs,
        "mouseMoveDelay", defaultMouseMoveDelayMs,
        "releaseMouseMoveDelay", defaultReleaseMouseMoveDelayMs,
        "groups", []
    )
    Loop groupCount {
        flow["groups"].Push(DefaultGroup())
    }
    return flow
}

SaveGeneralSettings() {
    global flows, stopHotkey, stopHotkeyEdit, defaultStopHotkey
    global keyDelayMs, skillKeyDelayMs, heroSelectDelayMs, npcClickDelayMs, chatDelayMs, teleportKeyDelayMs, mouseMoveDelayMs, releaseMouseMoveDelayMs
    global keyDelayEdit, skillKeyDelayEdit, heroSelectDelayEdit, npcClickDelayEdit, chatDelayEdit, teleportKeyDelayEdit, mouseMoveDelayEdit, releaseMouseMoveDelayEdit
    try {
        stopHotkey := NormalizeHotkey(stopHotkeyEdit.Text)
        if stopHotkey = "" {
            stopHotkey := defaultStopHotkey
        }
        stopHotkeyEdit.Text := stopHotkey
    }
    try {
        slot := GetCurrentFlowSlot()
        if slot {
            flow := flows[slot]
            flow["keyDelay"] := NormalizeDelay(keyDelayEdit.Text, flow["keyDelay"], 1000)
            flow["skillKeyDelay"] := NormalizeDelay(skillKeyDelayEdit.Text, flow["skillKeyDelay"], 1000)
            flow["heroSelectDelay"] := NormalizeDelay(heroSelectDelayEdit.Text, flow["heroSelectDelay"], 1000)
            flow["npcClickDelay"] := NormalizeDelay(npcClickDelayEdit.Text, flow["npcClickDelay"], 1000)
            flow["chatDelay"] := NormalizeDelay(chatDelayEdit.Text, flow["chatDelay"], 5000)
            flow["teleportKeyDelay"] := NormalizeDelay(teleportKeyDelayEdit.Text, flow["teleportKeyDelay"], 5000)
            flow["mouseMoveDelay"] := NormalizeDelay(mouseMoveDelayEdit.Text, flow["mouseMoveDelay"], 1000)
            flow["releaseMouseMoveDelay"] := NormalizeDelay(releaseMouseMoveDelayEdit.Text, flow["releaseMouseMoveDelay"], 1000)
            keyDelayMs := flow["keyDelay"]
            skillKeyDelayMs := flow["skillKeyDelay"]
            heroSelectDelayMs := flow["heroSelectDelay"]
            npcClickDelayMs := flow["npcClickDelay"]
            chatDelayMs := flow["chatDelay"]
            teleportKeyDelayMs := flow["teleportKeyDelay"]
            mouseMoveDelayMs := flow["mouseMoveDelay"]
            releaseMouseMoveDelayMs := flow["releaseMouseMoveDelay"]
            LoadDelayControlsFromFlow(flow)
        }
    }
}

CaptureFarmTarget(name, *) {
    global farms, farmRows
    SaveFarmRow(name)
    MouseGetPos &x, &y
    farms[name]["targetX"] := x
    farms[name]["targetY"] := y
    farmRows[name]["targetX"].Text := x
    farmRows[name]["targetY"].Text := y
    SaveConfig()
    SetStatus("已保存 " name " 的技能鼠标点：" x ", " y "。")
}

OnFlowChanged(*) {
    global flowDDL, currentFlowSlot, suppressFlowChange
    if suppressFlowChange {
        return
    }
    oldSlot := currentFlowSlot
    newSlot := flowDDL.Value
    if oldSlot && oldSlot != newSlot {
        SaveCurrentFlowToMemory(oldSlot, false, false)
        ApplyFlowHotkeys()
    }
    RefreshFlowNamesControl(newSlot)
    LoadFlowToControls(newSlot)
    UpdateTuningInfo()
}

LoadFlowToControls(slot) {
    global currentFlowSlot
    global flows, flowNameEdit, flowEnabledCB, flowHotkeyEdit, groupRows
    global preTypeOptions, farmNames
    global suppressDurationRefresh
    currentFlowSlot := slot
    flow := flows[slot]
    suppressDurationRefresh := true
    try {
        flowNameEdit.Text := flow["name"]
        flowEnabledCB.Value := flow["enabled"]
        flowHotkeyEdit.Text := flow["hotkey"]
        LoadDelayControlsFromFlow(flow, false)
        farmListWithNone := ArrayWithNone(farmNames)

        Loop groupRows.Length {
            row := groupRows[A_Index]
            g := flow["groups"][A_Index]
            row["enabled"].Value := g["enabled"]
            row["preType"].Choose(IndexOf(preTypeOptions, g["preType"], 1))
            row["preValue"].Text := g["preValue"]
            row["farm"].Delete()
            row["farm"].Add(farmListWithNone)
            row["farm"].Choose(IndexOf(farmListWithNone, g["farm"], 1))
            used := ComputeGroupActionDuration(slot, A_Index)
            waitMs := GetGroupWait(slot, A_Index, used)
            g["duration"] := used + waitMs
            row["used"].Text := used
            row["duration"].Text := g["duration"]
            row["wait"].Text := waitMs
        }
    } finally {
        suppressDurationRefresh := false
    }
    RefreshGroupDurationDisplay()
    UpdateTuningInfo()
}

RefreshFlowNamesControl(selectedSlot := 0) {
    global flowDDL, flowNames, suppressFlowChange
    if selectedSlot <= 0 {
        selectedSlot := GetCurrentFlowSlot()
    }
    suppressFlowChange := true
    try {
        try {
            flowDDL.Delete()
            flowDDL.Add(flowNames)
            flowDDL.Choose(selectedSlot)
        }
    } finally {
        suppressFlowChange := false
    }
}

LoadDelayControlsFromFlow(flow, refresh := true) {
    global keyDelayEdit, skillKeyDelayEdit, heroSelectDelayEdit, npcClickDelayEdit, chatDelayEdit, teleportKeyDelayEdit, mouseMoveDelayEdit, releaseMouseMoveDelayEdit
    try keyDelayEdit.Text := flow["keyDelay"]
    try skillKeyDelayEdit.Text := flow["skillKeyDelay"]
    try heroSelectDelayEdit.Text := flow["heroSelectDelay"]
    try npcClickDelayEdit.Text := flow["npcClickDelay"]
    try chatDelayEdit.Text := flow["chatDelay"]
    try teleportKeyDelayEdit.Text := flow["teleportKeyDelay"]
    try mouseMoveDelayEdit.Text := flow["mouseMoveDelay"]
    try releaseMouseMoveDelayEdit.Text := flow["releaseMouseMoveDelay"]
    if refresh {
        RefreshGroupDurationDisplay()
    }
}

RefreshGroupDurationDisplay(*) {
    global flows, groupRows, suppressDurationRefresh
    if suppressDurationRefresh {
        return
    }
    slot := GetCurrentFlowSlot()
    if !slot || !flows.Has(slot) {
        return
    }
    suppressDurationRefresh := true
    try {
        SyncFarmDraftForDuration()
        SyncFlowDraftForDuration(slot)
        Loop groupRows.Length {
            row := groupRows[A_Index]
            used := ComputeGroupActionDuration(slot, A_Index)
            g := flows[slot]["groups"][A_Index]
            currentWait := GetGroupWait(slot, A_Index, used)
            waitFocused := IsControlFocused(row["wait"])
            try currentWait := ToInt(row["wait"].Text, currentWait)
            waitMs := Clamp(currentWait, 0, 30000)
            g["wait"] := waitMs
            duration := used + waitMs
            g["duration"] := duration
            try row["used"].Text := used
            try row["duration"].Text := duration
            if !waitFocused {
                try row["wait"].Text := waitMs
            }
        }
    } finally {
        suppressDurationRefresh := false
    }
}

IsControlFocused(ctrl) {
    try {
        return DllCall("GetFocus", "ptr") = ctrl.Hwnd
    } catch {
        return false
    }
}

SyncFarmDraftForDuration() {
    global farms, farmRows, farmNames
    for _, name in farmNames {
        if !farmRows.Has(name) || !farms.Has(name) {
            continue
        }
        row := farmRows[name]
        farm := farms[name]
        try farm["actionKey"] := NormalizeKey(row["actionKey"].Text)
        try farm["releaseType"] := NormalizeReleaseType(row["releaseType"].Text)
        try farm["releaseKey"] := NormalizeReleaseKeyForType(farm["releaseType"], row["releaseKey"].Text)
        try farm["targetX"] := NormalizeCoord(row["targetX"].Text)
        try farm["targetY"] := NormalizeCoord(row["targetY"].Text)
    }
}

SyncFlowDraftForDuration(slot) {
    global flows, groupRows
    global keyDelayEdit, skillKeyDelayEdit, heroSelectDelayEdit, npcClickDelayEdit, chatDelayEdit, teleportKeyDelayEdit, mouseMoveDelayEdit, releaseMouseMoveDelayEdit
    if !flows.Has(slot) {
        return
    }
    flow := flows[slot]
    try flow["keyDelay"] := NormalizeDelay(keyDelayEdit.Text, flow["keyDelay"], 1000)
    try flow["skillKeyDelay"] := NormalizeDelay(skillKeyDelayEdit.Text, flow["skillKeyDelay"], 1000)
    try flow["heroSelectDelay"] := NormalizeDelay(heroSelectDelayEdit.Text, flow["heroSelectDelay"], 1000)
    try flow["npcClickDelay"] := NormalizeDelay(npcClickDelayEdit.Text, flow["npcClickDelay"], 1000)
    try flow["chatDelay"] := NormalizeDelay(chatDelayEdit.Text, flow["chatDelay"], 5000)
    try flow["teleportKeyDelay"] := NormalizeDelay(teleportKeyDelayEdit.Text, flow["teleportKeyDelay"], 5000)
    try flow["mouseMoveDelay"] := NormalizeDelay(mouseMoveDelayEdit.Text, flow["mouseMoveDelay"], 1000)
    try flow["releaseMouseMoveDelay"] := NormalizeDelay(releaseMouseMoveDelayEdit.Text, flow["releaseMouseMoveDelay"], 1000)

    Loop groupRows.Length {
        row := groupRows[A_Index]
        g := flow["groups"][A_Index]
        try g["enabled"] := row["enabled"].Value
        try g["preType"] := row["preType"].Text
        try g["preValue"] := NormalizeFlowPreValue(g["preType"], row["preValue"].Text)
        try g["farm"] := NormalizeFarmName(row["farm"].Text)
    }
}

GetGroupDuration(slot, idx, minUsed := 0) {
    global flows
    g := flows[slot]["groups"][idx]
    waitMs := GetGroupWait(slot, idx, minUsed)
    g["duration"] := minUsed + waitMs
    return g["duration"]
}

GetGroupWait(slot, idx, minUsed := 0) {
    global flows
    g := flows[slot]["groups"][idx]
    if !g.Has("wait") {
        legacyDuration := g.Has("duration") ? ToInt(g["duration"], minUsed) : minUsed
        g["wait"] := Max(0, legacyDuration - minUsed)
    }
    g["wait"] := Clamp(ToInt(g["wait"], 0), 0, 30000)
    return g["wait"]
}

ComputeGroupActionDuration(slot, idx) {
    global flows
    flow := flows[slot]
    g := flow["groups"][idx]
    if !g["enabled"] {
        return 0
    }

    total := 0
    switch g["preType"] {
        case "按键":
            preKey := NormalizeKey(g["preValue"])
            if preKey != "" {
                total += KeyActionDuration(preKey, flow) + GameKeyDispatchDurationMs(preKey)
            }
        case "公屏":
            if Trim(g["preValue"]) != "" {
                total += flow["chatDelay"]
            }
    }

    if g["farm"] != "无" {
        total += ComputeFarmActionDuration(g["farm"], flow)
    }
    return total
}

ComputeFarmActionDuration(name, flow) {
    global farmMeta, farms
    name := NormalizeFarmName(name)
    if !farmMeta.Has(name) || !farms.Has(name) {
        return 0
    }

    farm := farms[name]
    meta := farmMeta[name]
    ; The world-coordinate click now reuses the camera lock established by
    ; F9/F2/F3. Keep the fallback F1 path out of normal duration estimates;
    ; it is only used when the lock was lost at runtime.
    total := flow["mouseMoveDelay"] + NpcClickHoldDurationMs() + flow["npcClickDelay"]
    if meta["action"] != "只点击NPC" {
        actionKey := NormalizeKey(farm["actionKey"])
        if actionKey != "" {
            total += KeyActionDuration(actionKey, flow) + GameKeyDispatchDurationMs(actionKey)
        }
    }

    releaseType := NormalizeReleaseType(farm["releaseType"])
    if releaseType != "无" {
        releaseKey := ResolveReleaseKey(farm)
        if releaseKey != "" && IsPointConfigured(farm["targetX"], farm["targetY"]) {
            total += HeroSelectActionDuration(flow)
            total += flow["releaseMouseMoveDelay"]
            total += SkillKeyActionDuration(releaseKey, flow)
        }
    }
    return total
}

KeyActionDuration(key, flow) {
    key := NormalizeKey(key)
    if key = "" {
        return 0
    }
    return IsTeleportKey(key) ? flow["teleportKeyDelay"] : flow["keyDelay"]
}

GameKeyDispatchDurationMs(key := "") {
    ; SendGameKey holds ordinary/menu keys for this fixed interval before the
    ; configurable post-key delay is applied.
    return InStr(NormalizeKey(key), "{") ? 0 : 15
}

NpcClickHoldDurationMs() {
    ; GameLeftClick holds the mouse button down for 10ms.
    return 10
}

SkillKeyActionDuration(key, flow) {
    key := NormalizeKey(key)
    if key = "" {
        return 0
    }
    if IsTeleportKey(key) {
        return flow["teleportKeyDelay"]
    }
    return Max(flow["skillKeyDelay"], ReleaseKeyMinHoldMs(key))
}

HeroSelectActionDuration(flow) {
    return 2 * Max(flow["heroSelectDelay"], HeroSelectMinHoldMs())
        + HeroCameraLockGapMs()
        + HeroCameraSettleDurationMs()
}

HeroCameraLockGapMs() {
    return 16
}

HeroCameraSettleDurationMs() {
    global worldProjection
    return ToInt(worldProjection["f1SettleMs"], 40)
}

AdjustKeyDelay(direction, *) {
    AdjustFlowDelay("key", direction)
}

AdjustFlowDelay(kind, direction, *) {
    global flows, isRunning
    if isRunning {
        SetStatus("流程执行中，结束后再调参。")
        return
    }
    SaveCurrentFlowToMemory()
    slot := GetCurrentFlowSlot()
    step := GetTuneStep()
    flow := flows[slot]
    field := GetDelayField(kind)
    maxValue := GetDelayUpperLimit(kind)
    flow[field] := Clamp(flow[field] + direction * step, 0, maxValue)
    LoadDelayControlsFromFlow(flow)
    SaveConfig()
    UpdateTuningInfo()
    SetStatus("已调整当前流程" GetDelayLabel(kind) "：" flow[field] "ms。")
}

AdjustKeyDelayFixed(direction, *) {
    AdjustFlowDelayByStep("key", direction, 10)
}

AdjustFlowDelayFixed(kind, direction, *) {
    AdjustFlowDelayByStep(kind, direction, 10)
}

AdjustKeyDelayByStep(direction, step) {
    AdjustFlowDelayByStep("key", direction, step)
}

AdjustFlowDelayByStep(kind, direction, step) {
    global flows, isRunning
    if isRunning {
        SetStatus("流程执行中，结束后再调参。")
        return
    }
    SaveCurrentFlowToMemory()
    slot := GetCurrentFlowSlot()
    flow := flows[slot]
    field := GetDelayField(kind)
    maxValue := GetDelayUpperLimit(kind)
    flow[field] := Clamp(flow[field] + direction * step, 0, maxValue)
    LoadDelayControlsFromFlow(flow)
    SaveConfig()
    UpdateTuningInfo()
    SetStatus("已调整当前流程" GetDelayLabel(kind) "：" flow[field] "ms。")
}

AdjustGroupDelay(direction, *) {
    global flows, groupRows, isRunning
    if isRunning {
        SetStatus("流程执行中，结束后再调参。")
        return
    }
    SaveCurrentFlowToMemory()
    slot := GetCurrentFlowSlot()
    idx := GetTuneGroupIndex()
    step := GetTuneStep()
    used := ComputeGroupActionDuration(slot, idx)
    g := flows[slot]["groups"][idx]
    g["wait"] := Clamp(GetGroupWait(slot, idx, used) + direction * step, 0, 30000)
    g["duration"] := used + g["wait"]
    RefreshGroupDurationDisplay()
    SaveConfig()
    UpdateTuningInfo()
    SetStatus("已调整 ID" idx " 等待：" g["wait"] "ms；组合时长：" g["duration"] "ms。")
}

AdjustGroupDelayFixed(idx, direction, *) {
    AdjustGroupDelayByStep(idx, direction, 10)
}

AdjustGroupDelayByStep(idx, direction, step) {
    global flows, groupRows, isRunning
    if isRunning {
        SetStatus("流程执行中，结束后再调参。")
        return
    }
    SaveCurrentFlowToMemory()
    slot := GetCurrentFlowSlot()
    idx := Clamp(ToInt(idx, 1), 1, 8)
    used := ComputeGroupActionDuration(slot, idx)
    g := flows[slot]["groups"][idx]
    g["wait"] := Clamp(GetGroupWait(slot, idx, used) + direction * step, 0, 30000)
    g["duration"] := used + g["wait"]
    RefreshGroupDurationDisplay()
    SaveConfig()
    UpdateTuningInfo()
    SetStatus("已调整 ID" idx " 等待：" g["wait"] "ms；组合时长：" g["duration"] "ms。")
}

AutoTuneKeyDelay(*) {
    AutoTuneDelay("key")
}

AutoTuneGroupDelay(*) {
    AutoTuneDelay("group")
}

AutoTuneDelay(kind) {
    global flows, isRunning
    if isRunning {
        SetStatus("流程执行中，结束后再调参。")
        return
    }

    SaveCurrentFlowToMemory()
    SaveAllFarmRows()
    slot := GetCurrentFlowSlot()
    idx := GetTuneGroupIndex()
    step := GetTuneStep()
    maxValue := GetTuneMax(kind)
    label := kind = "group" ? "ID" idx " 等待" : GetDelayLabel(kind)
    startValue := GetTuneValue(kind, slot, idx)

    if maxValue < startValue {
        SetStatus("自动调参失败：上限ms不能小于当前" label "。")
        return
    }

    confirm := MsgBox("将从当前 " label " = " startValue "ms 开始，每轮增加 " step "ms，最高试到 " maxValue "ms。`n每轮结束后点“是”=成功并采用；点“否”=失败继续加；点“取消”=停止。`n开始前请确认已绑定游戏窗口，或游戏窗口能被脚本识别。", "自动调参测试", "YesNo")
    if confirm != "Yes" {
        SetStatus("自动调参已取消。")
        return
    }

    value := startValue
    while value <= maxValue {
        SetTuneValue(kind, slot, idx, value)
        SaveConfig()
        UpdateTuningInfo()
        SetStatus("自动调参测试：" label " = " value "ms。")

        if !ActivateGameWindowForRun() {
            return
        }
        Sleep 250
        ExecuteFlow(slot)

        result := MsgBox("本轮参数：" label " = " value "ms。`n这轮是否成功？", "调参反馈", "YesNoCancel")
        if result = "Yes" {
            SetTuneValue(kind, slot, idx, value)
            SaveConfig()
            UpdateTuningInfo()
            SetStatus("自动调参完成：已采用 " label " = " value "ms。")
            return
        }
        if result = "Cancel" {
            SetStatus("自动调参已停止，当前保留 " label " = " value "ms。")
            return
        }
        value += step
    }

    SetStatus("自动调参结束：从 " startValue "ms 到 " maxValue "ms 都未标记成功。")
}

GetTuneValue(kind, slot, idx) {
    global flows
    flow := flows[slot]
    if kind != "group" {
        return flow[GetDelayField(kind)]
    }
    return GetGroupWait(slot, idx, ComputeGroupActionDuration(slot, idx))
}

SetTuneValue(kind, slot, idx, value) {
    global flows, groupRows
    flow := flows[slot]
    if kind != "group" {
        field := GetDelayField(kind)
        value := Clamp(value, 0, GetDelayUpperLimit(kind))
        flow[field] := value
        LoadDelayControlsFromFlow(flow)
        return
    }
    used := ComputeGroupActionDuration(slot, idx)
    value := Clamp(value, 0, 30000)
    flow["groups"][idx]["wait"] := value
    flow["groups"][idx]["duration"] := used + value
    RefreshGroupDurationDisplay()
}

GetTuneStep() {
    global tuneStepEdit
    step := 20
    try step := ToInt(tuneStepEdit.Text, step)
    step := Clamp(step, 1, 1000)
    try tuneStepEdit.Text := step
    return step
}

GetTuneGroupIndex() {
    global tuneGroupDDL
    idx := 1
    try idx := ToInt(tuneGroupDDL.Text, idx)
    return Clamp(idx, 1, 8)
}

GetTuneMax(kind) {
    global tuneMaxEdit
    defaultMax := kind = "group" ? 1000 : 300
    upperLimit := kind = "group" ? 30000 : GetDelayUpperLimit(kind)
    maxValue := defaultMax
    try maxValue := ToInt(tuneMaxEdit.Text, defaultMax)
    maxValue := Clamp(maxValue, 1, upperLimit)
    try tuneMaxEdit.Text := maxValue
    return maxValue
}

GetDelayField(kind) {
    switch kind {
        case "skillKey":
            return "skillKeyDelay"
        case "heroSelect":
            return "heroSelectDelay"
        case "click", "npcClick":
            return "npcClickDelay"
        case "chat":
            return "chatDelay"
        case "teleport":
            return "teleportKeyDelay"
        case "mouse":
            return "mouseMoveDelay"
        case "releaseMouse":
            return "releaseMouseMoveDelay"
        default:
            return "keyDelay"
    }
}

GetDelayLabel(kind) {
    switch kind {
        case "skillKey":
            return "技能键耗时"
        case "heroSelect":
            return "F1选英雄耗时"
        case "click", "npcClick":
            return "NPC点击耗时"
        case "chat":
            return "公屏动作耗时"
        case "teleport":
            return "F2/F3动作耗时"
        case "mouse":
            return "NPC移鼠耗时"
        case "releaseMouse":
            return "技能移鼠耗时"
        default:
            return "普通按键耗时"
    }
}

GetDelayUpperLimit(kind) {
    return (kind = "chat" || kind = "teleport") ? 5000 : 1000
}

UpdateTuningInfo(*) {
    global flows, tuneInfoText
    try {
        slot := GetCurrentFlowSlot()
        idx := GetTuneGroupIndex()
        flow := flows[slot]
        used := ComputeGroupActionDuration(slot, idx)
        waitMs := GetGroupWait(slot, idx, used)
        duration := used + waitMs
        tuneInfoText.Text := "当前：普通键" flow["keyDelay"] " / 技能键" flow["skillKeyDelay"] " / F1 " flow["heroSelectDelay"] " / F2F3 " flow["teleportKeyDelay"] " / NPC点" flow["npcClickDelay"] " / 公" flow["chatDelay"] " / NPC移" flow["mouseMoveDelay"] " / 技能移" flow["releaseMouseMoveDelay"] "ms`nID" idx " 动作占用 " used "ms；等待 " waitMs "ms；组合 " duration "ms"
    }
}

SaveCurrentFlowToMemory(slot := 0, refreshNames := true, refreshDisplay := true) {
    global flows, flowNames, flowNameEdit, flowEnabledCB, flowHotkeyEdit, groupRows
    global keyDelayEdit, skillKeyDelayEdit, heroSelectDelayEdit, npcClickDelayEdit, chatDelayEdit, teleportKeyDelayEdit, mouseMoveDelayEdit, releaseMouseMoveDelayEdit
    if slot <= 0 {
        slot := GetCurrentFlowSlot()
    }
    flow := flows[slot]
    flowName := NormalizeFlowName(flowNameEdit.Text)
    if flowName = "" {
        flowName := DefaultFlowName(slot)
    }
    flow["name"] := flowName
    flowNames[slot] := flowName
    flowNameEdit.Text := flowName
    flow["enabled"] := flowEnabledCB.Value
    flow["hotkey"] := NormalizeHotkey(flowHotkeyEdit.Text)
    flow["keyDelay"] := NormalizeDelay(keyDelayEdit.Text, flow["keyDelay"], 1000)
    flow["skillKeyDelay"] := NormalizeDelay(skillKeyDelayEdit.Text, flow["skillKeyDelay"], 1000)
    flow["heroSelectDelay"] := NormalizeDelay(heroSelectDelayEdit.Text, flow["heroSelectDelay"], 1000)
    flow["npcClickDelay"] := NormalizeDelay(npcClickDelayEdit.Text, flow["npcClickDelay"], 1000)
    flow["chatDelay"] := NormalizeDelay(chatDelayEdit.Text, flow["chatDelay"], 5000)
    flow["teleportKeyDelay"] := NormalizeDelay(teleportKeyDelayEdit.Text, flow["teleportKeyDelay"], 5000)
    flow["mouseMoveDelay"] := NormalizeDelay(mouseMoveDelayEdit.Text, flow["mouseMoveDelay"], 1000)
    flow["releaseMouseMoveDelay"] := NormalizeDelay(releaseMouseMoveDelayEdit.Text, flow["releaseMouseMoveDelay"], 1000)
    LoadDelayControlsFromFlow(flow, false)
    SyncFarmDraftForDuration()

    Loop groupRows.Length {
        row := groupRows[A_Index]
        g := flow["groups"][A_Index]
        g["enabled"] := row["enabled"].Value
        g["preType"] := row["preType"].Text
        g["preValue"] := NormalizeFlowPreValue(g["preType"], row["preValue"].Text)
        g["farm"] := row["farm"].Text
        used := ComputeGroupActionDuration(slot, A_Index)
        waitMs := Clamp(ToInt(row["wait"].Text, GetGroupWait(slot, A_Index, used)), 0, 30000)
        g["wait"] := waitMs
        g["duration"] := used + waitMs
    }
    if refreshDisplay {
        RefreshGroupDurationDisplay()
    }
    if refreshNames {
        RefreshFlowNamesControl(slot)
    }
}

OnNpcChanged(*) {
    global npcDDL
    LoadNpcToControls(npcDDL.Text)
}

LoadNpcToControls(name) {
    global npcs, npcXEdit, npcYEdit
    npc := npcs[name]
    npcXEdit.Text := npc["x"]
    npcYEdit.Text := npc["y"]
}

SaveCurrentNpc(*) {
    global npcDDL, npcs, npcXEdit, npcYEdit
    SaveGeneralSettings()
    name := npcDDL.Text
    npc := npcs[name]
    npc["x"] := NormalizeCoord(npcXEdit.Text)
    npc["y"] := NormalizeCoord(npcYEdit.Text)
    SaveConfig()
    SetStatus("已保存 NPC 标定：" name "。")
}

SaveCurrentNpcToMemory() {
    global npcDDL, npcs, npcXEdit, npcYEdit
    try name := npcDDL.Text
    catch {
        return
    }
    if name = "" || !npcs.Has(name) {
        return
    }
    npc := npcs[name]
    npc["x"] := NormalizeCoord(npcXEdit.Text)
    npc["y"] := NormalizeCoord(npcYEdit.Text)
    npcXEdit.Text := npc["x"]
    npcYEdit.Text := npc["y"]
}

CaptureNpcClickPoint(*) {
    global npcDDL, npcs, npcXEdit, npcYEdit
    MouseGetPos &x, &y
    name := npcDDL.Text
    npcs[name]["x"] := x
    npcs[name]["y"] := y
    npcXEdit.Text := x
    npcYEdit.Text := y
    SaveConfig()
    SetStatus("已记录 NPC点击坐标：" name " -> " x ", " y "。")
    QuietTip("NPC点 " x ", " y)
}

ShowKeyMapGui(*) {
    global mainGui, keyMap, itemSlotCount

    kmGui := Gui("+Owner" mainGui.Hwnd, "平台按键映射表")
    kmGui.SetFont("s9", "Microsoft YaHei UI")
    kmGui.AddText("x16 y12 w660", "这里不自动读取 KK 平台映射。可手填 Tab/Space，也可点每格旁边的“采”后按键采集；装备格不采坐标。")

    skillEdits := []
    itemEdits := []
    kmGui.AddGroupBox("x16 y44 w392 h150", "技能栏 4列 x 3行")
    Loop 3 {
        r := A_Index
        Loop 4 {
            c := A_Index
            idx := (r - 1) * 4 + c
            x := 36 + (c - 1) * 92
            y := 74 + (r - 1) * 34
            kmGui.AddText("x" x " y" y+4 " w22", idx)
            edit := kmGui.AddEdit("x" x+24 " y" y " w42", keyMap["skill" idx])
            capBtn := kmGui.AddButton("x" x+70 " y" y-1 " w30", "采")
            capBtn.OnEvent("Click", StartKeyCapture.Bind(edit, "技能" idx))
            skillEdits.Push(edit)
        }
    }

    kmGui.AddGroupBox("x430 y44 w220 h150", "装备栏 2列 x 3行")
    Loop 3 {
        r := A_Index
        Loop 2 {
            c := A_Index
            idx := (r - 1) * 2 + c
            x := 450 + (c - 1) * 96
            y := 74 + (r - 1) * 32
            kmGui.AddText("x" x " y" y+4 " w24", idx)
            edit := kmGui.AddEdit("x" x+26 " y" y " w42", keyMap["item" idx])
            capBtn := kmGui.AddButton("x" x+72 " y" y-1 " w30", "采")
            capBtn.OnEvent("Click", StartKeyCapture.Bind(edit, "装备" idx))
            itemEdits.Push(edit)
        }
    }

    saveBtn := kmGui.AddButton("x16 y244 w120", "保存映射表")
    saveBtn.OnEvent("Click", (*) => SaveKeyMapFromGui(kmGui, skillEdits, itemEdits))
    kmGui.AddButton("x150 y244 w90", "关闭").OnEvent("Click", (*) => kmGui.Destroy())
    kmGui.Show("w680 h300")
}

StartKeyCapture(editCtrl, label := "", *) {
    StartKeyCaptureInternal(editCtrl, label, false)
}

StartHotkeyCapture(editCtrl, label := "", *) {
    StartKeyCaptureInternal(editCtrl, label, true)
}

StartKeyCaptureInternal(editCtrl, label := "", captureHotkey := false) {
    global activeKeyCapture
    if IsObject(activeKeyCapture) {
        FinishKeyCapture("")
    }

    capGui := Gui("+AlwaysOnTop +ToolWindow", "按键采集")
    capGui.SetFont("s9", "Microsoft YaHei UI")
    capGui.AddText("x16 y16 w300", "正在采集：" label)
    capGui.AddText("x16 y44 w320", captureHotkey ? "请按触发键，可按 Ctrl/Alt/Shift + 键，也支持鼠标侧键。" : "请按一个键。支持 Tab、空格、鼠标中键和侧键。")
    capGui.Show("w340 h95")

    activeKeyCapture := Map(
        "edit", editCtrl,
        "label", label,
        "hotkey", captureHotkey,
        "gui", capGui,
        "done", false
    )
    RegisterMouseCaptureHotkeys()

    ih := InputHook("L1")
    ih.KeyOpt("{All}", "E")
    ih.KeyOpt("{LControl}{RControl}{LShift}{RShift}{LAlt}{RAlt}{LWin}{RWin}", "-E")
    activeKeyCapture["ih"] := ih
    ih.Start()
    ih.Wait()

    if !IsObject(activeKeyCapture) || activeKeyCapture["done"] {
        return
    }

    key := ih.EndKey
    if key = "" && ih.Input != "" {
        key := ih.Input
    }
    FinishKeyCapture(key)
}

RegisterMouseCaptureHotkeys() {
    global keyCaptureMouseHotkeys
    HotIf
    for _, hk in keyCaptureMouseHotkeys {
        clean := StrReplace(hk, "*", "")
        try Hotkey(hk, FinishKeyCapture.Bind(clean), "On")
    }
}

UnregisterMouseCaptureHotkeys() {
    global keyCaptureMouseHotkeys
    HotIf
    for _, hk in keyCaptureMouseHotkeys {
        try Hotkey(hk, "Off")
    }
}

FinishKeyCapture(rawKey := "", *) {
    global activeKeyCapture
    if !IsObject(activeKeyCapture) {
        return
    }

    cap := activeKeyCapture
    cap["done"] := true
    UnregisterMouseCaptureHotkeys()

    try cap["ih"].Stop()
    try cap["gui"].Destroy()

    key := cap["hotkey"] ? BuildCapturedHotkey(rawKey) : NormalizeKey(rawKey)
    label := cap["label"]
    editCtrl := cap["edit"]
    activeKeyCapture := ""

    if key = "" {
        SetStatus("未采集到按键。")
        return
    }
    editCtrl.Text := key
    RefreshGroupDurationDisplay()
    SetStatus("已采集按键：" label " -> " key "。")
}

BuildCapturedHotkey(rawKey) {
    key := NormalizeKey(rawKey)
    if key = "" {
        return ""
    }
    upper := StrUpper(key)
    if upper = "CTRL" || upper = "CONTROL" || upper = "SHIFT" || upper = "ALT" || upper = "LWIN" || upper = "RWIN" {
        return ""
    }

    prefix := ""
    if GetKeyState("Ctrl", "P") {
        prefix .= "^"
    }
    if GetKeyState("Alt", "P") {
        prefix .= "!"
    }
    if GetKeyState("Shift", "P") {
        prefix .= "+"
    }
    if GetKeyState("LWin", "P") || GetKeyState("RWin", "P") {
        prefix .= "#"
    }
    return NormalizeHotkey(prefix key)
}

SaveKeyMapFromGui(kmGui, skillEdits, itemEdits) {
    global keyMap
    Loop skillEdits.Length {
        keyMap["skill" A_Index] := NormalizeKey(skillEdits[A_Index].Text)
    }
    Loop itemEdits.Length {
        keyMap["item" A_Index] := NormalizeKey(itemEdits[A_Index].Text)
    }
    SaveConfig()
    SetStatus("已保存平台按键映射表。")
    kmGui.Destroy()
}

ApplyFlowHotkeys() {
    global registeredHotkeys, flows, flowCount, stopHotkey, globalEnabled

    UnregisterRegisteredHotkeys()
    DisableConfiguredHotkeyVariants()

    HotIf IsGameActive
    stopHk := NormalizeHotkey(stopHotkey)
    if !globalEnabled {
        HotIf
        return
    }

    if stopHk != "" {
        if IsReservedHotkey(stopHk) {
            SetStatus("停止热键不能使用保留热键：" stopHk "。")
        } else {
            try {
                runtimeStopHk := MakeRuntimeHotkey(stopHk)
                Hotkey(runtimeStopHk, HandleStopHotkey, "On")
                registeredHotkeys.Push(runtimeStopHk)
            } catch as err {
                SetStatus("停止热键无效：" stopHk "；" err.Message)
            }
        }
    }

    seenFlowHotkeys := Map()
    Loop flowCount {
        flow := flows[A_Index]
        hk := NormalizeHotkey(flow["hotkey"])
        if !flow["enabled"] || hk = "" {
            continue
        }
        if stopHk != "" && hk = stopHk {
            SetStatus("流程热键与停止热键冲突，已跳过流程：" flow["name"] " / " hk "。")
            continue
        }
        if seenFlowHotkeys.Has(hk) {
            SetStatus("流程热键重复，已跳过流程：" flow["name"] " / " hk "。")
            continue
        }
        seenFlowHotkeys[hk] := true
        if IsReservedHotkey(hk) {
            SetStatus("跳过保留热键：" hk "。")
            continue
        }
        try {
            runtimeHk := MakeRuntimeHotkey(hk)
            Hotkey(runtimeHk, ExecuteFlow.Bind(A_Index), "On")
            registeredHotkeys.Push(runtimeHk)
        } catch as err {
            SetStatus("热键无效：" hk "；" err.Message)
        }
    }
    HotIf
}

OpenSkillChatInput(*) {
    global chatInputOpen
    chatInputOpen := !chatInputOpen
}

CloseSkillChatInput(*) {
    global chatInputOpen
    chatInputOpen := false
}

DisableConfiguredHotkeyVariants() {
    global flows, flowCount, stopHotkey
    keys := []
    stopHk := NormalizeHotkey(stopHotkey)
    if stopHk != "" && !IsReservedHotkey(stopHk) {
        keys.Push(stopHk)
    }
    Loop flowCount {
        try hk := NormalizeHotkey(flows[A_Index]["hotkey"])
        catch {
            hk := ""
        }
        if hk != "" && !IsReservedHotkey(hk) {
            keys.Push(hk)
        }
    }

    HotIf IsGameActive
    for _, hk in keys {
        DisableHotkeyAllVariants(hk)
    }
    HotIf
    for _, hk in keys {
        DisableHotkeyAllVariants(hk)
    }
}

MakeRuntimeHotkey(hk, passThrough := false) {
    hk := NormalizeHotkey(hk)
    if hk = "" || SubStr(hk, 1, 1) = "$" || SubStr(hk, 1, 1) = "~" {
        return hk
    }
    return passThrough ? "~$" hk : "$" hk
}

UnregisterRegisteredHotkeys() {
    global registeredHotkeys
    if registeredHotkeys.Length = 0 {
        return
    }

    seen := Map()
    HotIf IsGameActive
    for _, hk in registeredHotkeys {
        if hk = "" || seen.Has(hk) {
            continue
        }
        seen[hk] := true
        DisableHotkeyAllVariants(hk)
    }
    HotIf

    ; Older builds disabled without the matching HotIf context, so also clear
    ; any accidental global variant of the same key.
    for hk, _ in seen {
        DisableHotkeyAllVariants(hk)
    }
    registeredHotkeys := []
}

DisableHotkeyAllVariants(hk) {
    base := StripHotkeyRuntimeDecorators(hk)
    variants := [hk, base, "$" base, "~" base, "~$" base, "*" base, "*$" base, "$*" base, "~*" base, "~*$" base, "*~$" base]
    seen := Map()
    for _, variant in variants {
        if variant = "" || seen.Has(variant) {
            continue
        }
        seen[variant] := true
        try Hotkey(variant, "Off")
    }
}

StripHotkeyRuntimeDecorators(hk) {
    hk := NormalizeHotkey(hk)
    loop {
        first := SubStr(hk, 1, 1)
        if first = "$" || first = "~" || first = "*" {
            hk := SubStr(hk, 2)
            continue
        }
        break
    }
    return hk
}

ExecuteFlow(slot, *) {
    global flows, isRunning, stopRequested, globalEnabled, runWarning, currentFlowSlot, headlessMode
    global activeKeyDelayMs, activeSkillKeyDelayMs, activeHeroSelectDelayMs, activeNpcClickDelayMs, activeChatDelayMs, activeTeleportKeyDelayMs, activeMouseMoveDelayMs, activeReleaseMouseMoveDelayMs, currentActionElapsedMs

    if headlessMode {
        ReloadConfigIfChanged()
    } else {
        SaveAllFarmRows()
        SaveCurrentNpcToMemory()
        SaveCurrentFlowToMemory(currentFlowSlot, false, false)
    }
    if !CanStartRun() {
        return
    }

    flow := flows[slot]
    activeKeyDelayMs := flow["keyDelay"]
    activeSkillKeyDelayMs := flow["skillKeyDelay"]
    activeHeroSelectDelayMs := flow["heroSelectDelay"]
    activeNpcClickDelayMs := flow["npcClickDelay"]
    activeChatDelayMs := flow["chatDelay"]
    activeTeleportKeyDelayMs := flow["teleportKeyDelay"]
    activeMouseMoveDelayMs := flow["mouseMoveDelay"]
    activeReleaseMouseMoveDelayMs := flow["releaseMouseMoveDelay"]
    isRunning := true
    stopRequested := false
    runWarning := ""
    flowFailed := false
    SetStatus("正在执行流程：" flow["name"] "。")

    try {
        for idx, g in flow["groups"] {
            if stopRequested {
                break
            }
            if !g["enabled"] {
                continue
            }
            currentActionElapsedMs := 0
            if !ExecutePreCommand(g) {
                flowFailed := true
                break
            }
            if stopRequested {
                break
            }
            if g["farm"] != "无" && !ExecuteFarmStep(g["farm"]) {
                flowFailed := true
                break
            }
            used := currentActionElapsedMs
            SleepInterrupt(GetGroupWait(slot, idx, used))
        }
    } finally {
        isRunning := false
        if stopRequested {
            SetStatus("流程已停止。")
        } else if flowFailed {
            SetStatus("流程已中止：当前步骤执行失败，请检查本局初始化和该步骤配置。")
        } else if runWarning != "" {
            SetStatus("流程执行完成，但有警告：" runWarning)
        } else {
            SetStatus("流程执行完成。")
        }
    }
}

ExecutePreCommand(g) {
    switch g["preType"] {
        case "按键":
            sentKey := SendKey(g["preValue"])
            if sentKey = "" {
                return false
            }
            KeyDelayFor(sentKey)
        case "公屏":
            if !SendChat(g["preValue"]) {
                return false
            }
            ChatDelay()
    }
    return true
}

ExecuteFarm(name) {
    global isRunning, stopRequested, runWarning
    global keyDelayMs, skillKeyDelayMs, heroSelectDelayMs, npcClickDelayMs, chatDelayMs, teleportKeyDelayMs, mouseMoveDelayMs, releaseMouseMoveDelayMs
    global activeKeyDelayMs, activeSkillKeyDelayMs, activeHeroSelectDelayMs, activeNpcClickDelayMs, activeChatDelayMs, activeTeleportKeyDelayMs, activeMouseMoveDelayMs, activeReleaseMouseMoveDelayMs, currentActionElapsedMs

    SaveGeneralSettings()
    SaveAllFarmRows()
    SaveCurrentNpcToMemory()
    if !CanStartRun() {
        return
    }

    activeKeyDelayMs := keyDelayMs
    activeSkillKeyDelayMs := skillKeyDelayMs
    activeHeroSelectDelayMs := heroSelectDelayMs
    activeNpcClickDelayMs := npcClickDelayMs
    activeChatDelayMs := chatDelayMs
    activeTeleportKeyDelayMs := teleportKeyDelayMs
    activeMouseMoveDelayMs := mouseMoveDelayMs
    activeReleaseMouseMoveDelayMs := releaseMouseMoveDelayMs
    currentActionElapsedMs := 0
    isRunning := true
    stopRequested := false
    runWarning := ""
    farmSucceeded := false
    SetStatus("正在执行刷本项：" name "。")
    try {
        farmSucceeded := ExecuteFarmStep(name)
    } finally {
        isRunning := false
        if stopRequested {
            SetStatus("刷本项已停止。")
        } else if !farmSucceeded {
            SetStatus("刷本项已中止：请检查本局初始化和该项配置。")
        } else if runWarning != "" {
            SetStatus("刷本项执行完成，但有警告：" runWarning)
        } else {
            SetStatus("刷本项执行完成。")
        }
    }
}

ExecuteFarmStep(name) {
    global farmMeta, farms
    name := NormalizeFarmName(name)
    if !farmMeta.Has(name) {
        SetStatus("未知刷本项：" name "。")
        return false
    }
    meta := farmMeta[name]
    if ExecuteNpcAction(meta["npc"], meta["action"], farms[name]["actionKey"]) {
        return ExecuteFarmRelease(name)
    }
    return false
}

ExecuteNpcAction(npcName, action, commandKey) {
    if !ClickWorldNpc(npcName) {
        return false
    }

    switch action {
        case "x20", "x10", "x5", "追捕", "去尾兽处", "命令键":
            if NormalizeKey(commandKey) = "" {
                SetStatus("NPC动作缺少按键：" npcName " / " action "。")
                return false
            }
            sentKey := SendKey(commandKey)
            if sentKey = "" {
                return false
            }
            KeyDelayFor(sentKey)
        case "只点击NPC":
            return true
    }
    return true
}

GetMatchingFarmActionKey(npcName, action) {
    global farmMeta, farms
    for farmName, meta in farmMeta {
        if meta["npc"] = npcName && meta["action"] = action {
            return farms[farmName]["actionKey"]
        }
    }
    return ""
}

FindFarmNameForNpcAction(npcName, action) {
    global farmMeta
    if npcName = "" || action = "" || npcName = "无" || action = "无" {
        return ""
    }
    if npcName = "家里挑战自我NPC" && action = "x20" {
        return "家里挑战自我x10"
    }
    for farmName, meta in farmMeta {
        if meta["npc"] = npcName && meta["action"] = action {
            return farmName
        }
    }
    return ""
}

ExecuteMatchingFarmRelease(npcName, action) {
    global farmMeta
    for farmName, meta in farmMeta {
        if meta["npc"] = npcName && meta["action"] = action {
            ExecuteFarmRelease(farmName)
            return
        }
    }
}

ExecuteFarmRelease(name) {
    global farms
    farm := farms[name]

    farm["releaseType"] := NormalizeReleaseType(farm["releaseType"])

    if farm["releaseType"] = "无" {
        return true
    }

    key := ResolveReleaseKey(farm)
    if key = "" {
        SetStatus(GetReleaseKeyError(name, farm))
        return false
    }

    if !IsPointConfigured(farm["targetX"], farm["targetY"]) {
        SetStatus("刷本项已配置自动释放，但未标定技能鼠标点：" name "。")
        return false
    }
    if !SelectHeroForRelease() {
        return false
    }
    MouseMove Integer(farm["targetX"]), Integer(farm["targetY"]), 0
    ReleaseMouseMoveDelay()
    return SendReleaseKey(key) != ""
}

KeyDelay() {
    global activeKeyDelayMs
    ActionDelayMs(activeKeyDelayMs)
}

KeyDelayFor(key) {
    global activeKeyDelayMs, activeTeleportKeyDelayMs
    ActionDelayMs(IsTeleportKey(key) ? activeTeleportKeyDelayMs : activeKeyDelayMs)
}

SkillKeyDelayFor(key) {
    global activeSkillKeyDelayMs, activeTeleportKeyDelayMs
    if IsTeleportKey(key) {
        ActionDelayMs(activeTeleportKeyDelayMs)
        return
    }
    ActionDelayMs(activeSkillKeyDelayMs)
}

SelectHeroForRelease() {
    global activeHeroSelectDelayMs, cameraLocked
    cameraLocked := false
    if !SendTimedGameKey("F1", activeHeroSelectDelayMs, HeroSelectMinHoldMs(), 80) {
        return false
    }
    ActionDelayMs(HeroCameraLockGapMs())
    if !SendTimedGameKey("F1", activeHeroSelectDelayMs, HeroSelectMinHoldMs(), 80) {
        return false
    }
    ActionDelayMs(HeroCameraSettleDurationMs())
    cameraLocked := true
    return true
}

HeroSelectDelay() {
    global activeHeroSelectDelayMs
    ActionDelayMs(activeHeroSelectDelayMs)
}

NpcClickDelay() {
    global activeNpcClickDelayMs
    ActionDelayMs(activeNpcClickDelayMs)
}

ChatDelay() {
    global activeChatDelayMs
    ActionDelayMs(activeChatDelayMs)
}

MouseMoveDelay() {
    global activeMouseMoveDelayMs
    ActionDelayMs(activeMouseMoveDelayMs)
}

ReleaseMouseMoveDelay() {
    global activeReleaseMouseMoveDelayMs
    ActionDelayMs(activeReleaseMouseMoveDelayMs)
}

ActionDelayMs(ms) {
    global currentActionElapsedMs
    ms := ToInt(ms, 0)
    if ms > 0 {
        currentActionElapsedMs += ms
        SleepInterrupt(ms)
    }
}

DelayMs(ms) {
    ms := ToInt(ms, 0)
    if ms > 0 {
        SleepInterrupt(ms)
    }
}

ResolveReleaseKey(farm) {
    global keyMap, skillSlotCount, itemSlotCount

    releaseType := NormalizeReleaseType(farm["releaseType"])
    raw := Trim(farm["releaseKey"])

    if releaseType = "技能槽位" {
        idx := ToInt(raw, 0)
        if idx < 1 || idx > skillSlotCount {
            return ""
        }
        return NormalizeKey(keyMap["skill" idx])
    }
    if releaseType = "装备槽位" {
        idx := ToInt(raw, 0)
        if idx < 1 || idx > itemSlotCount {
            return ""
        }
        return NormalizeKey(keyMap["item" idx])
    }
    return NormalizeKey(raw)
}

GetReleaseKeyError(name, farm) {
    global keyMap, skillSlotCount, itemSlotCount
    releaseType := NormalizeReleaseType(farm["releaseType"])
    raw := Trim(farm["releaseKey"])

    if releaseType = "技能按键" || releaseType = "装备按键" {
        return "刷本项缺少释放按键：" name "。"
    }

    if releaseType = "技能槽位" || releaseType = "装备槽位" {
        idx := ToInt(raw, 0)
        maxSlot := releaseType = "技能槽位" ? skillSlotCount : itemSlotCount
        slotName := releaseType = "技能槽位" ? "技能" : "装备"
        if idx < 1 || idx > maxSlot {
            return "刷本项" slotName "槽位编号必须是 1-" maxSlot "：" name "。"
        }
        mapKey := releaseType = "技能槽位" ? "skill" idx : "item" idx
        if NormalizeKey(keyMap[mapKey]) = "" {
            return "刷本项槽位缺少平台映射键：" name " / 第" idx "格。"
        }
    }

    return "刷本项缺少释放按键或槽位映射：" name "。"
}

CanStartRun() {
    global globalEnabled, isRunning
    if isRunning {
        QuietTip("已有流程在执行，停止热键可停止")
        return false
    }
    if !globalEnabled {
        QuietTip("宏已暂停")
        return false
    }
    if !IsGameActive() {
        QuietTip("游戏窗口未激活")
        return false
    }
    return true
}

SendChat(text) {
    text := Trim(text)
    if text = "" {
        return false
    }

    ; War3 public chat: open chat, enter command, submit. Keep this tight.
    SendInput "{Enter}"
    SendText text
    SendInput "{Enter}"
    return true
}

SendKey(key) {
    global cameraLocked
    key := NormalizeKey(key)
    if key = "" {
        return ""
    }
    if SendGameKey(key) {
        if IsTeleportKey(key) {
            ; F2/F3 are map travel actions that also lock the hero camera.
            cameraLocked := true
        }
        return key
    }
    return ""
}

SendReleaseKey(key) {
    global activeSkillKeyDelayMs, activeTeleportKeyDelayMs, cameraLocked
    key := NormalizeKey(key)
    if key = "" {
        return ""
    }
    durationMs := IsTeleportKey(key) ? activeTeleportKeyDelayMs : activeSkillKeyDelayMs
    if SendTimedGameKey(key, durationMs, ReleaseKeyMinHoldMs(key), ReleaseKeyMaxHoldMs(key, durationMs)) {
        if IsTeleportKey(key) {
            cameraLocked := true
        }
        return key
    }
    return ""
}

HeroSelectMinHoldMs() {
    return 50
}

ReleaseKeyMinHoldMs(key) {
    return IsTeleportKey(key) ? 50 : 0
}

ReleaseKeyMaxHoldMs(key, durationMs := 0) {
    return IsTeleportKey(key) ? 100 : ToInt(durationMs, 0)
}

SendTimedGameKey(key, durationMs, minHoldMs := 30, maxHoldMs := 100) {
    key := NormalizeKey(key)
    if key = "" {
        return false
    }

    sendName := GetGameKeySendName(key)
    durationMs := ToInt(durationMs, 0)
    minHoldMs := Clamp(ToInt(minHoldMs, 30), 0, 30000)
    maxHoldMs := Clamp(ToInt(maxHoldMs, 100), minHoldMs, 30000)
    effectiveMs := Max(durationMs, minHoldMs)
    holdMs := Clamp(effectiveMs, minHoldMs, maxHoldMs)
    restMs := Max(0, effectiveMs - holdMs)

    try {
        if InStr(key, "{") {
            SendEvent key
            ActionDelayMs(effectiveMs)
        } else {
            SendEvent "{" sendName " down}"
            try {
                ActionDelayMs(holdMs)
            } finally {
                SendEvent "{" sendName " up}"
            }
            ActionDelayMs(restMs)
        }
        return true
    } catch as err {
        try {
            if InStr(key, "{") {
                SendInput key
            } else {
                SendInput "{" sendName " down}"
                try {
                    ActionDelayMs(holdMs)
                } finally {
                    SendInput "{" sendName " up}"
                }
            }
            ActionDelayMs(restMs)
            return true
        } catch as err2 {
            SetRunWarning("发送按键失败：" key "；" err2.Message)
            return false
        }
    }
}

GetGameKeySendName(key) {
    key := NormalizeKey(key)
    try {
        sc := GetKeySC(key)
        if sc {
            return "sc" Format("{:03X}", sc)
        }
    }
    return key
}

SendGameKey(key, holdMs := 15) {
    holdMs := Clamp(ToInt(holdMs, 15), 0, 200)
    try {
        if InStr(key, "{") {
            SendEvent key
        } else {
            SendEvent "{" key " down}"
            if holdMs > 0 {
                Sleep holdMs
            }
            SendEvent "{" key " up}"
        }
        return true
    } catch as err {
        try {
            if InStr(key, "{") {
                SendInput key
            } else {
                SendInput "{" key "}"
            }
            return true
        } catch as err2 {
            SetRunWarning("发送按键失败：" key "；" err2.Message)
            return false
        }
    }
}

SetRunWarning(text) {
    global runWarning
    runWarning := text
    SetStatus(text)
}

GameLeftClick() {
    try {
        SendEvent "{LButton down}"
        ActionDelayMs(NpcClickHoldDurationMs())
        SendEvent "{LButton up}"
        return true
    } catch {
        try {
            Click "Left"
            return true
        } catch as err {
            SetRunWarning("发送鼠标点击失败：" err.Message)
            return false
        }
    }
}

SleepInterrupt(ms) {
    global stopRequested
    endTime := A_TickCount + ms
    while A_TickCount < endTime {
        if stopRequested {
            return
        }
        Sleep Min(30, endTime - A_TickCount)
    }
}

RequestStop(fromHotkey := false, *) {
    global stopRequested, globalEnabled, stopHotkeyLastTick
    stopRequested := true
    globalEnabled := false
    if !fromHotkey {
        stopHotkeyLastTick := 0
    }
    ApplyFlowHotkeys()
    StartPausedStopPolling()
    QuietTip("已停止并暂停触发")
    SetStatus("已停止当前流程，并暂停全部触发。连续按两次停止热键可重新启用。")
}

HandleStopHotkey(*) {
    global stopHotkeyLastTick, stopHotkeyDoubleTapMs
    now := A_TickCount
    if stopHotkeyLastTick > 0 && now - stopHotkeyLastTick <= stopHotkeyDoubleTapMs {
        stopHotkeyLastTick := 0
        ResumeFromStopHotkey()
        return
    }

    stopHotkeyLastTick := now
    RequestStop(true)
}

ResumeFromStopHotkey() {
    global globalEnabled, stopRequested, isRunning
    globalEnabled := true
    if !isRunning {
        stopRequested := false
    }
    StopPausedStopPolling()
    ApplyFlowHotkeys()
    QuietTip("已重新启用触发")
    SetStatus(isRunning ? "已重新启用触发；当前流程仍会完成停止。" : "已重新启用触发。")
}

ToggleGlobalEnabled(*) {
    global globalEnabled, stopRequested, isRunning
    globalEnabled := !globalEnabled
    if globalEnabled && !isRunning {
        stopRequested := false
    }
    if globalEnabled {
        StopPausedStopPolling()
    } else {
        StartPausedStopPolling()
    }
    ApplyFlowHotkeys()
    QuietTip(globalEnabled ? "宏已启用" : "宏已暂停")
    SetStatus(globalEnabled ? "全部映射已启用。" : "全部映射已暂停。")
    SoundBeep globalEnabled ? 1200 : 500, 80
}

StartPausedStopPolling() {
    global pausedStopHotkeyWasDown, stopHotkey
    pausedStopHotkeyWasDown := IsHotkeyPhysicallyDown(stopHotkey)
    SetTimer PollPausedStopHotkey, 20
}

StopPausedStopPolling() {
    global pausedStopHotkeyWasDown
    SetTimer PollPausedStopHotkey, 0
    pausedStopHotkeyWasDown := false
}

PollPausedStopHotkey(*) {
    global globalEnabled, pausedStopHotkeyWasDown, stopHotkey
    if globalEnabled {
        StopPausedStopPolling()
        return
    }
    if !IsGameActive() {
        pausedStopHotkeyWasDown := false
        return
    }
    isDown := IsHotkeyPhysicallyDown(stopHotkey)
    if isDown && !pausedStopHotkeyWasDown {
        HandlePausedStopTap()
    }
    pausedStopHotkeyWasDown := isDown
}

HandlePausedStopTap() {
    global stopHotkeyLastTick, stopHotkeyDoubleTapMs
    now := A_TickCount
    if stopHotkeyLastTick > 0 && now - stopHotkeyLastTick <= stopHotkeyDoubleTapMs {
        stopHotkeyLastTick := 0
        ResumeFromStopHotkey()
        return
    }
    stopHotkeyLastTick := now
}

IsHotkeyPhysicallyDown(hk) {
    hk := NormalizeHotkey(hk)
    if hk = "" {
        return false
    }

    needCtrl := InStr(hk, "^") > 0
    needAlt := InStr(hk, "!") > 0
    needShift := InStr(hk, "+") > 0
    needWin := InStr(hk, "#") > 0
    key := RegExReplace(hk, "[\^\!\+\#]")
    key := StripHotkeyRuntimeDecorators(key)

    if needCtrl && !GetKeyState("Ctrl", "P") {
        return false
    }
    if needAlt && !GetKeyState("Alt", "P") {
        return false
    }
    if needShift && !GetKeyState("Shift", "P") {
        return false
    }
    if needWin && !(GetKeyState("LWin", "P") || GetKeyState("RWin", "P")) {
        return false
    }

    try {
        if GetKeyState(key, "P") {
            return true
        }
    }
    try {
        return GetKeyState(GetGameKeySendName(key), "P")
    }
    return false
}

ToggleSkipGameCheck(*) {
    global skipGameCheck, skipGameCheckCB
    skipGameCheck := skipGameCheckCB.Value = 1
    SaveConfig()
    ApplyFlowHotkeys()
    SetStatus(skipGameCheck ? "已跳过窗口检测：热键会在任意前台窗口触发。" : "已启用游戏窗口检测。")
}

IsMacroHotkeysActive(*) {
    global globalEnabled
    return globalEnabled && IsGameActive()
}

IsGameActive(*) {
    global gameMatchers, gameWindowMatcher, skipGameCheck
    if skipGameCheck {
        return true
    }
    if gameWindowMatcher != "" && WinActive(gameWindowMatcher) {
        return true
    }
    for _, matcher in gameMatchers {
        if WinActive(matcher) {
            return true
        }
    }
    return false
}

ActivateGameWindowForRun() {
    global gameMatchers, gameWindowMatcher
    if gameWindowMatcher != "" && WinExist(gameWindowMatcher) {
        WinActivate gameWindowMatcher
        try WinWaitActive gameWindowMatcher, , 2
        if WinActive(gameWindowMatcher) {
            return true
        }
    }

    for _, matcher in gameMatchers {
        if WinExist(matcher) {
            WinActivate matcher
            try WinWaitActive matcher, , 2
            if WinActive(matcher) {
                return true
            }
        }
    }

    SetStatus("自动调参失败：找不到游戏窗口，请先点“绑定游戏窗口”。")
    return false
}

ReturnFocusToGameSoon() {
    SetTimer ReturnFocusToGame, -60
}

ReturnFocusToGame(*) {
    global gameMatchers, gameWindowMatcher
    if gameWindowMatcher != "" && WinExist(gameWindowMatcher) {
        try WinActivate gameWindowMatcher
        return
    }
    for _, matcher in gameMatchers {
        if WinExist(matcher) {
            try WinActivate matcher
            return
        }
    }
}

CopyActiveWindowInfo(*) {
    hwnd := GetForegroundWindowID()
    winTitle := hwnd ? "ahk_id " hwnd : "A"
    title := SafeWinGetTitle(winTitle)
    exe := SafeWinGetProcessName(winTitle)
    className := SafeWinGetClass(winTitle)
    pid := SafeWinGetPID(winTitle)
    A_Clipboard := "HWND: " hwnd "`nTitle: " title "`nExe: " exe "`nClass: " className "`nPID: " pid
    SetStatus("已复制当前窗口信息：HWND " hwnd " / PID " pid " / " WindowProcessLabel(exe) " / " title "。")
}

HasCommandLineArg(expected) {
    for _, value in A_Args {
        if StrLower(Trim(value)) = StrLower(expected) {
            return true
        }
    }
    return false
}

GetCommandLineArgValue(expected) {
    for index, value in A_Args {
        if StrLower(Trim(value)) = StrLower(expected) && index < A_Args.Length {
            return Trim(A_Args[index + 1])
        }
        prefix := StrLower(expected) "="
        if SubStr(StrLower(Trim(value)), 1, StrLen(prefix)) = prefix {
            return SubStr(Trim(value), StrLen(prefix) + 1)
        }
    }
    return ""
}

HandleScriptExit(exitReason, exitCode) {
    global gameSession, sessionPath
    try {
        if gameSession["bound"] {
            WriteGameSession("closed", "Fire Will 后台已关闭。", false)
        }
    }
    try {
        if gameSession.Has("processHandle") && gameSession["processHandle"] {
            DllCall("CloseHandle", "ptr", gameSession["processHandle"])
        }
    }
}

InitializeGameSession(*) {
    global gameSession, gameWindowMatcher, worldProjection, cameraLocked

    cameraLocked := false
    gameSession["bound"] := false
    gameSession["ready"] := false
    gameSession["projectionReady"] := false

    WriteGameSession("initializing", "正在绑定游戏窗口并读取本局参数。", false)
    if !FindAndBindGameWindow() {
        WriteGameSession("error", "绑定失败：找不到 War3 游戏窗口。", false)
        return false
    }

    hwnd := GetBoundGameHwnd()
    if !hwnd {
        WriteGameSession("error", "绑定失败：窗口句柄无效。", false)
        return false
    }
    metrics := ReadGameClientMetrics(hwnd)
    if !metrics.Has("width") || metrics["width"] <= 0 || metrics["height"] <= 0 {
        WriteGameSession("error", "绑定失败：无法读取游戏客户区。", false)
        return false
    }

    pid := GetWindowPid(hwnd)
    if !pid {
        WriteGameSession("error", "绑定失败：无法读取游戏 PID。", false)
        return false
    }

    gameSession["hwnd"] := hwnd
    gameSession["pid"] := pid
    gameSession["bound"] := true
    gameSession["clientLeft"] := metrics["left"]
    gameSession["clientTop"] := metrics["top"]
    gameSession["clientWidth"] := metrics["width"]
    gameSession["clientHeight"] := metrics["height"]
    gameSession["dpi"] := metrics["dpi"]
    gameSession["gameBase"] := FindRemoteModuleBase(pid, "Game.dll")
    gameSession["gameModuleName"] := gameSession["gameBase"] ? "Game.dll" : ""
    gameSession["ready"] := false
    gameSession["projectionReady"] := false

    if !gameSession["gameBase"] {
        message := "游戏窗口已绑定；Game.dll 读取失败，NPC 世界坐标点击暂不可用。"
        WriteGameSession("bound", message, false)
        SetStatus(message)
        ShowGameTip("游戏窗口已绑定`n等待 Game.dll", 1800)
        return false
    }

    if !LockHeroAndCamera() {
        message := "窗口已绑定，但无法向游戏发送 F1 完成人物和镜头锁定。"
        WriteGameSession("bound", message, false)
        SetStatus(message)
        ShowGameTip("初始化未完成`n无法锁定人物和镜头", 1800)
        return false
    }

    if !IsProjectionConfigured() {
        message := "本局初始化未完成：世界坐标投影参数不完整。"
        WriteGameSession("bound", message, false)
        SetStatus(message)
        ShowGameTip("初始化未完成`n投影参数不完整", 2200)
        return false
    }

    if !RefreshCameraSnapshot() {
        message := "本局初始化未完成：无法读取当前镜头参数。请确认权限和地图版本。"
        WriteGameSession("bound", message, false)
        SetStatus(message)
        ShowGameTip("初始化未完成`n无法读取镜头参数", 2200)
        return false
    }

    gameSession["ready"] := true
    gameSession["projectionReady"] := true
    message := "本局初始化完成：窗口、客户区、DPI、Game.dll 和镜头投影均可用。"
    WriteGameSession("ready", message, true)
    SetStatus(message)
    ShowGameTip("初始化已完成`n人物及镜头已锁定", 2200)
    return true
}

LockHeroAndCamera() {
    global gameSession, defaultHeroSelectDelayMs, worldProjection, cameraLocked
    cameraLocked := false
    hwnd := gameSession["hwnd"]
    if !hwnd || !WinExist("ahk_id " hwnd) {
        return false
    }

    try {
        if GetKeyState("F9", "P") {
            KeyWait "F9", "T1"
        }
        WinActivate "ahk_id " hwnd
        if !WinWaitActive("ahk_id " hwnd, , 1.5) {
            return false
        }
    } catch {
        return false
    }

    if !SendGameKey("F1", HeroSelectMinHoldMs()) {
        return false
    }
    Sleep Max(HeroCameraLockGapMs(), defaultHeroSelectDelayMs)
    if !SendGameKey("F1", HeroSelectMinHoldMs()) {
        return false
    }
    Sleep Max(40, ToInt(worldProjection["f1SettleMs"], 40))
    cameraLocked := true
    return true
}

GetBoundGameHwnd() {
    global gameWindowMatcher
    if gameWindowMatcher = "" {
        return 0
    }
    try {
        return WinExist(gameWindowMatcher)
    } catch {
        return 0
    }
}

GetWindowPid(hwnd) {
    pid := 0
    DllCall("GetWindowThreadProcessId", "ptr", hwnd, "uint*", &pid)
    return pid
}

ReadGameClientMetrics(hwnd) {
    result := Map()
    rect := Buffer(16, 0)
    point := Buffer(8, 0)
    if !DllCall("GetClientRect", "ptr", hwnd, "ptr", rect) {
        return result
    }
    if !DllCall("ClientToScreen", "ptr", hwnd, "ptr", point) {
        return result
    }
    result["left"] := NumGet(point, 0, "int")
    result["top"] := NumGet(point, 4, "int")
    result["width"] := NumGet(rect, 8, "int")
    result["height"] := NumGet(rect, 12, "int")
    result["dpi"] := 96
    try result["dpi"] := DllCall("GetDpiForWindow", "ptr", hwnd, "uint")
    return result
}

RefreshBoundWindowState() {
    global gameSession
    hwnd := gameSession["hwnd"]
    if !hwnd || !WinExist("ahk_id " hwnd) {
        InvalidateGameSession("游戏窗口已关闭或句柄已失效，程序会自动重新绑定，也可按 F9。")
        return false
    }

    pid := GetWindowPid(hwnd)
    if !pid || pid != gameSession["pid"] {
        InvalidateGameSession("War3 进程已变化，程序会自动重新绑定，也可按 F9。")
        return false
    }

    metrics := ReadGameClientMetrics(hwnd)
    if !metrics.Has("width") || metrics["width"] <= 0 || metrics["height"] <= 0 {
        InvalidateGameSession("无法读取当前游戏客户区，程序会自动重新绑定，也可按 F9。")
        return false
    }

    gameSession["clientLeft"] := metrics["left"]
    gameSession["clientTop"] := metrics["top"]
    gameSession["clientWidth"] := metrics["width"]
    gameSession["clientHeight"] := metrics["height"]
    gameSession["dpi"] := metrics["dpi"]
    return true
}

InvalidateGameSession(message) {
    global gameSession, cameraLocked
    gameSession["bound"] := false
    gameSession["ready"] := false
    gameSession["projectionReady"] := false
    cameraLocked := false
    if gameSession.Has("processHandle") && gameSession["processHandle"] {
        try DllCall("CloseHandle", "ptr", gameSession["processHandle"])
        gameSession.Delete("processHandle")
    }
    WriteGameSession("error", message, false)
    SetStatus(message)
}

FindRemoteModuleBase(pid, moduleName) {
    process := DllCall("OpenProcess", "uint", 0x0410, "int", false, "uint", pid, "ptr")
    if process {
        try {
            moduleBuffer := Buffer(A_PtrSize * 512, 0)
            needed := 0
            if DllCall("Psapi\EnumProcessModulesEx", "ptr", process, "ptr", moduleBuffer, "uint", moduleBuffer.Size, "uint*", &needed, "uint", 3) {
                count := Floor(needed / A_PtrSize)
                Loop count {
                    module := NumGet(moduleBuffer, (A_Index - 1) * A_PtrSize, "ptr")
                    nameBuffer := Buffer(520, 0)
                    if !DllCall("Psapi\GetModuleBaseNameW", "ptr", process, "ptr", module, "ptr", nameBuffer, "uint", 260) {
                        continue
                    }
                    currentName := StrGet(nameBuffer, "UTF-16")
                    if StrLower(currentName) != StrLower(moduleName) {
                        continue
                    }
                    info := Buffer(A_PtrSize * 2 + 8, 0)
                    if DllCall("Psapi\GetModuleInformation", "ptr", process, "ptr", module, "ptr", info, "uint", info.Size) {
                        return NumGet(info, 0, "ptr")
                    }
                }
            }
        } finally {
            DllCall("CloseHandle", "ptr", process)
        }
    }
    ; Psapi can reject a 32-bit target from a 64-bit caller. Toolhelp32 uses
    ; the target's MODULEENTRY32W layout and works for both architectures.
    snapshot := DllCall("kernel32\CreateToolhelp32Snapshot", "uint", 0x18, "uint", pid, "ptr")
    if !snapshot || snapshot = -1 {
        return 0
    }
    try {
        entry := Buffer(1080, 0)
        NumPut("uint", 1080, entry, 0)
        if !DllCall("kernel32\Module32FirstW", "ptr", snapshot, "ptr", entry) {
            return 0
        }
        loop {
            currentName := StrGet(entry.Ptr + 32, 256, "UTF-16")
            if StrLower(currentName) = StrLower(moduleName) {
                return NumGet(entry, 24, "uint")
            }
            if !DllCall("kernel32\Module32NextW", "ptr", snapshot, "ptr", entry) {
                break
            }
            NumPut("uint", 1080, entry, 0)
        }
    } finally {
        DllCall("CloseHandle", "ptr", snapshot)
    }
    return 0
}

OpenGameProcess() {
    global gameSession
    if gameSession.Has("processHandle") && gameSession["processHandle"] {
        return gameSession["processHandle"]
    }
    pid := ToInt(gameSession["pid"], 0)
    if !pid {
        return 0
    }
    handle := DllCall("OpenProcess", "uint", 0x0438, "int", false, "uint", pid, "ptr")
    if handle {
        gameSession["processHandle"] := handle
    }
    return handle
}

ReadRemoteBytes(address, size) {
    handle := OpenGameProcess()
    if !handle || !address || size <= 0 {
        return 0
    }
    data := Buffer(size, 0)
    read := 0
    if !DllCall("ReadProcessMemory", "ptr", handle, "ptr", address, "ptr", data, "uptr", size, "uptr*", &read) || read != size {
        return 0
    }
    return data
}

ReadRemoteFloat(address) {
    data := ReadRemoteBytes(address, 4)
    if !data {
        return ""
    }
    value := NumGet(data, 0, "float")
    return (value = value && Abs(value) < 1000000) ? value : ""
}

ReadRemotePtr(address) {
    data := ReadRemoteBytes(address, A_PtrSize)
    if !data {
        return 0
    }
    return NumGet(data, 0, "ptr")
}

ReadProjectionValue(offsetText, indirectDepth := 0) {
    global gameSession
    offsetText := Trim(offsetText)
    if offsetText = "" || !gameSession["gameBase"] {
        return ""
    }
    try offset := Integer(offsetText)
    catch {
        return ""
    }
    address := gameSession["gameBase"] + offset
    Loop Max(0, indirectDepth) {
        address := ReadRemotePtr(address)
        if !address {
            return ""
        }
    }
    return ReadRemoteFloat(address)
}

RefreshCameraSnapshot() {
    global gameSession, worldProjection
    if !gameSession["gameBase"] {
        return false
    }
    x := ReadProjectionValue(worldProjection["cameraXOffset"], worldProjection["cameraXIndirect"])
    y := ReadProjectionValue(worldProjection["cameraYOffset"], worldProjection["cameraYIndirect"])
    zoom := ReadProjectionValue(worldProjection["cameraZoomOffset"], worldProjection["cameraZoomIndirect"])
    if x = "" || y = "" {
        return false
    }
    gameSession["cameraX"] := x
    gameSession["cameraY"] := y
    gameSession["cameraZoom"] := zoom = "" || zoom <= 0 ? 1.0 : zoom
    return true
}

IsProjectionConfigured() {
    global worldProjection
    return Trim(worldProjection["pixelsPerWorld"]) != ""
        && Trim(worldProjection["cameraXOffset"]) != ""
        && Trim(worldProjection["cameraYOffset"]) != ""
}

WorldToClient(worldX, worldY, &clientX, &clientY) {
    global gameSession, worldProjection
    if !gameSession["ready"] || !IsProjectionConfigured() || !RefreshCameraSnapshot() {
        return false
    }
    pixelsPerWorld := ToFloat(worldProjection["pixelsPerWorld"], 0)
    if pixelsPerWorld <= 0 {
        return false
    }
    dx := ToFloat(worldX, 0) - gameSession["cameraX"]
    dy := ToFloat(worldY, 0) - gameSession["cameraY"]
    zoom := gameSession["cameraZoom"]
    axisX := worldProjection["axisX"]
    axisY := worldProjection["axisY"]
    clientX := gameSession["clientWidth"] / 2 + ((dx * axisX) + (dy * axisY)) * pixelsPerWorld * zoom + worldProjection["clickAnchorX"]
    clientY := gameSession["clientHeight"] / 2 + ((dx * axisY) - (dy * axisX)) * pixelsPerWorld * worldProjection["verticalScale"] * zoom + worldProjection["clickAnchorY"]
    return clientX >= 0 && clientX <= gameSession["clientWidth"] && clientY >= 0 && clientY <= gameSession["clientHeight"]
}

GetNpcSnapRadius() {
    global gameSession, worldProjection
    height := Max(1, ToInt(gameSession["clientHeight"], 1080))
    return Clamp(Round(worldProjection["snapRadius1080"] * height / 1080), 4, 200)
}

ClickWorldNpc(npcName) {
    global npcs, gameSession, worldProjection, cameraLocked
    if !gameSession["ready"] {
        SetStatus("本局正在自动初始化；如未完成，可按 F9 或点击“绑定游戏窗口并初始化”。")
        return false
    }
    if !RefreshBoundWindowState() {
        return false
    }
    if !npcs.Has(npcName) || npcs[npcName]["worldX"] = "" || npcs[npcName]["worldY"] = "" {
        SetStatus("NPC没有世界坐标配置：" npcName "。")
        return false
    }
    ; F9 and F2/F3 already lock the hero camera. Only use the old two-F1
    ; recovery path when that lock was not established for this session.
    if !cameraLocked {
        if !SendTimedGameKey("F1", activeHeroSelectDelayMs, HeroSelectMinHoldMs(), 80) {
            return false
        }
        ActionDelayMs(HeroCameraLockGapMs())
        if !SendTimedGameKey("F1", activeHeroSelectDelayMs, HeroSelectMinHoldMs(), 80) {
            return false
        }
        ActionDelayMs(HeroCameraSettleDurationMs())
        cameraLocked := true
    }
    if !IsProjectionConfigured() || !WorldToClient(npcs[npcName]["worldX"], npcs[npcName]["worldY"], &clientX, &clientY) {
        SetStatus("NPC不在当前可点击视野内，未发送鼠标点击：" npcName "。")
        return false
    }

    ; The projected point is the snap target. The radius scales with the
    ; client height, so 1080p/2K/4K keep the same physical hit tolerance.
    snapRadius := GetNpcSnapRadius()
    clientX := Clamp(Round(clientX), snapRadius, gameSession["clientWidth"] - snapRadius)
    clientY := Clamp(Round(clientY), snapRadius, gameSession["clientHeight"] - snapRadius)
    screenX := gameSession["clientLeft"] + clientX
    screenY := gameSession["clientTop"] + clientY
    MouseMove screenX, screenY, 0
    MouseMoveDelay()
    if !GameLeftClick() {
        return false
    }
    NpcClickDelay()
    return true
}

WriteGameSession(state, message, projectionReady := false) {
    global sessionPath, gameSession, worldProjection, sessionActiveLast, sessionBoundsLast
    gameSession["state"] := state
    gameSession["message"] := message
    gameSession["projectionReady"] := projectionReady
    gameSession["active"] := gameSession["bound"] && IsGameActive()
    sessionActiveLast := gameSession["active"] ? 1 : 0
    sessionBoundsLast := gameSession["clientLeft"] "|" gameSession["clientTop"] "|" gameSession["clientWidth"] "|" gameSession["clientHeight"]
    IniWrite(gameSession["bound"] ? 1 : 0, sessionPath, "Session", "bound")
    IniWrite(state = "ready" ? 1 : 0, sessionPath, "Session", "ready")
    IniWrite(state, sessionPath, "Session", "state")
    IniWrite(message, sessionPath, "Session", "message")
    IniWrite(gameSession["pid"], sessionPath, "Session", "pid")
    IniWrite(gameSession["hwnd"], sessionPath, "Session", "hwnd")
    IniWrite(gameSession["clientLeft"], sessionPath, "Session", "clientLeft")
    IniWrite(gameSession["clientTop"], sessionPath, "Session", "clientTop")
    IniWrite(gameSession["clientWidth"], sessionPath, "Session", "clientWidth")
    IniWrite(gameSession["clientHeight"], sessionPath, "Session", "clientHeight")
    IniWrite(gameSession["dpi"], sessionPath, "Session", "dpi")
    IniWrite(gameSession["active"] ? 1 : 0, sessionPath, "Session", "active")
    IniWrite(gameSession["gameBase"] ? Format("0x{:X}", gameSession["gameBase"]) : "", sessionPath, "Session", "moduleBase")
    IniWrite(gameSession["gameModuleName"], sessionPath, "Session", "moduleName")
    IniWrite(projectionReady ? 1 : 0, sessionPath, "Session", "projectionReady")
    IniWrite(FormatTime(A_Now, "yyyy-MM-dd HH:mm:ss"), sessionPath, "Session", "updatedAt")
}

BindGameWindowAfterDelay(*) {
    SetStatus("请在 3 秒内切到游戏窗口；也可以在游戏里按 Ctrl+Alt+B 直接绑定当前窗口。")
    QuietTip("3秒内切到游戏窗口", 2500)
    Sleep 3000
    BindActiveWindowAsGame()
}

BindActiveWindowAsGame(*) {
    hwnd := GetForegroundWindowID()
    if hwnd = "" || hwnd = 0 {
        hwnd := SafeWinGetID("A")
    }
    if BindWindowHwndAsGame(hwnd, "当前前台窗口") {
        return
    }
    FindAndBindGameWindow()
}

BindWindowHwndAsGame(hwnd, source := "窗口") {
    global gameWindowMatcher, gameSession, cameraLocked
    if hwnd = "" || hwnd = 0 {
        SetStatus("绑定失败：没有读到" source "句柄。")
        return false
    }

    winTitle := "ahk_id " hwnd
    title := SafeWinGetTitle(winTitle)
    exe := SafeWinGetProcessName(winTitle)
    className := SafeWinGetClass(winTitle)
    pid := SafeWinGetPID(winTitle)

    if IsMacroConfigWindow(hwnd, title, exe) {
        SetStatus("绑定失败：当前是宏配置器窗口。请切到游戏后按 Ctrl+Alt+B，或点绑定后3秒内切到游戏。")
        return false
    }

    if title = "" && exe = "" && className = "" {
        SetStatus("绑定失败：当前窗口信息为空，可能被权限或全屏模式挡住。请用管理员启动宏和游戏，或把魔兽改窗口化。")
        return false
    }

    gameWindowMatcher := "ahk_id " hwnd
    cameraLocked := false
    gameSession["bound"] := false
    gameSession["ready"] := false
    gameSession["projectionReady"] := false
    if gameSession.Has("processHandle") && gameSession["processHandle"] {
        try DllCall("CloseHandle", "ptr", gameSession["processHandle"])
        gameSession.Delete("processHandle")
    }
    SetStatus("已绑定游戏窗口(" source ")：" gameWindowMatcher " / PID " pid " / " WindowProcessLabel(exe) " / " title " / " className "。")
    QuietTip("已绑定游戏窗口", 1200)
    return true
}

WindowProcessLabel(exe) {
    return exe != "" ? exe : "进程名未读到(不影响句柄绑定)"
}

FindAndBindGameWindow(*) {
    global gameMatchers
    for _, matcher in gameMatchers {
        try hwnds := WinGetList(matcher)
        catch {
            continue
        }
        for _, hwnd in hwnds {
            title := SafeWinGetTitle("ahk_id " hwnd)
            exe := SafeWinGetProcessName("ahk_id " hwnd)
            if IsMacroConfigWindow(hwnd, title, exe) {
                continue
            }
            if BindWindowHwndAsGame(hwnd, "自动查找") {
                return true
            }
        }
    }
    SetStatus("绑定失败：没找到游戏窗口。请确认 War3 已启动并进入游戏画面。")
    QuietTip("未找到游戏窗口", 1200)
    return false
}

FindExistingGameWindow() {
    global gameMatchers
    for _, matcher in gameMatchers {
        try hwnds := WinGetList(matcher)
        catch {
            continue
        }
        for _, hwnd in hwnds {
            title := SafeWinGetTitle("ahk_id " hwnd)
            exe := SafeWinGetProcessName("ahk_id " hwnd)
            if !IsMacroConfigWindow(hwnd, title, exe) {
                return hwnd
            }
        }
    }
    return 0
}

GetForegroundWindowID() {
    try {
        return DllCall("GetForegroundWindow", "ptr")
    } catch {
        return 0
    }
}

IsMacroConfigWindow(hwnd, title, exe) {
    global mainGui, appTitle
    try {
        if hwnd = mainGui.Hwnd {
            return true
        }
    }
    normalizedExe := StrLower(exe)
    return InStr(title, appTitle)
        || normalizedExe = StrLower(A_ScriptName)
        || normalizedExe = "autohotkey64.exe"
        || normalizedExe = "autohotkey.exe"
        || normalizedExe = "war3_macro_gui.exe"
}

SafeWinGetID(winTitle := "A") {
    try {
        return WinGetID(winTitle)
    } catch {
        return 0
    }
}

SafeWinGetTitle(winTitle := "A") {
    try {
        return WinGetTitle(winTitle)
    } catch {
        return ""
    }
}

SafeWinGetProcessName(winTitle := "A") {
    try {
        name := WinGetProcessName(winTitle)
        if name != "" {
            return name
        }
    } catch {
    }
    pid := SafeWinGetPID(winTitle)
    return ProcessNameFromPID(pid)
}

SafeWinGetClass(winTitle := "A") {
    try {
        return WinGetClass(winTitle)
    } catch {
        return ""
    }
}

SafeWinGetPID(winTitle := "A") {
    try {
        pid := WinGetPID(winTitle)
        if pid {
            return pid
        }
    } catch {
    }
    try {
        hwnd := WinGetID(winTitle)
        pid := 0
        DllCall("GetWindowThreadProcessId", "ptr", hwnd, "uint*", &pid)
        return pid
    } catch {
        return 0
    }
}

ProcessNameFromPID(pid) {
    if !pid {
        return ""
    }
    try {
        name := ProcessGetName(pid)
        if name != "" {
            return name
        }
    }
    try {
        wmi := ComObjGet("winmgmts:")
        for proc in wmi.ExecQuery("SELECT Name FROM Win32_Process WHERE ProcessId=" pid) {
            return proc.Name
        }
    }
    return ""
}

IsReservedHotkey(hk) {
    normalized := StrUpper(NormalizeKey(hk))
    return normalized = "F9" || normalized = "ESC"
}

IsTeleportKey(key) {
    normalized := StrUpper(NormalizeKey(key))
    return normalized = "F2" || normalized = "F3"
}

ArrayWithNone(arr) {
    result := ["无"]
    for _, item in arr {
        result.Push(item)
    }
    return result
}

SetStatus(text) {
    global mainGui
    try mainGui["StatusText"].Text := "状态：" text
}

UpdateCurrentProfileLabel() {
    global currentProfileText, currentProfileName
    try currentProfileText.Text := "当前配置英雄：" currentProfileName
}

QuietTip(text, ms := 900) {
    ToolTip text
    SetTimer () => ToolTip(), -ms
}

ShowGameTip(text, ms := 1800) {
    global gameSession
    left := ToInt(gameSession["clientLeft"], 0)
    top := ToInt(gameSession["clientTop"], 0)
    width := Max(1, ToInt(gameSession["clientWidth"], 1))
    height := Max(1, ToInt(gameSession["clientHeight"], 1))
    x := left + Max(16, Round(width / 2) - 110)
    y := top + Max(16, Round(height * 0.08))
    ToolTip text, x, y, 2
    SetTimer HideGameTip, -ms
}

HideGameTip(*) {
    ToolTip "", 0, 0, 2
}

ToInt(value, defaultValue := 0) {
    try {
        return Integer(value)
    } catch {
        return defaultValue
    }
}

NormalizeDelay(value, defaultValue, maxValue) {
    return Clamp(ToInt(value, defaultValue), 0, maxValue)
}

NormalizeCoord(value) {
    value := Trim(value)
    if value = "" {
        return ""
    }
    return ToInt(value, "")
}

NormalizeWorldCoord(value) {
    value := Trim(value)
    if value = "" {
        return ""
    }
    try {
        return Float(value)
    } catch {
        return ""
    }
}

ToFloat(value, defaultValue := 0.0) {
    try {
        return Float(value)
    } catch {
        return defaultValue
    }
}

IsPointConfigured(x, y) {
    return NormalizeCoord(x) != "" && NormalizeCoord(y) != ""
}

GetDDLTextOrNone(ctrl) {
    try {
        text := Trim(ctrl.Text)
        return text != "" ? text : "无"
    } catch {
        return "无"
    }
}

WrapIndex(idx, count) {
    if count <= 0 {
        return 1
    }
    while idx < 1 {
        idx += count
    }
    while idx > count {
        idx -= count
    }
    return idx
}

Clamp(value, minValue, maxValue) {
    if value < minValue {
        return minValue
    }
    if value > maxValue {
        return maxValue
    }
    return value
}

NormalizeReleaseType(value) {
    value := Trim(value)
    if value = "物品按键" {
        return "装备按键"
    }
    return value
}

NormalizeReleaseKeyForType(releaseType, value) {
    releaseType := NormalizeReleaseType(releaseType)
    if releaseType = "技能槽位" || releaseType = "装备槽位" {
        return Trim(value)
    }
    return NormalizeKey(value)
}

NormalizeFlowPreValue(preType, value) {
    if preType = "按键" {
        return NormalizeKey(value)
    }
    return Trim(value)
}

NormalizeNpcCamera(name, value) {
    if name = "尾兽处追捕逃忍NPC" {
        return ""
    }
    return NormalizeKey(value)
}

NormalizeFarmName(value) {
    value := Trim(value)
    if value = "家里挑战自我x20" {
        return "家里挑战自我x10"
    }
    return value
}

NormalizeHotkey(value) {
    value := Trim(StrReplace(StrReplace(value, "`r", ""), "`n", ""))
    if value = "" {
        return ""
    }

    value := StrReplace(value, "＋", "+")
    value := RegExReplace(value, "\s*\+\s*", "+")
    parts := StrSplit(value, "+")
    if parts.Length = 1 {
        return NormalizeKey(value)
    }

    prefix := ""
    key := ""
    for _, part in parts {
        p := Trim(part)
        upper := StrUpper(p)
        switch upper {
            case "CTRL", "CONTROL", "控", "控制":
                if !InStr(prefix, "^") {
                    prefix .= "^"
                }
            case "ALT":
                if !InStr(prefix, "!") {
                    prefix .= "!"
                }
            case "SHIFT":
                if !InStr(prefix, "+") {
                    prefix .= "+"
                }
            case "WIN", "WINDOWS":
                if !InStr(prefix, "#") {
                    prefix .= "#"
                }
            default:
                key := NormalizeKey(p)
        }
    }
    return key != "" ? prefix key : NormalizeKey(value)
}

NormalizeKey(value) {
    raw := StrReplace(StrReplace(value, "`r", ""), "`n", "")
    if raw = "" {
        return ""
    }

    trimmed := Trim(raw, " `t")
    if trimmed = "" {
        return InStr(raw, "`t") ? "Tab" : "Space"
    }

    if RegExMatch(trimmed, "i)^\{([^{}]+)\}$", &match) {
        trimmed := match[1]
    }

    upper := StrUpper(trimmed)
    switch upper {
        case "TAB", "制表", "制表键":
            return "Tab"
        case "SPACE", "SPACEBAR", "空格", "空格键":
            return "Space"
        case "ESC", "ESCAPE":
            return "Esc"
    }
    return trimmed
}

IndexOf(arr, value, defaultIndex := 1) {
    for i, item in arr {
        if item = value {
            return i
        }
    }
    return defaultIndex
}
