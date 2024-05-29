using OBSCLIMacros;
using OBSCLIMacros.Actions;
using OBSCLIMacros.States;
using OBSStudioClient;
using OBSStudioClient.Messages;
using System.Text.Json;
using System.Text.Json.Serialization;

const string ConfigFileName = "config.json";
var config = new Config(ConfigFileName);

Console.Write("Loading config...");
if (config.Load())
{
    Console.WriteLine("OK");
}
else
{
    Console.WriteLine("Config not found");
    config.Credentials = EnterOBSCredentials();
}

var client = new ObsClient();

try
{
    while (!await Connect(client, config))
    {
        Console.WriteLine("Couldn't connect to OBS. Check host, port and password.");
        config.Credentials = EnterOBSCredentials();
    }

    await MenuState.Run(config, client);
}
finally
{
    client.Disconnect();
    client.Dispose();
}

static OBSCredentials EnterOBSCredentials()
{
    Console.WriteLine("Host:");
    var host = Console.ReadLine();
    Console.WriteLine("Port:");
    int port;
    while (!int.TryParse(Console.ReadLine(), out port))
    {
        Console.WriteLine("Not a valid number. Try again:");
    }
    Console.WriteLine("Password:");
    var password = Console.ReadLine();
    return new OBSCredentials
    {
        Host = host!,
        Port = port,
        Password = password!
    };
}

static async Task<bool> Connect(ObsClient client, Config config)
{
    var sem = new SemaphoreSlim(0, 1);
    var authorized = false;

    void HandlePropertyChange(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(client.ConnectionState))
        {
            if (client.ConnectionState == OBSStudioClient.Enums.ConnectionState.Connected)
            {
                authorized = true;
                sem.Release();
            }
            else if (client.ConnectionState == OBSStudioClient.Enums.ConnectionState.Disconnecting)
            {
                authorized = false;
                sem.Release();
            }
        }
    }

    client.PropertyChanged += HandlePropertyChange;
    var connected = await client.ConnectAsync(
        true,
        config.Credentials.Password,
        config.Credentials.Host,
        config.Credentials.Port,
        OBSStudioClient.Enums.EventSubscriptions.None);

    if (!connected)
    {
        client.PropertyChanged -= HandlePropertyChange;
        return false;
    }

    await sem.WaitAsync();
    client.PropertyChanged -= HandlePropertyChange;

    return connected && authorized;
}

namespace OBSCLIMacros
{
    class Config
    {
        public OBSCredentials Credentials { get; set; } = new();

        public Dictionary<ConsoleKeyInfo, IAction> Macros { get; set; } = new();

        [JsonIgnore]
        private string Filename { get; set; }

        public Config(string filename)
        {
            Filename = filename;
        }

        public void Save()
        {
            var serial = ToSerializableConfig();
            var json = JsonSerializer.Serialize(serial);
            using var sw = new StreamWriter(Filename);
            sw.Write(json);
        }

        public bool Load()
        {
            if (!File.Exists(Filename))
            {
                return false;
            }
            using var sr = new StreamReader(Filename);
            var json = sr.ReadToEnd();
            var result = JsonSerializer.Deserialize<SerializableConfig>(json);
            if (result == null)
            {
                return false;
            }

            Credentials = result.Credentials;
            Macros = result.Macros
                .Select(m =>
                {
                    var shift = (m.Modifiers & ConsoleModifiers.Shift) != 0;
                    var alt = (m.Modifiers & ConsoleModifiers.Alt) != 0;
                    var control = (m.Modifiers & ConsoleModifiers.Control) != 0;
                    var keyInfo = new ConsoleKeyInfo(m.KeyChar, m.Key, shift, alt, control);
                    return (keyInfo, m.Action.ToAction());
                })
                .ToDictionary();

            return true;
        }

