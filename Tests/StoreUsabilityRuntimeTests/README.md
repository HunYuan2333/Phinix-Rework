# Store usability policy probe

Runs the compiled production Store policy methods against a supplied Host/game reference directory. Only translation data is injected. This does not execute native GUI, network, download, installation or management transactions.

```sh
dotnet run --project Tests/StoreUsabilityRuntimeTests/StoreUsabilityRuntimeTests.csproj -c Release -p:ReferencesRoot=/absolute/path/to/references -p:BaseOutputPath=/tmp/store-probe/bin/ -p:BaseIntermediateOutputPath=/tmp/store-probe/obj/ -- /absolute/path/to/references /absolute/path/to/Phinix-Rework
```

Prepare separate reference and output directories for 1.6 and 1.7. Recompile the probe for each game's exact `Assembly-CSharp` identity. The directory must include `PluginStore.Client.dll`, the Host contract/dependency assemblies under their assembly identity names, and game references. Native layout and business outcomes require separate game tests.
