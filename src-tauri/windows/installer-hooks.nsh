; Themia NSIS installer hooks.
; Wired via tauri.conf.json -> bundle.windows.nsis.installerHooks.
;
; POSTINSTALL registers the "Themia" event source ([LOG-09]); POSTUNINSTALL
; unregisters it and opens the uninstall survey.

; [LOG-09] Register the event source so Event Viewer renders our ERROR records
; as their own text. Writing to the Application log works WITHOUT this key, but
; an unregistered source is displayed wrapped in "The description for Event ID
; 1000 from source Themia cannot be found", with the real message buried below
; it. EventMessageFile points at EventCreate.exe, the message table Windows
; ships for exactly this case (its `eventcreate` command registers custom
; sources the same way); it renders event ids 1..1000 as the insertion string
; verbatim. TypesSupported = 7 = ERROR | WARNING | INFORMATION.
;
; Needs HKLM, i.e. a per-machine (elevated) install. A per-user install just
; leaves the key absent and the entries render with the preamble, which is why
; the app never depends on this having run.
!macro NSIS_HOOK_POSTINSTALL
  WriteRegExpandStr HKLM "SYSTEM\CurrentControlSet\Services\EventLog\Application\Themia" \
    "EventMessageFile" "%SystemRoot%\System32\EventCreate.exe"
  WriteRegDWORD HKLM "SYSTEM\CurrentControlSet\Services\EventLog\Application\Themia" \
    "TypesSupported" 7
!macroend

; On a real (interactive) uninstall we open the uninstall survey in the user's
; default browser. This is the only uninstall signal Windows gives us; the app
; itself gets no uninstall callback (see docs/ANALYTICS.md), but the uninstaller
; can. The survey page lives on the website and embeds the zero-backend form.
;
; ${VERSION} is defined by Tauri's NSIS template, so the survey can break
; responses down by the version the user churned from.
!macro NSIS_HOOK_POSTUNINSTALL
  ; [LOG-09] Leave no orphaned event source behind. Past entries stay in the
  ; Application log (they are the log's, not ours) and simply lose the message
  ; file, same as any uninstalled app.
  DeleteRegKey HKLM "SYSTEM\CurrentControlSet\Services\EventLog\Application\Themia"

  ; Skip on silent runs. Tauri executes the OLD uninstaller with /S during an
  ; in-place update; without this guard the survey would pop on every update.
  IfSilent +2
  ExecShell "open" "https://themia.app/goodbye?v=${VERSION}"
!macroend
