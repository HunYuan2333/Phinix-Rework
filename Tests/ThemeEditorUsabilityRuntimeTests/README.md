# Theme editor usability probe

Runs the production `ThemeSettingsWindow` projection, theme catalog and draft validation against the public presentation contracts. Only the engine window/publishing boundary is stubbed; this does not validate native layout, rendering or IME behavior.

Supply a directory containing the jointly built `Utils.dll` and `Google.Protobuf.dll` (net10):

```sh
dotnet run --project Tests/ThemeEditorUsabilityRuntimeTests/ThemeEditorUsabilityRuntimeTests.csproj -c Release -p:ReferencesRoot=/absolute/path/to/references -p:BaseOutputPath=/tmp/theme-editor-check/bin/ -p:BaseIntermediateOutputPath=/tmp/theme-editor-check/obj/
```

Use a fresh temporary output directory. No game launch, saved-game edits, package publication or network requests are performed by this probe.

Covers the default inline color picker, advanced hexadecimal/RGB controls, incomplete input recovery, opacity preservation, draft synchronization, browsing and persistence. Native wheel event/layout boundary checks live in `PresentationRuntimeTests/InlineColorPickerTests.cs`; actual game rendering still requires playtesting.
