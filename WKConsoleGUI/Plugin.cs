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
    private float _pendingScaleFactor;
    private ConfigEntry<int> buttonsPerRow; 
    private bool showGUI = false;
    private Rect windowRect = new Rect(100, 100, 500, 550);
    private GUIStyle tooltipStyle;
    private bool tooltipStyleInitialized = false;
    
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

        guiScaleFactor = Config.Bind(
            "GUI",
            "ScaleFactor",
            1.0f,
            new ConfigDescription(
                "GUI 窗口的缩放比例。在高DPI屏幕上可以设置为1.5, 2.0等，以增大窗口尺寸。",
                new AcceptableValueRange<float>(0.5f, 3.0f)
            )
        );
        buttonsPerRow = Config.Bind(
            "GUI",
            "ButtonsPerRow",
            3, // 默认每行 3 个按钮
            new ConfigDescription(
                "每行显示的命令按钮数量。",
                new AcceptableValueRange<int>(1, 5) 
            )
        );


        // --- 初始化 _pendingScaleFactor 为当前配置值 ---
        _pendingScaleFactor = guiScaleFactor.Value;
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
        Matrix4x4 originalMatrix = GUI.matrix;
        float scale = guiScaleFactor.Value;
        
        if (scale > 0 && scale != 1.0f)
        {
            Vector3 scaleCenter = new Vector3(Screen.width / 2, Screen.height / 2, 0);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
        }

        if (showGUI)
        {
            // 确保窗口ID唯一
            windowRect = GUI.Window(GetHashCode(), windowRect, DrawWindow, "WKConsoleGUI");
        }
        GUI.matrix = originalMatrix;

        // --- 核心改动：在所有 GUI.Window 绘制完成后，统一处理 Tooltip ---
        if (Event.current.type == EventType.Repaint && !string.IsNullOrEmpty(GUI.tooltip))
        {
            Vector2 mousePos = Event.current.mousePosition;
            
            // 计算 Tooltip 文本的实际尺寸
            Vector2 tooltipSize = tooltipStyle.CalcSize(new GUIContent(GUI.tooltip));
            
            float x = mousePos.x + 15; 
            float y = mousePos.y + 15; 

            float screenWidth = Screen.width;
            float screenHeight = Screen.height;

            // 调整 X 坐标以防超出屏幕右侧
            if (x + tooltipSize.x > screenWidth)
            {
                x = screenWidth - tooltipSize.x - 5; 
            }
            // 调整 Y 坐标以防超出屏幕底部
            if (y + tooltipSize.y > screenHeight)
            {
                y = mousePos.y - tooltipSize.y - 15; 
                if (y < 0) y = 0; 
            }

            Rect tooltipRect = new Rect(x, y, tooltipSize.x, tooltipSize.y);
            
            GUI.Label(tooltipRect, GUI.tooltip, tooltipStyle);
        }
    }

    void DrawWindow(int windowID)
    {
        GUILayout.BeginVertical();
        const float fixedBottomHeight = 150f; 
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Width(windowRect.width), GUILayout.Height(windowRect.height - fixedBottomHeight));

        int currentButtonIndex = 0;
        int maxButtonsPerRow = buttonsPerRow.Value; 

        float availableWidthForButtons = windowRect.width - 25; 
        float buttonSpacing = 5f;
        float buttonWidth = (availableWidthForButtons - (maxButtonsPerRow - 1) * buttonSpacing) / maxButtonsPerRow;
        if (buttonWidth < 50) buttonWidth = 50; 

        foreach (CommandEntry entry in commands)
        {
            if (currentButtonIndex % maxButtonsPerRow == 0)
            {
                if (currentButtonIndex > 0)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.Space(5); 
                }
                GUILayout.BeginHorizontal(); 
            }

            // --- 关键：GUIContent 传入 Label 和 Description 作为 Tooltip ---
            GUIContent buttonContent = new GUIContent(entry.Label, entry.Description);
            
            // 使用 GUILayout.Box 来创建可视化块，并用 Tooltip 显示描述
            Rect boxRect = GUILayoutUtility.GetRect(buttonContent, GUI.skin.box, GUILayout.Width(buttonWidth), GUILayout.Height(40)); 
            
            // 绘制 Box
            GUI.Box(boxRect, buttonContent); // GUI.Box 会自动处理 GUIContent 中的 Tooltip 部分

            // 在 boxRect 范围内绘制一个透明按钮来捕获点击事件
            if (GUI.Button(boxRect, GUIContent.none, GUIStyle.none)) 
            {
                ExecuteCommand(entry.Command);
            }

            currentButtonIndex++;
        }

        if (currentButtonIndex > 0)
        {
            GUILayout.EndHorizontal();
        }
        
        GUILayout.EndScrollView(); // 结束滚动视图

        // --- 新增：重载配置按钮 ---
        GUILayout.Space(10); // 按钮上方留点空间
        // --- GUI 缩放滑块 (现在操作的是 _pendingScaleFactor) ---
        GUILayout.Label($"GUI 缩放 ({_pendingScaleFactor:F1}x)"); // 显示暂存值
        _pendingScaleFactor = GUILayout.HorizontalSlider(_pendingScaleFactor, 0.5f, 3.0f);
        // --- 新增：应用缩放按钮 ---
        if (GUILayout.Button("应用缩放"))
        {
            if (guiScaleFactor.Value != _pendingScaleFactor) // 只有当值改变时才应用
            {
                guiScaleFactor.Value = _pendingScaleFactor; // 将暂存值赋给配置项
                Logger.LogInfo($"GUI 缩放已应用: {guiScaleFactor.Value:F1}x");
                // 由于 guiScaleFactor.Value 改变会立即影响 OnGUI 中的 GUI.matrix，所以不需要 Config.Reload()
                // 但是，如果希望更改保存到文件，可能需要手动触发 Save()
                // Config.Save(); // 如果需要在运行时立即写入配置文件
            }
            else
            {
                Logger.LogInfo("缩放值未改变，无需应用。");
            }
        }
        // --- 结束新增 ---


        GUILayout.Space(10); // 分隔空间

        if (GUILayout.Button("重载配置"))
        {
            Logger.LogInfo("尝试重载配置...");
            Config.Reload(); // BepInEx 的 ConfigManager 提供的重载方法
            _pendingScaleFactor = guiScaleFactor.Value; 
            LoadCommands(); // 重新加载命令列表（如果从配置加载）
            Logger.LogInfo("配置已重载！");
        }


        GUI.DragWindow(new Rect(0, 0, windowRect.width, windowRect.height));
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

    private void LoadCommands()
    {
        commands.Clear(); // 清空现有命令列表

        string jsonFilePath = Path.Combine(Paths.ConfigPath, "GUICommandsButtons.json"); // 构建 JSON 文件路径

        if (File.Exists(jsonFilePath))
        {
            try
            {
                string jsonString = File.ReadAllText(jsonFilePath);
                List<CommandEntry> loadedCommands = JsonConvert.DeserializeObject<List<CommandEntry>>(jsonString);

                if (loadedCommands != null)
                {
                    commands.AddRange(loadedCommands);
                    Logger.LogInfo($"成功从 '{jsonFilePath}' 加载 {commands.Count} 条命令。");
                }
                else
                {
                    Logger.LogWarning($"JSON 文件 '{jsonFilePath}' 内容为空或格式不正确。将加载默认命令。");
                    LoadDefaultCommands(); // 如果文件为空或解析失败，加载默认命令
                }
            }
            catch (JsonException ex)
            {
                Logger.LogError($"解析 JSON 文件 '{jsonFilePath}' 失败: {ex.Message}。请检查文件格式。将加载默认命令。");
                Logger.LogDebug($"JSON 解析错误详情: {ex.ToString()}"); // 调试用
                LoadDefaultCommands(); // JSON 解析失败，加载默认命令
            }
            catch (IOException ex)
            {
                Logger.LogError($"读取文件 '{jsonFilePath}' 失败: {ex.Message}。将加载默认命令。");
                LoadDefaultCommands();
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"加载命令时发生未知错误: {ex.Message}。将加载默认命令。");
                LoadDefaultCommands();
            }
        }
        else
        {
            Logger.LogWarning($"未找到命令文件 '{jsonFilePath}'。将加载默认命令，并尝试创建示例文件。");
            LoadDefaultCommands();
            SaveDefaultCommandsToFile(jsonFilePath); // 创建一个包含默认命令的示例文件
        }
    }

    // --- 新增：加载默认命令的方法 ---
    private void LoadDefaultCommands()
    {
        commands.Clear(); // 确保在加载默认前清空
        commands.Add(new CommandEntry("开启作弊", "cheats", "切换作弊，要开，不开下面用不了，开了禁用进度和成就防止成为世1开"));
        commands.Add(new CommandEntry("推动(?", "addforcetoplayer 1, 1, 1", ""));
        commands.Add(new CommandEntry("全亮", "fullbright", "███████"));
        commands.Add(new CommandEntry("获取种子", "getgenerationseed", "目前还不能种地（大概"));
        commands.Add(new CommandEntry("无敌", "godmode", "但是还是得爬"));
        commands.Add(new CommandEntry("无限体力", "infinitestamina", "我编不出来了"));
        commands.Add(new CommandEntry("默认测试1", "test_default 1", "这是从代码加载的默认命令1"));
        commands.Add(new CommandEntry("默认测试2", "test_default 2", "这是从代码加载的默认命令2"));
    }

    // --- 新增：保存默认命令到文件的方法 ---
    private void SaveDefaultCommandsToFile(string filePath)
    {
        try
        {
            // 创建一个包含默认命令的列表用于序列化
            List<CommandEntry> defaultCommands = new List<CommandEntry>
            {
                new CommandEntry("开启作弊", "cheats", "切换作弊，要开，不开下面用不了，开了禁用进度和成就防止成为世1开"),
                new CommandEntry("推动(?", "addforcetoplayer 1, 1, 1", ""),
                new CommandEntry("全亮", "fullbright", "███████"),
                new CommandEntry("获取种子", "getgenerationseed", "目前还不能种地（大概"),
                new CommandEntry("无敌", "godmode", "但是还是得爬"),
                new CommandEntry("无限体力", "infinitestamina", "我编不出来了")
            };

            string jsonString = JsonConvert.SerializeObject(defaultCommands, Formatting.Indented); // Formatting.Indented 使JSON更易读
            File.WriteAllText(filePath, jsonString);
            Logger.LogInfo($"已在 '{filePath}' 创建示例命令文件。");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"创建示例命令文件失败: {ex.Message}");
        }
    }
}

