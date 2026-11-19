Import-Module Microsoft.PowerShell.LocalAccounts -ErrorAction Stop
[Environment]::Is64BitProcess
(Get-Command Get-LocalUser -ErrorAction Stop).Name
