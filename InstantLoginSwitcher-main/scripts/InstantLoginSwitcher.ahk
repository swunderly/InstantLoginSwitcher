#Requires AutoHotkey v2.0
#SingleInstance Ignore

primaryUser := "__PRIMARY_USER__"
secondaryUser := "__SECONDARY_USER__"
scriptPath := A_ScriptDir "\Switch-Login.ps1"

triggered := false

Numpad4:: {
    global triggered
    if GetKeyState("Numpad5", "P") && GetKeyState("Numpad6", "P") && !triggered {
        triggered := true
        RunSwitch()
    }
}

Numpad5:: {
    global triggered
    if GetKeyState("Numpad4", "P") && GetKeyState("Numpad6", "P") && !triggered {
        triggered := true
        RunSwitch()
    }
}

Numpad6:: {
    global triggered
    if GetKeyState("Numpad4", "P") && GetKeyState("Numpad5", "P") && !triggered {
        triggered := true
        RunSwitch()
    }
}

Numpad4 Up::ResetTrigger
Numpad5 Up::ResetTrigger
Numpad6 Up::ResetTrigger

ResetTrigger(*) {
    global triggered
    if !GetKeyState("Numpad4", "P") && !GetKeyState("Numpad5", "P") && !GetKeyState("Numpad6", "P") {
        triggered := false
    }
}

RunSwitch() {
    global scriptPath, primaryUser, secondaryUser
    cmd := Format(
        "powershell.exe -NoProfile -ExecutionPolicy Bypass -File ""{1}"" -PrimaryUser ""{2}"" -SecondaryUser ""{3}""",
        scriptPath,
        primaryUser,
        secondaryUser
    )
    Run(cmd, , "Hide")
}
