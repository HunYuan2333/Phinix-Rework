# Theme packages: author, validate and install

Use a data-only theme package for colors. Do not add DLLs, game variants or another theme service. Source locators are relative to Host; replace command paths with actual absolute paths.

## Files and XML

Copy the [configuration](../templates/theme-package/theme-config.json) and [XML](../templates/theme-package/Themes/sample.xml), preserving their relative layout. Configuration fields are `packageId`, `name`, `author`, `version`, `phinixRange` and `themes`; each theme has `id`, `name`, `file`. The input file is relative to the configuration. Packaging generates `Themes/<id>.xml`; its name need not match the input filename.

```xml
<PhinixTheme version="3" name="Sample" author="Author">
  <color key="ui.background" r="0.08" g="0.12" b="0.10" a="1" />
  <color key="ui.text.primary" r="0.90" g="0.95" b="0.92" a="1" />
  <param key="chat.nameColor.saturation" value="0.65" />
  <inherit key="ui.surface" />
</PhinixTheme>
```

- Root: `PhinixTheme`, version `2` or `3`, optional name/author. Do not use `<theme>` or hexadecimal color strings.
- `color` requires key/r/g/b/a with finite channels in 0–1. `param` requires key/value and a finite number; its usable range also depends on the registration.
- Only version 3 supports `inherit`. Do not add DTDs, external entities, extra elements or duplicate keys.
- Known aliases such as `primaryText`, `background` and `nameColor.*` are rejected. Use `ui.text.primary`, `ui.background` and `chat.nameColor.*`.
- Valid custom keys can be preserved without being registered. Static validation does not establish visible effects. Check unused keys and current registrations in the editor. Host owns defaults/fallbacks through the same theme service.

## Package and statically validate

```sh
dotnet /path/to/ManagedPackageTool.dll --pack-theme /path/to/my-theme/theme-config.json /path/to/artifacts/my-theme-1.0.0.zip
dotnet /path/to/ManagedPackageTool.dll --validate-theme /path/to/artifacts/my-theme-1.0.0.zip
```

Use a fresh output path. The tool runs on .NET 10 without launching the game; building the theme tool does not require game DLLs. Source: `Extensions/PluginStore/Tools/ManagedPackageTool/ThemePackageCommand.cs`.

Success exits with 0 and emits JSON containing `management="phinix-theme"`, `validation="static-data-only"` and `installed=false`, not `Package validation successful.`. The ZIP contains root `manifest.json` (schemaVersion 1) and declared `Themes/*.xml` only. Compressed and expanded package limits are each 8 MiB; each XML is limited to 1 MiB. No DLLs, images or runtime variants.

The template uses `>=0.10.4 <1.0.0` for the corrected online interaction path. This is not the version that introduced the format. Choose the minimum from required capabilities and tested Hosts, not a product name.

## Game acceptance

Local theme import requires neither developer mode nor restart. Use the current local-theme entry point, for example the store's More menu. Inspect package name/version/digest/source, then install. Theme identity is `package/<packageId>/<themeId>`. Installation does not apply a theme: select a draft in the theme window and apply it. Verify hot switching, editing, export and reimport.

For online installation, filter themes and open details. Verify that clicking preserves selection, install is available and confirmation updates inventory. Do not silently adopt the source of a same-ID local import. Export and uninstall it before online installation when required. Removing an active theme should restore the default without leftover window backgrounds. Check text, fields, selection, scrollbars and complete window borders in light and dark themes.

Retain input after format/range/source failures; do not skip validation. Report refresh, installation, selection, application, export and removal separately. Tool success is not game acceptance.

## Publication and evidence

Themes currently use a separate schema4 feed with maintainer-reviewed publication, not the DLL hourly update policy. Any public GitHub author/organization can supply a theme subject to redistribution review. Index verifies original bytes and hosts identical distribution copies. Gateway maintainers own CF cache/credentials; theme authors do not supply server tokens. See [publication.md](publication.md).

Source: Common submodule `Common/Utils/Framework/Themes/{ThemePackage.cs,ThemePackageBuilder.cs,ManagedThemePackageStore.cs}`, Host `Client/Source/UI/{UiTheme.cs,ClientThemePackageService.cs}` and `Client/PresentationContracts/UI/{IClientTheme.cs,IClientThemePackages.cs}`.
