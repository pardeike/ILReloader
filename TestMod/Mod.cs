using System.Runtime.CompilerServices;
using TestApplication;

namespace TestMod;

// Custom attribute to mark methods or constructors as reloadable
[AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method)]
internal class ReloadableAttribute : Attribute { }

public class Mod : IMod
{
    private ModDialog dialog = new();

    public IAppDialog GetDialog() => dialog;

    static Mod()
    {
        Console.WriteLine("--> TestMod.Mod static constructor");
    }

    public Mod()
    {
        Console.WriteLine("TestMod loaded");
    }
}

public class ModDialog : IAppDialog
{
    private DialogConfig myConfig;

    static ModDialog()
    {
        Console.WriteLine("--> TestMod.ModDialog static constructor");
    }

    [Reloadable]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Prepare(DialogConfig config)
    {
        Console.WriteLine("TEST 1x"); // for testing, edit this line and build so the dll changes
        myConfig = config;
    }

    [Reloadable]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Show()
    {
        Console.WriteLine("TEST 2x"); // for testing, edit this line and build so the dll changes
        Console.WriteLine($"Showing mod dialog with message: {myConfig.message}");
    }
}
