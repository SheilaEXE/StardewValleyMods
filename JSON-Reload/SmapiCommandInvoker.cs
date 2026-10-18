using System.Reflection;
using StardewModdingAPI;

namespace JsonReload;

internal sealed class SmapiCommandInvoker
{
    private readonly ICommandHelper commandHelper;

    public SmapiCommandInvoker(ICommandHelper commandHelper)
    {
        this.commandHelper = commandHelper;
    }

    public bool TryInvoke(string commandName, string[] args, out string? error)
    {
        try
        {
            object helper = this.commandHelper;
            FieldInfo? managerField = helper.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(field => field.FieldType.Name.Equals("CommandManager", StringComparison.Ordinal));

            object? manager = managerField?.GetValue(helper);
            if (manager is null)
            {
                error = "SMAPI's command manager could not be found.";
                return false;
            }

            MethodInfo? getMethod = manager.GetType().GetMethod(
                "Get",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null
            );

            object? command = getMethod?.Invoke(manager, new object[] { commandName });
            if (command is null)
            {
                error = $"The command '{commandName}' is not registered.";
                return false;
            }

            PropertyInfo? callbackProperty = command.GetType().GetProperty(
                "Callback",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );

            if (callbackProperty?.GetValue(command) is not Action<string, string[]> callback)
            {
                error = $"The callback for '{commandName}' could not be accessed.";
                return false;
            }

            callback(commandName, args);
            error = null;
            return true;
        }
        catch (TargetInvocationException ex)
        {
            error = ex.InnerException?.Message ?? ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
