#Requires AutoHotkey v2.0
#SingleInstance Force

encodedPath := A_ScriptDir . "\switch-command.b64"
triggered := false

CheckCombo() {
    global triggered

    if triggered {
        return
    }

    if GetKeyState("Numpad4", "P") && GetKeyState("Numpad5", "P") && GetKeyState("Numpad6", "P") {
        triggered := true
        RunSwitch()
    }
}

Numpad4::CheckCombo()
Numpad5::CheckCombo()
Numpad6::CheckCombo()

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
    global encodedPath

    if !FileExist(encodedPath) {
        return
    }

    encoded := Trim(FileRead(encodedPath, "UTF-8"))
    if (encoded = "") {
        return
    }

    if (SubStr(encoded, 1, 1) = Chr(0xFEFF)) {
        encoded := SubStr(encoded, 2)
    }

    try {
        Run("powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " . encoded, , "Hide")
    }
    catch {
    }
}
