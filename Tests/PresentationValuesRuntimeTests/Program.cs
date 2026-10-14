using System;
using System.Linq;
using PhinixClient.Framework;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string message)
    { assertions++; if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string message)
    { try { action(); } catch (ArgumentException) { assertions++; return; } throw new InvalidOperationException(message); }
    private static bool EngineType(Type type)
    {
        if (type.HasElementType) return EngineType(type.GetElementType());
        return type.Namespace == "UnityEngine" || type.Namespace == "Verse" || type.Namespace == "Waylith" ||
            type.IsGenericType && type.GetGenericArguments().Any(EngineType);
    }
    private static int Main()
    {
        try
        {
            Check(typeof(IClientSettingsWindowService).GetMethods().All(m => !EngineType(m.ReturnType) && m.GetParameters().All(p => !EngineType(p.ParameterType))), "Settings entry remains engine neutral.");
            Check(typeof(IClientGameWindowService).GetMethods().Single().GetParameters().Single().ParameterType.FullName == "Verse.Window", "Native window access is explicit and separate from settings.");
            Check(typeof(IClientGameWindowService).Assembly.GetType("PhinixClient.Framework.IClientWindowService") == null, "The mixed ordinary/native window contract is removed for the coordinated API migration.");
            var contractAssembly = typeof(IClientPresentationService).Assembly;
            Check(contractAssembly.GetName().Name == "ClientPresentation.Contracts", "Presentation contracts have a separate physical assembly.");
            Check(contractAssembly != typeof(IClientGameWindowService).Assembly, "Game contracts cannot share the neutral presentation assembly.");
            Check(!contractAssembly.GetReferencedAssemblies().Any(a => a.Name == "Assembly-CSharp" || a.Name == "ClientExtensionAbstractions" || a.Name == "PhinixClient" || a.Name.StartsWith("UnityEngine") || a.Name == "UnityStubs" || a.Name == "Waylith"), "Neutral assembly has no native or Host reference.");
            Check(typeof(IClientTheme).Assembly == contractAssembly && typeof(IClientActionSet).Assembly == contractAssembly && typeof(IClientPresentationImage).Assembly == contractAssembly, "Theme, image, actions and session retain one shared contract identity.");
            Check(typeof(IClientGameWindowService).Assembly.GetType(typeof(IClientPresentationService).FullName) == null, "The old contract assembly contains no duplicate presentation definition.");
            var bounds = new ClientPresentationBounds(-12.25f, 20.5f, 640.25f, 0f);
            Check(bounds.X == -12.25f && bounds.Y == 20.5f && bounds.Width == 640.25f && bounds.Height == 0f, "Logical coordinates are preserved without pixel rounding.");
            Check(new ClientPresentationBounds(0, 0, -1, float.NaN).Width == -1, "The value does not silently change the existing renderer's invalid-bounds policy.");
            Check(float.IsNaN(new ClientPresentationBounds(0, 0, 1, float.NaN).Height), "Nonfinite values are preserved for renderer validation.");
            foreach (var type in new[] { typeof(ClientPresentationBounds), typeof(ClientRgba) })
            {
                Check(type.GetProperties().All(p => p.PropertyType == typeof(float)), "Public values contain scalar floats only.");
                Check(type.GetMethods().All(m => !m.Name.StartsWith("op_")), "No implicit engine conversion or operator bypasses the boundary.");
            }
            Check(typeof(ClientPresentationRow).GetProperty("Image").PropertyType == typeof(IClientPresentationImage), "Row stores a neutral image source.");
            Check(typeof(ClientPresentationRow).GetProperty("Icon") == null && typeof(ClientPresentationRow).GetProperty("IconSource") == null, "Rows do not export engine textures or delegates.");
            foreach (var type in new[] { typeof(ClientPresentationBadge), typeof(ClientPresentationMedia), typeof(ClientPresentationComposer) })
            {
                Check(type.GetProperty("Image").PropertyType == typeof(IClientPresentationImage), "All ordinary image slots have the same neutral source.");
                Check(type.GetProperty("Icon") == null && type.GetProperty("Texture") == null, "Ordinary image slots do not export engine resources.");
                Check(type.GetConstructors().All(c => c.GetParameters().All(p => !p.ParameterType.FullName.StartsWith("UnityEngine."))), "Slot constructors do not accept engine resources.");
            }
            Check(typeof(IClientPresentationImageLease).GetProperties().Single().PropertyType == typeof(bool), "The ordinary lease has no native handle.");
            Reject(() => new ClientPresentationImageSize(0, 4), "Partial unknown dimensions are rejected.");
            Check(new ClientPresentationImageSize(64, 64).Width == 64, "Pixel metadata retains owner dimensions.");
            Check(typeof(IClientThemeRegistration).GetMethod("RegisterColor").GetParameters()[1].ParameterType == typeof(ClientRgba), "Theme registration accepts neutral RGBA.");
            Check(typeof(ClientThemeColor).GetProperty("DefaultColor").PropertyType == typeof(ClientRgba), "Catalog definitions do not export engine colors.");
            Check(typeof(ClientThemeColor).GetConstructors().Single().GetParameters().All(p => !p.ParameterType.FullName.StartsWith("UnityEngine.")), "Theme definition constructor does not accept engine colors.");
            foreach (var type in new[] { typeof(IClientThemeReader), typeof(IClientTheme), typeof(IClientThemeRegistration), typeof(IClientThemeCatalog), typeof(ClientThemeSnapshot), typeof(ClientThemeDraft), typeof(ClientThemeColorPreview), typeof(ClientThemeColor) })
            {
                Check(type.GetProperties().All(p => !EngineType(p.PropertyType)) &&
                    type.GetMethods().All(m => !EngineType(m.ReturnType) && m.GetParameters().All(p => !EngineType(p.ParameterType))) &&
                    type.GetConstructors().All(c => c.GetParameters().All(p => !EngineType(p.ParameterType))), "Theme values and nested generic containers do not leak engine types: " + type.Name);
            }
            var paletteValue = new ClientRgba(.1234567f, .2345678f, .3456789f, .456789f);
            var engineColor = paletteValue.ToUnityColor();
            Check(engineColor.r == paletteValue.Red && engineColor.g == paletteValue.Green && engineColor.b == paletteValue.Blue && engineColor.a == paletteValue.Alpha, "Current engine bridge preserves exact channels without native drawing.");
            var returnedColor = engineColor.ToClientRgba();
            Check(returnedColor.Red == paletteValue.Red && returnedColor.Green == paletteValue.Green && returnedColor.Blue == paletteValue.Blue && returnedColor.Alpha == paletteValue.Alpha, "Explicit engine bridge roundtrip preserves raw RGBA.");
            var paletteColors = new System.Collections.Generic.Dictionary<string, ClientRgba> { { "future.color", paletteValue } };
            var paletteFloats = new System.Collections.Generic.Dictionary<string, float> { { "future.float", .125f } };
            var snapshot = new ClientThemeSnapshot(77, paletteColors, paletteFloats);
            var inherited = new[] { "future.inherit" };
            var draft = new ClientThemeDraft("test", paletteColors, paletteFloats, inherited);
            paletteColors.Clear(); paletteFloats.Clear(); inherited[0] = "changed";
            Check(snapshot.Version == 77 && snapshot.GetColor("future.color").Alpha == paletteValue.Alpha && snapshot.GetFloat("future.float") == .125f, "Captured neutral palettes are defensive copies.");
            Check(draft.Colors["future.color"].Red == paletteValue.Red && draft.Floats["future.float"] == .125f && draft.InheritedKeys.Contains("future.inherit"), "Draft copies preserve unknown colors, floats and inherited keys.");
            draft.Colors.Clear(); Check(snapshot.GetColor("future.color").Blue == paletteValue.Blue, "Mutating a draft cannot affect an existing reader snapshot.");
            Check(!snapshot.TryGetColor("missing", out var absent) && absent.Red == 0 && absent.Alpha == 0, "Missing neutral colors retain TryGet default semantics.");
            Check(snapshot.GetFloat("missing", .75f) == .75f, "Missing numeric parameters retain caller fallback.");
            var menuMethod = typeof(IClientPresentationSession).GetMethod("CreateActionMenuItems");
            Check(menuMethod.ReturnType == typeof(System.Collections.Generic.IReadOnlyList<ClientPresentationMenuItem>) && !EngineType(menuMethod.ReturnType), "Session menu capture returns neutral read-only items.");
            Check(typeof(IClientPresentationSession).GetMethod("CreateActionMenuOptions") == null, "Session no longer exports a native FloatMenuOption method.");
            Check(typeof(ClientPresentationMenuItem).GetProperties().All(p => p.PropertyType == typeof(string) || p.PropertyType == typeof(Action)), "Neutral menu items export label and guarded UI request only.");
            Check(typeof(IClientPresentationSession).GetMethods().All(m => !EngineType(m.ReturnType) && m.GetParameters().All(p => !EngineType(p.ParameterType))), "Ordinary session signatures have no engine types, including nested menu containers.");
            var regionDraw = typeof(IClientPresentationRegion).GetMethod("Draw");
            Check(regionDraw.GetParameters().Single().ParameterType == typeof(IClientPresentationDrawContext), "Region no longer exposes engine Rect or a native draw target.");
            Check(typeof(IClientPresentationDrawContext).GetProperties().All(p => !EngineType(p.PropertyType)) && typeof(IClientPresentationDrawContext).GetMethods().All(m => !EngineType(m.ReturnType) && m.GetParameters().All(p => !EngineType(p.ParameterType))), "Draw context exposes only neutral bounds, theme and passive drawing.");
            Check(typeof(IClientPresentationDrawContext).GetMethods().Where(m => !m.IsSpecialName).Single().Name == "Progress", "Current drawing capability is limited to the real preview use case.");
            var draw = typeof(IClientPresentationSession).GetMethod("Draw");
            Check(draw.GetParameters().Single().ParameterType == typeof(ClientPresentationBounds), "Session entry accepts neutral bounds.");
            Check(typeof(ClientPresentationSwatch).GetProperty("Value").PropertyType == typeof(ClientRgba), "Swatch exposes neutral RGBA.");
            var rgba = new ClientRgba(.123f, .234f, .345f, .456f);
            var swatch = new ClientPresentationSwatch("sample", "sample", rgba);
            Check(swatch.Value.Red == rgba.Red && swatch.Value.Green == rgba.Green && swatch.Value.Blue == rgba.Blue && swatch.Value.Alpha == rgba.Alpha, "Swatch preserves all four channels without color-space changes.");
            Check(new ClientRgba(0, 0, 0).Alpha == 1, "Default alpha remains opaque.");
            Reject(() => new ClientPresentationSwatch("invalid", "", new ClientRgba(float.NaN, 0, 0)), "Nonfinite red is rejected.");
            Reject(() => new ClientPresentationSwatch("invalid", "", new ClientRgba(0, float.PositiveInfinity, 0)), "Nonfinite green is rejected.");
            Reject(() => new ClientPresentationSwatch("invalid", "", new ClientRgba(0, 0, -1)), "Negative blue is rejected.");
            Reject(() => new ClientPresentationSwatch("invalid", "", new ClientRgba(0, 0, 0, 2)), "Out-of-range alpha is rejected.");
            Console.WriteLine("Presentation values passed: " + assertions + " assertions; actual contracts, no GUI execution.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