        public string MacrosToString()
        {
            if (Macros.Count == 0)
            {
                return "Registered Macros: None";
            }
            else
            {
                var lines = Macros
                    .OrderBy(m => m.Key.Key)
                    .Select(m => $"{m.Key.PrettyPrint()} -> {m.Value}");

                return $"Registered Macros:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
            }
        }

        private SerializableConfig ToSerializableConfig()
        {
            return new SerializableConfig
            {
                Credentials = Credentials,
                Macros = Macros.Select(m => new SerializableMacro
                {
                    KeyChar = m.Key.KeyChar,
                    Key = m.Key.Key,
                    Modifiers = m.Key.Modifiers,
                    Action = m.Value.ToSerializableAction()
                }).ToList()
            };
        }
    }

    class SerializableConfig
    {
        public OBSCredentials Credentials { get; set; } = new();

        public List<SerializableMacro> Macros { get; set; } = new();
    }

    class SerializableMacro
    {
        public char KeyChar { get; set; }

        public ConsoleKey Key { get; set; }

        public ConsoleModifiers Modifiers { get; set; }

        public SerializableAction Action { get; set; }
    }

    public class OBSCredentials
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 4455;
        public string Password { get; set; } = string.Empty;
    }

    public static class ConsoleKeyInfoExtensions
    {
        public static string PrettyPrint(this ConsoleKeyInfo key)
        {
            string modifiers = string.Empty;
            modifiers += (key.Modifiers & ConsoleModifiers.Alt) != 0 ? "+ALT" : string.Empty;
            modifiers += (key.Modifiers & ConsoleModifiers.Shift) != 0 ? "+SHIFT" : string.Empty;
            modifiers += (key.Modifiers & ConsoleModifiers.Control) != 0 ? "+CTRL" : string.Empty;
            return $"{key.Key}{modifiers}";
        }
    }

    namespace States
    {
        static class MenuState
        {
            public static async Task Run(Config config, ObsClient client)
            {
                while (true)
                {
                    Console.Clear();
                    Console.WriteLine(
@"m) Macro Mode
e) Edit Mode
s) Save");

                    var key = Console.ReadKey(true);

                    if (key.Key == ConsoleKey.Escape)
                    {
                        return;
                    }
                    else if (key.Key == ConsoleKey.M)
                    {
                        await MacroState.Run(config, client);
                    }
                    else if (key.Key == ConsoleKey.E)
                    {
                        await EditState.Run(config, client);
                    }
                    else if (key.Key == ConsoleKey.S)
                    {
                        config.Save();
                    }
                }
            }
        }

        static class MacroState
        {
            public static async Task Run(Config config, ObsClient client)
            {
                Console.Clear();
                Console.WriteLine(
$@"Macro State
{config.MacrosToString()}");

                while (true)
                {
                    var key = Console.ReadKey(true);

                    if (key.Key == ConsoleKey.Escape)
                    {
                        return;
                    }
                    else if (config.Macros.TryGetValue(key, out var value))
                    {
                        await value.Run(client);
                    }
                }
            }
        }

        static class EditState
        {
            public static async Task Run(Config config, ObsClient client)
            {
                while (true)
                {
                    Console.Clear();
                    Console.WriteLine(
$@"{config.MacrosToString()}
n) New Macro
r) Remove Macro");

                    var key = Console.ReadKey(true);

                    if (key.Key == ConsoleKey.Escape)
                    {
                        return;
                    }
                    else if (key.Key == ConsoleKey.N)
                    {
                        await RecordKeyState.Run(config, client);
                    }
                    else if (key.Key == ConsoleKey.R)
                    {
                        RemoveKeyState.Run(config);
                    }
                }
            }
        }

        static class RecordKeyState
        {
            public static async Task Run(Config config, ObsClient client)
            {
                while (true)
                {
                    Console.Clear();
                    Console.WriteLine("Press key to assign new macro:");

                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape)
                    {
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"Key pressed: {key.PrettyPrint()}");
                        if (await AssignActionState.Run(config, client, key))
                        {
                            return;
                        }
                    }
                }
            }
        }

        static class AssignActionState
        {
            public static async Task<bool> Run(Config config, ObsClient client, ConsoleKeyInfo triggerKey)
            {
                while (true)
                {
                    Console.WriteLine(
@"Select Action:
s) Switch Scene
q) Enable Scene Item
w) Disable Scene Item
e) Mute Input
r) Unmute Input
h) Trigger Hotkey");

                    var key = Console.ReadKey(true);
                    switch (key.Key)
                    {
                        case ConsoleKey.Escape:
                            {
                                return false;
                            }
                        case ConsoleKey.S:
                            {
                                Console.WriteLine("Listing Scenes...");
                                var scenes = await client.GetSceneList();
                                var scene = PromptForChoice(scenes.Scenes, s => s.SceneName);

                                config.Macros.Add(triggerKey, new SwitchScene(scene.SceneName));

                                return true;
                            }
                        case ConsoleKey.Q:
                            {
                                Console.WriteLine("Listing Scenes...");
                                var scenes = await client.GetSceneList();
                                var scene = PromptForChoice(scenes.Scenes, s => s.SceneName);

                                Console.WriteLine("Listing Items...");
                                var items = await client.GetSceneItemList(scene.SceneName);
                                var item = PromptForChoice(items, i => i.SourceName);

                                config.Macros.Add(triggerKey, new EnableItem(scene.SceneName, item.SceneItemId, item.SourceName));

                                return true;
                            }
                        case ConsoleKey.W:
                            {
                                Console.WriteLine("Listing Scenes...");
                                var scenes = await client.GetSceneList();
                                var scene = PromptForChoice(scenes.Scenes, s => s.SceneName);

                                Console.WriteLine("Listing Items...");
                                var items = await client.GetSceneItemList(scene.SceneName);
                                var item = PromptForChoice(items, i => i.SourceName);

                                config.Macros.Add(triggerKey, new DisableItem(scene.SceneName, item.SceneItemId, item.SourceName));

                                return true;
                            }
                        case ConsoleKey.E:
                            {
                                Console.WriteLine("Listing Inputs...");
                                var inputs = FilterAudioInputs(await client.GetInputList());
                                var input = PromptForChoice(inputs, i => i.InputName);

                                config.Macros.Add(triggerKey, new MuteInput(input.InputName));

                                return true;
                            }
                        case ConsoleKey.R:
                            {
                                Console.WriteLine("Listing Inputs...");
                                var inputs = FilterAudioInputs(await client.GetInputList());
                                var input = PromptForChoice(inputs, i => i.InputName);

                                config.Macros.Add(triggerKey, new UnmuteInput(input.InputName));

                                return true;
                            }
                        case ConsoleKey.H:
                            {
                                Console.WriteLine("Listing Hotkeys...");
                                var hotkeys = await client.GetHotkeyList();
                                var hotkey = PromptForChoice(hotkeys);

                                config.Macros.Add(triggerKey, new TriggerHotkey(hotkey));

                                return true;
                            }
                    }
                }
            }


            private static IEnumerable<(int Index, T Value)> AddIndex<T>(this IEnumerable<T> values)
            {
                return values.Select((v, i) => (i, v));
            }

            private static T PromptForChoice<T>(IEnumerable<T> values, Func<T, string> toStringFunc)
            {
                var newVals = values.AddIndex();
                foreach (var v in newVals)
                {
                    Console.WriteLine($"{v.Index})\t{toStringFunc(v.Value)}");
                }

                Console.WriteLine("Enter index:");
                int index;
                while (!int.TryParse(Console.ReadLine(), out index))
                {
                    Console.WriteLine("Not a number!");
                }

                return newVals.First(v => v.Index == index).Value;
            }

            private static T PromptForChoice<T>(IEnumerable<T> values)
            {
                return PromptForChoice(values, v => v!.ToString()!);
            }

            private static IEnumerable<Input> FilterAudioInputs(IEnumerable<Input> inputs)
            {
                return inputs.Where(i => i.InputKind.Contains("output") || i.InputKind.Contains("input"));
            }
        }

        static class RemoveKeyState
        {
            public static void Run(Config config)
            {
                while (true)
                {
                    Console.Clear();
                    Console.WriteLine(
$@"{config.MacrosToString()}
Enter key to remove");

                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape)
                    {
                        return;
                    }
                    else if (config.Macros.Remove(key))
                    {
                        Console.WriteLine($"Removed {key.PrettyPrint()}");
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"Key {key.PrettyPrint()} not found");
                    }
                }
            }
        }
    }

    namespace Actions
    {
        enum ActionTypes
        {
            SwitchScene,
            EnableItem,
            DisableItem,
            ToggleInput,
            MuteInput,
            UnmuteInput,
            TriggerHotkey
        }

        interface IAction
        {
            Task Run(ObsClient client);

            SerializableAction ToSerializableAction();
        }

        class SerializableAction
        {
            public ActionTypes Type { get; set; }

            public Dictionary<string, string> Parameters { get; set; } = new();

            public IAction ToAction()
            {
                switch (Type)
                {
                    case ActionTypes.SwitchScene:
                        {
                            var name = Parameters[nameof(SwitchScene.SceneName)];
                            return new SwitchScene(name);
                        }
                    case ActionTypes.EnableItem:
                        {
                            var scene = Parameters[nameof(EnableItem.SceneName)];
                            var itemId = int.Parse(Parameters[nameof(EnableItem.ItemID)]);
                            var itemName = Parameters[nameof(EnableItem.ItemName)];
                            return new EnableItem(scene, itemId, itemName);
                        }
                    case ActionTypes.DisableItem:
                        {
                            var scene = Parameters[nameof(DisableItem.SceneName)];
                            var itemId = int.Parse(Parameters[nameof(DisableItem.ItemID)]);
                            var itemName = Parameters[nameof(DisableItem.ItemName)];
                            return new DisableItem(scene, itemId, itemName);
                        }
                    case ActionTypes.ToggleInput:
                        {
                            var inputName = Parameters[nameof(ToggleInput.InputName)];
                            return new ToggleInput(inputName);
                        }
                    case ActionTypes.MuteInput:
                        {
                            var inputName = Parameters[nameof(MuteInput.InputName)];
                            return new MuteInput(inputName);
                        }
                    case ActionTypes.UnmuteInput:
                        {
                            var inputName = Parameters[nameof(UnmuteInput.InputName)];
                            return new UnmuteInput(inputName);
                        }
                    case ActionTypes.TriggerHotkey:
                        {
                            var hotkey = Parameters[nameof(TriggerHotkey.Hotkey)];
                            return new TriggerHotkey(hotkey);
                        }
                }
                throw new ArgumentOutOfRangeException($"Action type {Type} is unknown!");
            }
        }

        class SwitchScene : IAction
        {
            public string SceneName { get; private set; }

            public SwitchScene(string sceneName)
            {
                SceneName = sceneName;
            }

            public async Task Run(ObsClient client)
            {
                await client.SetCurrentProgramScene(SceneName);
            }

            public SerializableAction ToSerializableAction()
            {
                var act = new SerializableAction();
                act.Type = ActionTypes.SwitchScene;
                act.Parameters.Add(nameof(SceneName), SceneName);
                return act;
            }

            public override string ToString()
            {
                return $"Switch to Scene: {SceneName}";
            }
        }

        class EnableItem : IAction
        {
            public string SceneName { get; private set; }

            public int ItemID { get; private set; }

            public string ItemName { get; private set; }

            public EnableItem(string scene, int itemID, string itemName)
            {
                SceneName = scene;
                ItemID = itemID;
                ItemName = itemName;
            }

            public async Task Run(ObsClient client)
            {
                await client.SetSceneItemEnabled(SceneName, ItemID, true);
            }

            public SerializableAction ToSerializableAction()
            {
                var act = new SerializableAction();
                act.Type = ActionTypes.EnableItem;
                act.Parameters.Add(nameof(SceneName), SceneName);
                act.Parameters.Add(nameof(ItemID), ItemID.ToString());
                act.Parameters.Add(nameof(ItemName), ItemName);
                return act;
            }

            public override string ToString()
            {
                return $"Enable {ItemName} in {SceneName}";
            }
        }

        class DisableItem : IAction
        {
            public string SceneName { get; private set; }

            public int ItemID { get; private set; }

            public string ItemName { get; private set; }

            public DisableItem(string sceneName, int itemID, string itemName)
            {
                SceneName = sceneName;
                ItemID = itemID;
                ItemName = itemName;
            }

            public async Task Run(ObsClient client)
            {
                await client.SetSceneItemEnabled(SceneName, ItemID, false);
            }

            public SerializableAction ToSerializableAction()
            {
                var act = new SerializableAction();
                act.Type = ActionTypes.DisableItem;
                act.Parameters.Add(nameof(SceneName), SceneName);
                act.Parameters.Add(nameof(ItemID), ItemID.ToString());
                act.Parameters.Add(nameof(ItemName), ItemName);
                return act;
            }

            public override string ToString()
            {
                return $"Disable {ItemName} in {SceneName}";
            }
        }

        class ToggleInput : IAction
        {
            public string InputName { get; private set; }

            public ToggleInput(string inputName)
            {
                InputName = inputName;
            }

            public async Task Run(ObsClient client)
            {
                await client.ToggleInputMute(InputName);
            }

            public SerializableAction ToSerializableAction()
            {
                var act = new SerializableAction();
                act.Type = ActionTypes.ToggleInput;
                act.Parameters.Add(nameof(InputName), InputName);
                return act;
            }

            public override string ToString()
            {
                return $"Toggle {InputName}";
            }
        }

        class MuteInput : IAction
        {
            public string InputName { get; private set; }

            public MuteInput(string inputName)
            {
                InputName = inputName;
            }

            public async Task Run(ObsClient client)
            {
                await client.SetInputMute(InputName, true);
            }

            public SerializableAction ToSerializableAction()
            {
                var act = new SerializableAction();
                act.Type = ActionTypes.MuteInput;
                act.Parameters.Add(nameof(InputName), InputName);
                return act;
            }

            public override string ToString()
            {
                return $"Mute {InputName}";
            }
        }

        class UnmuteInput : IAction
        {
            public string InputName { get; private set; }

            public UnmuteInput(string inputName)
            {
                InputName = inputName;
            }

            public async Task Run(ObsClient client)
            {
                await client.SetInputMute(InputName, false);
            }

            public SerializableAction ToSerializableAction()
            {
                var act = new SerializableAction();
                act.Type = ActionTypes.UnmuteInput;
                act.Parameters.Add(nameof(InputName), InputName);
                return act;
            }

            public override string ToString()
            {
                return $"Unmute {InputName}";
            }
        }

        class TriggerHotkey : IAction
        {
            public string Hotkey { get; private set; }

            public TriggerHotkey(string hotkey)
            {
                Hotkey = hotkey;
            }

            public async Task Run(ObsClient client)
            {
                await client.TriggerHotkeyByName(Hotkey);
            }

            public SerializableAction ToSerializableAction()
            {
                var act = new SerializableAction();
                act.Type = ActionTypes.TriggerHotkey;
                act.Parameters.Add(nameof(Hotkey), Hotkey);
                return act;
            }

            public override string ToString()
            {
                return $"Trigger Hotkey: {Hotkey}";
            }
        }
    }
}
