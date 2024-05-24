using OBSCLIMacros;
using OBSCLIMacros.Actions;
using OBSCLIMacros.States;
using OBSStudioClient;
using System.Text;
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
    var creds = EnterOBSCredentials();
    config.Credentials.Host = creds.Host;
    config.Credentials.Port = creds.Port;
    config.Credentials.Password = creds.Password;
    config.Save();
}

var client = new ObsClient();

try
{
    var isConnected = await client.ConnectAsync(
        true,
        config.Credentials.Password,
        config.Credentials.Host,
        config.Credentials.Port,
        OBSStudioClient.Enums.EventSubscriptions.None);
    await Task.Delay(1);    // currently a bug in the OBSClient library, this statement triggers the scheduler to run a task inside this library

    if (!isConnected)
    {
        return;
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
                var sb = new StringBuilder();
                sb.AppendLine("Registered Macros:");
                foreach (var m in Macros.OrderBy(m => m.Key.Key))
                {
                    sb.AppendLine($"{m.Key.PrettyPrint()} -> {m.Value}");
                }
                return sb.ToString();
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
                    Console.WriteLine("m) Macro Mode");
                    Console.WriteLine("e) Edit Mode");
                    Console.WriteLine("s) Save");

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
                Console.WriteLine("Macro State");
                Console.WriteLine(config.MacrosToString());

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
                    Console.WriteLine(config.MacrosToString());
                    Console.WriteLine("n) New Macro");
                    Console.WriteLine("r) Remove Macro");

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
                    Console.WriteLine("Select Action:");
                    Console.WriteLine("s) Switch Scene");
                    Console.WriteLine("t) Toggle Scene Item");

                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape)
                    {
                        return false;
                    }
                    else if (key.Key == ConsoleKey.S)
                    {
                        Console.WriteLine("Listing Scenes...");
                        var scenes = await client.GetSceneList();
                        foreach (var s in scenes.Scenes)
                        {
                            Console.WriteLine($"{s.SceneIndex}) {s.SceneName}");
                        }

                        Console.WriteLine("Enter index:");
                        int index;
                        while (!int.TryParse(Console.ReadLine(), out index))
                        {
                            Console.WriteLine("Not a number!");
                        }

                        var scene = scenes.Scenes.First(s => s.SceneIndex == index);

                        config.Macros.Add(triggerKey, new SwitchSceneAction(scene.SceneName));

                        return true;
                    }
                    else if (key.Key == ConsoleKey.T)
                    {
                        Console.WriteLine("Listing Scenes...");
                        var scenes = await client.GetSceneList();
                        foreach (var s in scenes.Scenes)
                        {
                            Console.WriteLine($"{s.SceneIndex}) {s.SceneName}");
                        }

                        Console.WriteLine("Enter index:");
                        int index;
                        while (!int.TryParse(Console.ReadLine(), out index))
                        {
                            Console.WriteLine("Not a number!");
                        }

                        var scene = scenes.Scenes.First(s => s.SceneIndex == index);

                        Console.WriteLine("Listing Items...");
                        var items = await client.GetSceneItemList(scene.SceneName);
                        foreach (var i in items)
                        {
                            Console.WriteLine($"{i.SceneItemIndex}) {i.SourceName}");
                        }

                        Console.WriteLine("Enter index:");
                        while (!int.TryParse(Console.ReadLine(), out index))
                        {
                            Console.WriteLine("Not a number!");
                        }

                        var item = items.First(i => i.SceneItemIndex == index);

                        config.Macros.Add(triggerKey, new ToggleItemAction(scene.SceneName, item.SceneItemId, item.SourceName, item.SceneItemEnabled));

                        return true;
                    }
                }
            }
        }

        static class RemoveKeyState
        {
            public static void Run(Config config)
            {
                while (true)
                {
                    Console.Clear();
                    Console.WriteLine(config.MacrosToString());
                    Console.WriteLine("Enter key to remove");

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
            ToggleItem
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
                            var name = Parameters[nameof(SwitchSceneAction.SceneName)];
                            return new SwitchSceneAction(name);
                        }
                    case ActionTypes.ToggleItem:
                        {
                            var scene = Parameters[nameof(ToggleItemAction.SceneName)];
                            if (!int.TryParse(Parameters[nameof(ToggleItemAction.ItemID)], out var itemID))
                            {
                                throw new ArgumentException("ItemID could not be parsed!");
                            }
                            var itemName = Parameters[nameof(ToggleItemAction.ItemName)];
                            //var enabled = await Globals.Client.GetSceneItemEnabled(scene, itemID);
                            return new ToggleItemAction(scene, itemID, itemName, false);
                        }
                }
                throw new ArgumentOutOfRangeException($"Action type {Type} is unknown!");
            }
        }

        class SwitchSceneAction : IAction
        {
            public string SceneName { get; private set; }

            public SwitchSceneAction(string sceneName)
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

        class ToggleItemAction : IAction
        {
            public string SceneName { get; private set; }

            public int ItemID { get; private set; }

            public string ItemName { get; private set; }

            private bool Enabled { get; set; }

            public ToggleItemAction(string scene, int itemID, string itemName, bool enabled)
            {
                SceneName = scene;
                ItemID = itemID;
                ItemName = itemName;
                Enabled = enabled;
            }

            public async Task Run(ObsClient client)
            {
                Enabled = !Enabled;
                await client.SetSceneItemEnabled(SceneName, ItemID, Enabled);
            }

            public SerializableAction ToSerializableAction()
            {
                var act = new SerializableAction();
                act.Type = ActionTypes.ToggleItem;
                act.Parameters.Add(nameof(SceneName), SceneName);
                act.Parameters.Add(nameof(ItemID), ItemID.ToString());
                act.Parameters.Add(nameof(ItemName), ItemName);
                return act;
            }

            public override string ToString()
            {
                return $"Toggle {ItemName} in {SceneName}";
            }
        }
    }
}
