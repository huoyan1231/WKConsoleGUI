using System;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UIElements;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace Wkmod_1;

[BepInPlugin("huoyan1231.WKconsoleGUI", "WKconsoleGUI", "0.0.0.1")]
[BepInProcess("White Knuckle.exe")]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    
    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F7))
        {
            Logger.LogInfo("F7 key pressed. Attempting to toggle console...");
            ToggleConsoleViaReflection();
        }
    }
    private void ToggleConsoleViaReflection()
    {
        // 获取 CommandConsole 类型
        var consoleType = AccessTools.TypeByName("CommandConsole");
        if (consoleType == null)
        {
            Logger.LogWarning("未找到 CommandConsole 类型！");
            return;
        }

        // 获取静态实例字段：CommandConsole.instance
        var instanceField = AccessTools.Field(consoleType, "instance");
        var instance = instanceField.GetValue(null);
        if (instance == null)
        {
            Logger.LogWarning("CommandConsole.instance 为 null！");
            return;
        }

        // 获取 ToggleConsole 方法并调用
        var toggleMethod = AccessTools.Method(consoleType, "ToggleConsole");
        toggleMethod?.Invoke(instance, null);
        Logger.LogInfo("调用 CommandConsole.ToggleConsole()");
    }
}

