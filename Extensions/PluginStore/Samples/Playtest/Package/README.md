# Phinix Store Playtest 1.1.0

Test-only extension `phinix.poc.playtest`, separate from the immutable inert marker.
It registers `IMainTabProvider` through `IExtensionBuilder.RegisterApi`, activates
and shuts down through the same discovery path as other extensions. The tab has
a click counter and a confirmation that places 100 silver near the current map
center. Use a test save: saving retains the silver. No network handlers, static
game references, persistent component or runtime assembly loader are included.

Build in the Phinix Rework checkout (or pass `-p:PhinixRoot=/absolute/checkout`):

```sh
dotnet build Extensions/PluginStore/Samples/Playtest/Playtest.csproj --configuration Release -p:BuildInParallel=false -m:1
python3 Extensions/PluginStore/Samples/Playtest/pack.py --assembly Extensions/PluginStore/Samples/Playtest/bin/Release/net472/Phinix.Store.Playtest.dll --output /tmp/phinix-store-playtest-1.1.0.zip
```

Do not copy reference DLLs into the package. Install through the staging store,
then enable the new mod in RimWorld and restart. See **Store test** in Phinix and
`Playtest: activated` in logs. A successful compile is not game validation.
Disable the extension and restart to verify that the tab disappears. To uninstall,
disable the **RimWorld mod** and restart first; extension disable alone cannot
unload a CLR assembly. The store checks managed dependencies and all owned bytes.
