Set-StrictMode -Version Latest

$signature = @"
using System;
using System.Runtime.InteropServices;

public static class NativeCred {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CREDENTIAL {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32", SetLastError=true, CharSet=CharSet.Unicode)]
    public static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32", SetLastError=true)]
    public static extern void CredFree([In] IntPtr cred);

    [DllImport("advapi32", SetLastError=true, CharSet=CharSet.Unicode)]
    public static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] uint flags);
}
"@

if (-not ("NativeCred" -as [type])) {
    Add-Type -TypeDefinition $signature
}

function Write-StoredCredential {
    param(
        [Parameter(Mandatory)] [string]$Target,
        [Parameter(Mandatory)] [string]$UserName,
        [Parameter(Mandatory)] [SecureString]$Password
    )

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
    try {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringUni($bstr)
        $bytes = [Text.Encoding]::Unicode.GetBytes($plain)

        $cred = New-Object NativeCred+CREDENTIAL
        $cred.Type = 1 # CRED_TYPE_GENERIC
        $cred.TargetName = $Target
        $cred.UserName = $UserName
        $cred.Persist = 2 # CRED_PERSIST_LOCAL_MACHINE
        $cred.CredentialBlobSize = $bytes.Length
        $cred.CredentialBlob = [Runtime.InteropServices.Marshal]::StringToCoTaskMemUni($plain)

        try {
            if (-not [NativeCred]::CredWrite([ref]$cred, 0)) {
                throw "CredWrite failed with code $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
            }
        }
        finally {
            if ($cred.CredentialBlob -ne [IntPtr]::Zero) {
                [Runtime.InteropServices.Marshal]::ZeroFreeCoTaskMemUnicode($cred.CredentialBlob)
            }
        }
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

function Read-StoredCredential {
    param(
        [Parameter(Mandatory)] [string]$Target
    )

    $ptr = [IntPtr]::Zero
    if (-not [NativeCred]::CredRead($Target, 1, 0, [ref]$ptr)) {
        throw "CredRead failed for target '$Target' with code $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
    }

    try {
        $cred = [Runtime.InteropServices.Marshal]::PtrToStructure($ptr, [type]"NativeCred+CREDENTIAL")
        $password = ""
        if ($cred.CredentialBlob -ne [IntPtr]::Zero -and $cred.CredentialBlobSize -gt 0) {
            $password = [Runtime.InteropServices.Marshal]::PtrToStringUni($cred.CredentialBlob, $cred.CredentialBlobSize / 2)
        }

        [pscustomobject]@{
            Target   = $cred.TargetName
            UserName = $cred.UserName
            Password = $password
        }
    }
    finally {
        if ($ptr -ne [IntPtr]::Zero) {
            [NativeCred]::CredFree($ptr)
        }
    }
}

Export-ModuleMember -Function Write-StoredCredential, Read-StoredCredential
