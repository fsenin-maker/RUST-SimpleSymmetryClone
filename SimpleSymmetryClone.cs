using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Game.Rust;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("SimpleSymmetryClone", "GrokAI", "1.3.2")]
    [Description("Симметричное строительство с красивым UI, авто-детектом, поддержкой дверей и апгрейда")]
    public class SimpleSymmetryClone : RustPlugin
    {
        #region Конфигурация и Константы
        
        private const string PermUse = "simplesymmetryclone.use";
        private const string UILayer = "SimpleSymmetryClone.UI";
        private const float DetectRadius = 50f;
        
        // Prefab IDs для оконных решеток
        private const ulong WindowBarsMetalPrefabId = 1429861300;
        private const ulong WindowBarsWoodPrefabId = 1379457182;
        
        // Допустимые типы симметрии
        private static readonly string[] ValidSymmetryTypes = { "n2s", "n3s", "n4s", "n6s", "m2s", "m4s" };
        
        #endregion
        
        #region Классы данных
        
        private readonly Dictionary<ulong, SymmetryData> _playerData = new Dictionary<ulong, SymmetryData>();

        private class SymmetryData
        {
            public bool Enabled { get; set; } = false;
            public Vector3? Center { get; set; }
            public Quaternion? CenterRot { get; set; }
            public string Type { get; set; } = "n4s";
            public bool UiEnabled { get; set; } = true;
        }
        
        #endregion
        
        #region Инициализация
        
        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            cmd.AddChatCommand("sym", this, nameof(CmdSym));
            cmd.AddChatCommand("ui", this, nameof(CmdUI));
        }
        
        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyUI(player);
            }
            _playerData.Clear();
        }
        
        #endregion
        
        #region Работа с данными игрока
        
        private SymmetryData GetData(ulong userId)
        {
            if (!_playerData.TryGetValue(userId, out var data))
            {
                data = new SymmetryData();
                _playerData[userId] = data;
            }
            return data;
        }
        
        #endregion
        
        #region Команды чата
        
        private void CmdSym(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            
            if (!permission.UserHasPermission(player.UserIDString, PermUse))
            {
                SendReply(player, "У вас нет прав на использование симметрии.");
                return;
            }

            var data = GetData(player.userID);

            if (args.Length == 0)
            {
                data.UiEnabled = !data.UiEnabled;
                if (data.UiEnabled) 
                    CreateUI(player);
                else 
                    DestroyUI(player);
                    
                SendReply(player, $"UI {(data.UiEnabled ? "включён" : "выключен")}.");
                return;
            }

            string sub = args[0].ToLower();

            switch (sub)
            {
                case "toggle":
                    data.Enabled = !data.Enabled;
                    UpdateUI(player);
                    SendReply(player, $"Симметрия {(data.Enabled ? "включена" : "выключена")}.");
                    break;

                case "set":
                    if (SetCenter(player))
                    {
                        if (args.Length > 1 && args[1].ToLower() == "auto") 
                            AutoDetectType(player);
                        UpdateUI(player);
                    }
                    break;

                case "auto":
                    if (data.Center == null)
                    {
                        SendReply(player, "Сначала установите центр: /sym set");
                        return;
                    }
                    AutoDetectType(player);
                    UpdateUI(player);
                    break;

                case "delete":
                    data.Center = null;
                    data.CenterRot = null;
                    UpdateUI(player);
                    SendReply(player, "Центр симметрии удалён.");
                    break;

                default:
                    if (ValidSymmetryTypes.Contains(sub))
                    {
                        data.Type = sub;
                        UpdateUI(player);
                        SendReply(player, $"Тип симметрии: {sub.ToUpper()}");
                    }
                    else
                    {
                        SendReply(player, $"Доступные типы: {string.Join(", ", ValidSymmetryTypes)}");
                    }
                    break;
            }
        }

        private void CmdUI(BasePlayer player, string cmd, string[] args)
        {
            if (player == null || args.Length == 0) return;
            
            var data = GetData(player.userID);

            switch (args[0])
            {
                case "ToggleBtn":
                    data.Enabled = !data.Enabled;
                    UpdateUI(player);
                    break;
                    
                case "SetBtn":
                    if (SetCenter(player))
                        AutoDetectType(player);
                    UpdateUI(player);
                    break;
                    
                case "DeleteBtn":
                    data.Center = null;
                    data.CenterRot = null;
                    UpdateUI(player);
                    break;
                    
                default:
                    if (args[0].StartsWith("Type") && args[0].Length > 4)
                    {
                        string type = args[0].Substring(4).ToLower();
                        if (ValidSymmetryTypes.Contains(type))
                        {
                            data.Type = type;
                            UpdateUI(player);
                        }
                    }
                    break;
            }
        }
        
        #endregion
        
        #region Основная логика
        
        private bool SetCenter(BasePlayer player)
        {
            if (player == null) return false;
            
            var data = GetData(player.userID);
            
            RaycastHit hit;
            if (Physics.Raycast(player.eyes.HeadRay(), out hit, 100f))
            {
                data.Center = hit.point;
                data.CenterRot = player.transform.rotation;
                SendReply(player, "Центр симметрии установлен.");
                return true;
            }
            
            SendReply(player, "Не удалось найти поверхность.");
            return false;
        }

        private void AutoDetectType(BasePlayer player)
        {
            if (player == null) return;
            
            var data = GetData(player.userID);
            if (data.Center == null) return;

            var center = data.Center.Value;
            List<BuildingBlock> foundations = new List<BuildingBlock>();
            
            Vis.Entities(center, DetectRadius, foundations, Rust.LayerMask.GetMask("Construction"));

            // Оптимизированный фильтр фундаментов
            foundations = foundations.Where(b => 
                b != null && 
                b.OwnerID == player.userID &&
                (b.PrefabName.Contains("foundation") || b.PrefabName.Contains("foundation.triangle"))
            ).ToList();

            if (foundations.Count < 3)
            {
                data.Type = "n4s";
                SendReply(player, "Мало фундаментов для детекта → n4s по умолчанию.");
                return;
            }

            List<float> angles = new List<float>();
            foreach (var f in foundations)
            {
                if (f == null || f.transform == null) continue;
                
                var dir = f.transform.position - center;
                dir.y = 0;
                
                if (dir.sqrMagnitude < 0.01f) continue;
                
                dir.Normalize();
                float angle = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
                
                if (angle < 0) angle += 360;
                angles.Add(angle);
            }

            if (angles.Count < 3)
            {
                data.Type = "n4s";
                SendReply(player, "Недостаточно углов для вычисления → n4s по умолчанию.");
                return;
            }

            angles.Sort();
            float minDiff = 360f;
            
            for (int i = 0; i < angles.Count; i++)
            {
                float diff = (angles[(i + 1) % angles.Count] - angles[i] + 360) % 360;
                if (diff > 0.1f && diff < minDiff) 
                    minDiff = diff;
            }

            int sides = minDiff > 0.1f ? Mathf.RoundToInt(360f / minDiff) : 4;
            sides = Mathf.Clamp(sides, 2, 6);
            
            data.Type = sides == 2 ? "n2s" :
                       sides == 3 ? "n3s" :
                       sides == 4 ? "n4s" :
                       sides == 6 ? "n6s" : "n4s";

            SendReply(player, $"Определён тип: {data.Type.ToUpper()}");
        }
        
        #endregion
        
        #region UI Система
        
        private void CreateUI(BasePlayer player)
        {
            if (player == null) return;
            
            DestroyUI(player);
            var data = GetData(player.userID);

            var container = new CuiElementContainer();

            // Главная панель
            container.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.85" },
                RectTransform = { 
                    AnchorMin = "0.5 0", 
                    AnchorMax = "0.5 0", 
                    OffsetMin = "-220 20", 
                    OffsetMax = "220 200" 
                }
            }, "Hud", UILayer);

            // Заголовок
            container.Add(new CuiLabel
            {
                Text = { 
                    Text = "СИММЕТРИЧНОЕ СТРОИТЕЛЬСТВО", 
                    FontSize = 18, 
                    Align = TextAnchor.MiddleCenter, 
                    Color = "1 1 1 1" 
                },
                RectTransform = { AnchorMin = "0 0.8", AnchorMax = "1 1" }
            }, UILayer);

            // Статус
            container.Add(new CuiLabel
            {
                Text = { 
                    Text = data.Enabled ? "<color=#00ff00>АКТИВНО</color>" : "<color=#ff0000>НЕАКТИВНО</color>", 
                    FontSize = 18, 
                    Align = TextAnchor.MiddleCenter 
                },
                RectTransform = { AnchorMin = "0 0.65", AnchorMax = "1 0.8" }
            }, UILayer, "StatusLabel");

            // Статус центра
            string centerText = data.Center.HasValue ? 
                "<color=#00ff00>ЦЕНТР УСТАНОВЛЕН</color>" : 
                "<color=#ff8800>ЦЕНТР НЕ УСТАНОВЛЕН</color>";
                
            container.Add(new CuiLabel
            {
                Text = { 
                    Text = centerText, 
                    FontSize = 14, 
                    Align = TextAnchor.MiddleCenter 
                },
                RectTransform = { AnchorMin = "0 0.55", AnchorMax = "1 0.65" }
            }, UILayer, "CenterLabel");

            // Текущий тип
            container.Add(new CuiLabel
            {
                Text = { 
                    Text = $"Тип: <color=#ffd700>{data.Type.ToUpper()}</color>", 
                    FontSize = 15, 
                    Align = TextAnchor.MiddleCenter 
                },
                RectTransform = { AnchorMin = "0 0.45", AnchorMax = "1 0.55" }
            }, UILayer, "TypeLabel");

            // Кнопки
            AddButton(container, "ToggleBtn", data.Enabled ? "ВЫКЛЮЧИТЬ" : "ВКЛЮЧИТЬ", 
                     "0.05 0.28", "0.45 0.42", "0.4 0.6 0.4 0.9");
                     
            AddButton(container, "SetBtn", "УСТАНОВИТЬ ЦЕНТР + АВТО", 
                     "0.5 0.28", "0.95 0.42", "0.3 0.5 0.8 0.9");
                     
            AddButton(container, "DeleteBtn", "УДАЛИТЬ ЦЕНТР", 
                     "0.05 0.15", "0.95 0.27", "0.7 0.3 0.3 0.9");

            // Кнопки типов
            for (int i = 0; i < ValidSymmetryTypes.Length; i++)
            {
                string type = ValidSymmetryTypes[i];
                float xMin = 0.02f + i * 0.163f;
                float xMax = xMin + 0.15f;
                string color = data.Type == type ? "0.2 0.7 0.2 0.9" : "0.25 0.25 0.25 0.8";
                AddButton(container, "Type" + type, type.ToUpper(), 
                         $"{xMin} 0.02", $"{xMax} 0.13", color);
            }

            CuiHelper.AddUi(player, container);
        }

        private void AddButton(CuiElementContainer c, string name, string text, string min, string max, string color)
        {
            c.Add(new CuiButton
            {
                Button = { Command = $"ui {name}", Color = color },
                Text = { 
                    Text = text, 
                    FontSize = 14, 
                    Align = TextAnchor.MiddleCenter, 
                    Color = "1 1 1 1" 
                },
                RectTransform = { AnchorMin = min, AnchorMax = max }
            }, UILayer, name);
        }

        private void UpdateUI(BasePlayer player)
        {
            if (player == null) return;
            
            var data = GetData(player.userID);
            if (data.UiEnabled) 
                CreateUI(player);
        }

        private void DestroyUI(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, UILayer);
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player != null)
                DestroyUI(player);
        }
        
        #endregion
        
        #region Строительство
        
        private bool IsBuildableEntity(BaseEntity entity)
        {
            if (entity == null) return false;
            
            return entity is BuildingBlock || 
                   entity is Door || 
                   entity.prefabID == WindowBarsMetalPrefabId || 
                   entity.prefabID == WindowBarsWoodPrefabId;
        }

        private void OnEntityBuilt(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null) return;
            
            if (!permission.UserHasPermission(player.UserIDString, PermUse)) 
                return;

            var data = GetData(player.userID);
            if (!data.Enabled || !data.Center.HasValue || !data.CenterRot.HasValue) 
                return;

            if (!IsBuildableEntity(entity)) 
                return;

            var prefab = entity.PrefabName;
            var skin = entity.skinID;
            var health = entity.Health();
            
            if (health <= 0) return;

            BuildingGrade.Enum? grade = null;
            if (entity is BuildingBlock bb) 
                grade = bb.grade;

            var buildCost = (entity as BuildingBlock)?.BuildCost() ?? GetDeployCost(prefab);

            var type = data.Type;
            var isMirrored = type.StartsWith("m");
            if (!int.TryParse(type[1].ToString(), out int sides))
                sides = 4;

            var center = data.Center.Value;
            var centerRot = data.CenterRot.Value;
            var invCenterRot = Quaternion.Inverse(centerRot);

            var localPos = invCenterRot * (entity.transform.position - center);
            var localRot = invCenterRot * entity.transform.rotation;

            var symmetries = new List<List<Vector3>>();

            if (isMirrored)
            {
                var normal1 = centerRot * Vector3.forward; 
                normal1.Normalize();
                
                Vector3 normal2 = Vector3.zero;
                if (sides >= 4)
                {
                    normal2 = (centerRot * Vector3.right).normalized;
                }

                symmetries.Add(new List<Vector3> { normal1 });
                if (sides >= 4)
                {
                    symmetries.Add(new List<Vector3> { normal2 });
                    symmetries.Add(new List<Vector3> { normal1, normal2 });
                }
            }
            else
            {
                float step = 360f / sides;
                for (int i = 1; i < sides; i++)
                {
                    symmetries.Add(new List<Vector3> { new Vector3(0, step * i, 0) });
                }
            }

            timer.Once(0.1f, () =>
            {
                if (player == null) return;
                
                foreach (var sym in symmetries)
                {
                    Vector3 newLocalPos = localPos;
                    Quaternion newLocalRot = localRot;

                    if (isMirrored)
                    {
                        Vector3 vec = newLocalPos;
                        Matrix4x4 rotMatrix = Matrix4x4.Rotate(newLocalRot);

                        foreach (var normal in sym)
                        {
                            vec = Vector3.Reflect(vec, normal);
                            var refMatrix = ReflectionMatrix(normal);
                            rotMatrix = refMatrix * rotMatrix * refMatrix;
                        }

                        newLocalPos = vec;
                        newLocalRot = rotMatrix.rotation;

                        if (entity is Door) 
                            newLocalRot *= Quaternion.Euler(0, 180, 0);
                    }
                    else
                    {
                        var symRot = Quaternion.Euler(sym[0]);
                        newLocalPos = symRot * localPos;
                        newLocalRot = symRot * localRot;
                    }

                    var newPos = center + centerRot * newLocalPos;
                    var newRot = centerRot * newLocalRot;

                    // Проверка ресурсов
                    if (buildCost != null && buildCost.Count > 0)
                    {
                        bool canAfford = buildCost.All(c => 
                            c != null && 
                            c.itemDef != null && 
                            player.inventory.GetAmount(c.itemid) >= (int)c.amount);
                            
                        if (!canAfford)
                        {
                            SendReply(player, "Недостаточно ресурсов для симметричной копии.");
                            continue;
                        }
                        
                        foreach (var c in buildCost)
                        {
                            if (c != null && c.itemDef != null)
                                player.inventory.Take(null, c.itemid, (int)c.amount);
                        }
                    }

                    var newEntity = GameManager.server.CreateEntity(prefab, newPos, newRot);
                    if (newEntity == null) continue;

                    newEntity.OwnerID = player.userID;
                    newEntity.skinID = skin;
                    newEntity.Spawn();
                    newEntity.health = health;

                    if (newEntity is BuildingBlock newBb && grade.HasValue)
                    {
                        newBb.SetGrade(grade.Value);
                        newBb.health = health;
                    }

                    newEntity.SendNetworkUpdateImmediate();
                }
            });
        }

        private void OnBuildingBlockUpgrade(BuildingBlock block, BasePlayer player, BuildingGrade.Enum grade)
        {
            if (block == null || player == null) return;
            
            if (!permission.UserHasPermission(player.UserIDString, PermUse)) 
                return;

            var data = GetData(player.userID);
            if (!data.Enabled || !data.Center.HasValue || !data.CenterRot.HasValue) 
                return;

            var center = data.Center.Value;
            var centerRot = data.CenterRot.Value;
            var invCenterRot = Quaternion.Inverse(centerRot);
            var localPos = invCenterRot * (block.transform.position - center);

            var type = data.Type;
            var isMirrored = type.StartsWith("m");
            if (!int.TryParse(type[1].ToString(), out int sides))
                sides = 4;

            var symmetries = new List<List<Vector3>>();

            if (isMirrored)
            {
                var normal1 = centerRot * Vector3.forward; 
                normal1.Normalize();
                
                Vector3 normal2 = Vector3.zero;
                if (sides >= 4)
                {
                    normal2 = (centerRot * Vector3.right).normalized;
                }

                symmetries.Add(new List<Vector3> { normal1 });
                if (sides >= 4)
                {
                    symmetries.Add(new List<Vector3> { normal2 });
                    symmetries.Add(new List<Vector3> { normal1, normal2 });
                }
            }
            else
            {
                float step = 360f / sides;
                for (int i = 1; i < sides; i++)
                {
                    symmetries.Add(new List<Vector3> { new Vector3(0, step * i, 0) });
                }
            }

            timer.Once(0.1f, () =>
            {
                foreach (var sym in symmetries)
                {
                    Vector3 newLocalPos = localPos;

                    if (isMirrored)
                    {
                        Vector3 vec = newLocalPos;
                        foreach (var n in sym) 
                            vec = Vector3.Reflect(vec, n);
                        newLocalPos = vec;
                    }
                    else
                    {
                        var symRot = Quaternion.Euler(sym[0]);
                        newLocalPos = symRot * localPos;
                    }

                    var newPos = center + centerRot * newLocalPos;

                    var entities = Pool.GetList<BaseEntity>();
                    try
                    {
                        Vis.Entities(newPos, 0.5f, entities, Rust.LayerMask.GetMask("Construction"));
                        var symBlock = entities.FirstOrDefault(e => 
                            e is BuildingBlock && 
                            Vector3.Distance(e.transform.position, newPos) < 0.1f) as BuildingBlock;

                        if (symBlock == null || symBlock.grade == grade) 
                            continue;

                        symBlock.SetGrade(grade);
                        symBlock.health = symBlock.MaxHealth();
                        symBlock.SendNetworkUpdateImmediate();
                    }
                    finally
                    {
                        Pool.FreeList(ref entities);
                    }
                }
            });
        }
        
        #endregion
        
        #region Вспомогательные методы
        
        private Matrix4x4 ReflectionMatrix(Vector3 normal)
        {
            normal = normal.normalized;
            Matrix4x4 m = Matrix4x4.identity;
            
            m[0, 0] = 1 - 2 * normal.x * normal.x;
            m[0, 1] = -2 * normal.x * normal.y;
            m[0, 2] = -2 * normal.x * normal.z;
            
            m[1, 0] = -2 * normal.y * normal.x;
            m[1, 1] = 1 - 2 * normal.y * normal.y;
            m[1, 2] = -2 * normal.y * normal.z;
            
            m[2, 0] = -2 * normal.z * normal.x;
            m[2, 1] = -2 * normal.z * normal.y;
            m[2, 2] = 1 - 2 * normal.z * normal.z;
            
            return m;
        }

        private List<ItemAmount> GetDeployCost(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            
            if (prefab.Contains("door.hinged.wood")) 
                return CreateItemList(("wood", 300));
            if (prefab.Contains("door.hinged.metal")) 
                return CreateItemList(("metal.fragments", 150));
            if (prefab.Contains("door.hinged.toptier")) 
                return CreateItemList(("metal.refined", 20), ("gears", 5));
            if (prefab.Contains("door.double.hinged.wood")) 
                return CreateItemList(("wood", 350));
            if (prefab.Contains("door.double.hinged.metal")) 
                return CreateItemList(("metal.fragments", 200));
            if (prefab.Contains("door.double.hinged.toptier")) 
                return CreateItemList(("metal.refined", 25), ("gears", 5));
            if (prefab.Contains("wall.frame.garagedoor")) 
                return CreateItemList(("metal.fragments", 300), ("gears", 2));
            if (prefab.Contains("window.bars.wood")) 
                return CreateItemList(("wood", 50));
            if (prefab.Contains("window.bars.metal")) 
                return CreateItemList(("metal.fragments", 25));
                
            return null;
        }

        private List<ItemAmount> CreateItemList(params (string itemName, int amount)[] items)
        {
            var result = new List<ItemAmount>();
            
            foreach (var item in items)
            {
                var itemDef = ItemManager.FindItemDefinition(item.itemName);
                if (itemDef != null)
                {
                    result.Add(new ItemAmount(itemDef, item.amount));
                }
            }
            
            return result.Count > 0 ? result : null;
        }
        
        #endregion
    }
}