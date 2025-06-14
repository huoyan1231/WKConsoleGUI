using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using HarmonyLib;
using System.Collections.Generic;
using BepInEx.Configuration;
using Newtonsoft.Json;
using System.IO;

namespace WKConsoleGUI;

[BepInPlugin("huoyan1231.WKconsoleGUI", "WKconsoleGUI", "0.0.0.1")]
[BepInProcess("White Knuckle.exe")]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    
    // --- 配置条目 ---
    private ConfigEntry<KeyboardShortcut> toggleConsoleKey;
    private ConfigEntry<KeyboardShortcut> toggleGUIKey;
    // 如果要从配置文件加载命令，这里可以是字符串，然后在Awake中解析
    // private ConfigEntry<string> customCommandsJson; 
    private ConfigEntry<string> customCommandsJson;
    private ConfigEntry<float> guiScaleFactor;

    
    private bool showGUI = false;
    private Rect windowRect = new Rect(100, 100, 300, 400);

    private Vector2 scrollPosition = Vector2.zero; // 初始化为(0,0)

    // 命令列表，可以从配置文件加载
    private List<CommandEntry> commands = new List<CommandEntry>();

    
    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        
        // --- 初始化配置条目 ---
        toggleConsoleKey = Config.Bind(
            "General",                                    // 配置节名
            "ToggleConsoleHotkey",                        // 配置键名
            new KeyboardShortcut(KeyCode.F7),             // 默认值：F7
            "用于切换游戏内置控制台的快捷键。"           // 描述
        );

        toggleGUIKey = Config.Bind(
            "General",
            "ToggleGUIHotkey",
            new KeyboardShortcut(KeyCode.F8),
            "用于切换此Mod界面的快捷键。"
        );

        // 初始化默认命令列表 (如果从配置文件加载，则只在文件不存在时写入默认值)
        customCommandsJson = Config.Bind(
            "Commands",
            "CustomCommands",
            JsonConvert.SerializeObject(new List<CommandEntry>
            {
                /* 默认命令 */
            }), // 默认值的JS
            "自定义控制台命令列表 (JSON格式)。"
            );
        LoadCommands(); 
        // --- 新增：DPI 缩放比例配置项 ---
        guiScaleFactor = Config.Bind(
            "GUI",
            "ScaleFactor",
            1.0f, // 默认不缩放
            "GUI 窗口的缩放比例。在高DPI屏幕上可以设置为1.5, 2.0等，以增大窗口尺寸。"
        );

    }

    private void Update()
    {
        //切换控制台显示
        if (toggleConsoleKey.Value.IsDown())
        {
            Logger.LogInfo($"{toggleConsoleKey.Value.ToString()} pressed. Attempting to toggle console...");
            ToggleConsoleViaReflection();
        }

        //切换 GUI 显示，使用控制台释放鼠标
        if (toggleGUIKey.Value.IsDown())
        {
            ToggleConsoleViaReflection();
            showGUI = !showGUI;
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
    void OnGUI()
    {
        // --- 新增：应用 DPI 缩放 ---
        // 记录原始矩阵，以便在 OnGUI 结束时恢复
        Matrix4x4 originalMatrix = GUI.matrix;

        // 获取 DPI 缩放因子
        float scale = guiScaleFactor.Value;

        // 如果用户设置的缩放因子大于 0 且不等于 1，则应用缩放
        if (scale > 0 && scale != 1.0f)
        {
            // 计算屏幕中心点，确保缩放以中心为基准，而不是左上角
            Vector3 scaleCenter = new Vector3(Screen.width / 2, Screen.height / 2, 0);
            
            // 创建缩放矩阵
            // 注意：GUI.matrix的缩放是基于屏幕左上角(0,0)的
            // 为了让窗口在缩放后仍然居中，我们可以在窗口绘制时重新计算其位置
            // 或者简单地将缩放应用到整个GUI，并让用户调整窗口位置
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

            // 如果窗口位置也需要随DPI调整，可能需要这样计算：
            // windowRect.x *= scale;
            // windowRect.y *= scale;
            // windowRect.width *= scale;
            // windowRect.height *= scale;
            // 但更好的做法是让用户拖动窗口到合适位置，或在Awake中设置更大的初始尺寸
        }
        // --- 结束 DPI 缩放应用 ---

        if (showGUI)
        {
            // 确保窗口ID唯一
            windowRect = GUI.Window(GetHashCode(), windowRect, DrawWindow, "控制台指令面板");
        }
        GUI.matrix = originalMatrix;

    }

    void DrawWindow(int windowID)
    {
        GUILayout.BeginVertical();
        
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Width(windowRect.width), GUILayout.Height(windowRect.height - 50)); // 预留底部空间给重载按钮和拖动条

        foreach (CommandEntry entry in commands)
        {
            GUILayout.BeginVertical("box");
            GUILayout.Label(entry.Description);
            if (GUILayout.Button(entry.Label))
            {
                ExecuteCommand(entry.Command);
            }
            GUILayout.EndVertical();
        }
        
        GUILayout.EndScrollView(); // 结束滚动视图

        // --- 新增：重载配置按钮 ---
        GUILayout.Space(10); // 按钮上方留点空间
        if (GUILayout.Button("重载配置"))
        {
            Logger.LogInfo("尝试重载配置...");
            Config.Reload(); // BepInEx 的 ConfigManager 提供的重载方法
            LoadCommands(); // 重新加载命令列表（如果从配置加载）
            Logger.LogInfo("配置已重载！");
        }


        GUI.DragWindow(new Rect(0, 0, 10000, 20)); // 允许拖动窗口
        GUILayout.EndVertical();
    }

    void ExecuteCommand(string commandText)
    {
        var type = AccessTools.TypeByName("CommandConsole");
        var instanceField = AccessTools.Field(type, "instance");
        var instance = instanceField.GetValue(null);
        if (instance == null)
        {
            Logger.LogWarning("CommandConsole.instance 为 null！");
            return;
        }

        var execMethod = AccessTools.Method(type, "ExecuteCommand");
        execMethod?.Invoke(instance, new object[] { commandText });
        Logger.LogInfo($"执行命令：{commandText}");
    }

    // 用类代替元组
    public class CommandEntry
    {
        public string Label;
        public string Command;
        public string Description;

        public CommandEntry(string label, string command, string description)
        {
            Label = label;
            Command = command;
            Description = description;
        }
    }

    // --- 新增：加载命令列表的方法 ---
    private void LoadCommands()
    {
        // 这是一个简单的示例，您可以扩展为从 ConfigEntry<string> 或 JSON 文件加载
        // 为了方便演示，我们仍然使用硬编码的默认值，但逻辑可以轻松修改
        
        // 每次加载前清空现有列表，防止重复
        commands.Clear(); 

        // 这里可以从配置文件加载 JSON 字符串，然后反序列化
        // 例如：
         string jsonCommands = customCommandsJson.Value;
         if (!string.IsNullOrEmpty(jsonCommands)) {
             try {
                 commands = JsonConvert.DeserializeObject<List<CommandEntry>>(jsonCommands);
                 Logger.LogInfo("Commands loaded from config.");
                 return; // 如果成功从配置加载，则不加载默认值
             } catch (System.Exception ex) {
                 Logger.LogError($"Failed to load commands from config: {ex.Message}");
             }
         }

        // 如果没有从配置加载成功，则加载默认命令
        Logger.LogInfo("Loading default commands.");
        commands.Add(new CommandEntry("开启作弊", "cheats true", "启用作弊功能"));
        commands.Add(new CommandEntry("关闭作弊", "cheats false", "禁用作弊功能")); // 添加关闭作弊
        commands.Add(new CommandEntry("传送到起点", "teleport 0 0 0", "将玩家传送到原点"));
        commands.Add(new CommandEntry("加满血", "heal", "恢复所有生命值"));
        commands.Add(new CommandEntry("生成手枪", "spawn item_pistol", "生成一把手枪")); // 更多示例
        commands.Add(new CommandEntry("生成步枪", "spawn item_rifle", "生成一把步枪"));
    }

}

