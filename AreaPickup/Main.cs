using HarmonyLib;
using SRML;
using SRML.SR;
using SRML.Utils.Enum;
using SRML.Console;
using SRML.Config.Attributes;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using InControl;
using AssetsLib;
using static AssetsLib.UIUtils;
using static AssetsLib.TextureUtils;
using Console = SRML.Console.Console;
using Object = UnityEngine.Object;

namespace AreaPickup
{
    public class Main : ModEntryPoint
    {
        internal static Assembly modAssembly = Assembly.GetExecutingAssembly();
        internal static string modName = $"{modAssembly.GetName().Name}";
        internal static string modDir = $"{Environment.CurrentDirectory}\\SRML\\Mods\\{modName}";
        internal static GameObject areaCollectionPrefab;
        internal static List<Identifiable.Id> filterInclude = new List<Identifiable.Id>();
        internal static List<Identifiable.Id> filterExclude = new List<Identifiable.Id>();
        internal static IdGroup filterGroups = IdGroup.None;
        internal static Sprite foodIcon = LoadImage("any_food_icon.png").CreateSprite();
        internal static Sprite slimeIcon = LoadImage("any_slime_icon.png").CreateSprite();
        internal static Sprite resourceIcon = LoadImage("any_resource_icon.png").CreateSprite();
        internal static Sprite anyIcon = LoadImage("any_icon.png").CreateSprite();
        internal static Sprite disableIcon = LoadImage("disable_icon.png").CreateSprite();
        internal static Sprite vacIcon;
        internal static Sprite miscIcon;
        internal static GameObject vacFx;
        internal static Dictionary<IdGroup, Sprite> groupIcons = new Dictionary<IdGroup, Sprite>();
        internal static Dictionary<Identifiable.Id, Sprite> itemIcons = new Dictionary<Identifiable.Id, Sprite>();
        internal static Dictionary<Sprite, Sprite> inactiveIcons = new Dictionary<Sprite, Sprite>();
        internal static Dictionary<Sprite, Sprite> excludeIcons = new Dictionary<Sprite, Sprite>();
        internal static DroneUIProgramPicker uiPrefab2;
        internal static DroneUIProgramButton buttonPrefab;
        internal static Console.ConsoleInstance Console;
        internal static PlayerAction openMenu;

        public override void PreLoad()
        {
            Console = ConsoleInstance;
            HarmonyInstance.PatchAll();
            var iconFetch = Resources.FindObjectsOfTypeAll<Sprite>().Find(
                (x) => x.name == "iconCategoryVeggie",
                (x) => x.name == "iconCategoryPlort",
                (x) => x.name == "iconCategoryFruit",
                (x) => x.name == "iconCategoryMeat",
                (x) => x.name == "tut_vac1",
                (x) => x.name == "iconDecorizerOrnaments"
                );
            vacIcon = iconFetch[4];
            miscIcon = iconFetch[5];
            groupIcons = new Dictionary<IdGroup, Sprite>()
            {
                [IdGroup.Slimes] = slimeIcon,
                [IdGroup.Plorts] = iconFetch[1],
                [IdGroup.Fruit] = iconFetch[2],
                [IdGroup.Veggies] = iconFetch[0],
                [IdGroup.Meat] = iconFetch[3],
                [IdGroup.Food] = foodIcon,
                [IdGroup.Resources] = resourceIcon,
                [IdGroup.Decorations] = miscIcon
            };
            SRCallbacks.OnActorSpawn += (x, y, z) =>
            {
                if (x != Identifiable.Id.PLAYER)
                    return;
                Object.Instantiate(areaCollectionPrefab, y.transform);
            };
            areaCollectionPrefab = new GameObject("").CreatePrefabCopy();
            areaCollectionPrefab.name = "Area Collection";
            var collide = areaCollectionPrefab.AddComponent<SphereCollider>();
            collide.radius = 10;
            collide.isTrigger = true;
            areaCollectionPrefab.AddComponent<ItemCollector>().enabled = false;

            uiPrefab2 = Resources.FindObjectsOfTypeAll<DroneUIProgramPicker>().First((x) => !x.name.EndsWith("(Clone)"));
            buttonPrefab = Resources.FindObjectsOfTypeAll<DroneUIProgramButton>().First((x) => x.gameObject.name == "DroneUIProgramButton");
            TranslationPatcher.AddUITranslation("b.food", "Food");
            TranslationPatcher.AddUITranslation("b.misc", "Misc");
            TranslationPatcher.AddUITranslation("b.plorts", "Plorts");
            TranslationPatcher.AddUITranslation("b.groups", "Filter Groups");
            TranslationPatcher.AddUITranslation("t.filters", "Filter Toggles");
            TranslationPatcher.AddUITranslation("b.disable_filters", "Disable All Filters");
            TranslationPatcher.AddPediaTranslation("m.upgrade.name.personal.area_pickup", "Auto-Pickup");
            TranslationPatcher.AddPediaTranslation("m.upgrade.desc.personal.area_pickup", "Auto picks up the items in the surrounding area based on the filters set");
            LookupRegistry.RegisterUpgradeEntry(Ids.AREA_PICKUP.Define(vacIcon, 950));
            PersonalUpgradeRegistry.RegisterUpgradeLock(
                Ids.AREA_PICKUP,
                (x) => new PlayerState.UpgradeLocker(
                    x,
                    () => SceneContext.Instance.ProgressDirector.model != null
                        && SceneContext.Instance.ProgressDirector.HasProgress(ProgressDirector.ProgressType.UNLOCK_RUINS),
                    12
                )
            );
            PersonalUpgradeRegistry.RegisterUpgradeCallback(Ids.AREA_PICKUP, (x, y) => SceneContext.Instance.Player.GetComponentInChildren<ItemCollector>().enabled = true);

            (openMenu = BindingRegistry.RegisterBindedAction("key.autoPickupOpen")).AddDefaultBinding(Key.Y);
            TranslationPatcher.AddUITranslation("key.key.autopickupopen", "Open Auto-Pickup Filter Menu");
        }

        public override void PostLoad()
        {
            itemIcons = new Dictionary<Identifiable.Id, Sprite>();
            foreach (var slimeDef in GameContext.Instance.SlimeDefinitions.slimeDefinitionsByIdentifiable)
            {
                var prefab = GameContext.Instance.LookupDirector.identifiablePrefabs.FirstOrDefault((x) => x && x.GetComponent<Identifiable>().id == slimeDef.Key);
                if (prefab && prefab.GetComponent<Vacuumable>() && prefab.GetComponent<Vacuumable>().size == Vacuumable.Size.NORMAL && slimeDef.Value.AppearancesDefault[0].Icon)
                    itemIcons[slimeDef.Key] = slimeDef.Value.AppearancesDefault[0].Icon;
            }
            foreach (var vacDef in GameContext.Instance.LookupDirector.vacItemDict)
            {
                if (itemIcons.ContainsKey(vacDef.Key))
                    continue;
                var prefab = GameContext.Instance.LookupDirector.identifiablePrefabs.FirstOrDefault((x) => x && x.GetComponent<Identifiable>().id == vacDef.Key);
                if (prefab && prefab.GetComponent<Vacuumable>() && prefab.GetComponent<Vacuumable>().size == Vacuumable.Size.NORMAL && vacDef.Value.icon)
                    itemIcons[vacDef.Key] = vacDef.Value.icon;
            }
            foreach (var icon in groupIcons.Values)
            {
                var s = icon.GetReadable();
                s.texture.ModifyTexturePixels((x) => new Color(1, 1, 1, x.a).Multiply(x.grayscale));
                inactiveIcons[icon] = s;
                s.texture.Compress(true);
            }
            foreach (var icon in itemIcons.Values)
            {
                var s = icon.GetReadable();
                s.texture.ModifyTexturePixels((x) => new Color(1, 1, 1, x.a).Multiply(x.grayscale));
                inactiveIcons[icon] = s;
                s.texture.Compress(true);
                s = icon.GetReadable();
                s.texture.ModifyTexturePixels((x) => new Color(1, 0, 0, x.a).Multiply(x.grayscale));
                excludeIcons[icon] = s;
                s.texture.Compress(true);
            }
            vacFx = Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault((x) => x.name == "FX vacAcquire");
        }

        public static void Log(string message) => Console.Log($"[{modName}]: " + message);
        public static void LogError(string message) => Console.LogError($"[{modName}]: " + message);
        public static void LogWarning(string message) => Console.LogWarning($"[{modName}]: " + message);
        public static void LogSuccess(string message) => Console.LogSuccess($"[{modName}]: " + message);

        public override void Update()
        {
            if (openMenu.WasPressed)
                OpenFilterMenu();
        }

        public static void OpenFilterMenu(IdGroup menuType = (IdGroup)(-1))
        {
            var options = new List<ModeOption>();
            if (menuType == (IdGroup)(-1))
                options.AddRange(new ModeOption[]
                {
                    new ModeOption(anyIcon,"b.groups",() => OpenFilterMenu(IdGroup.None)),
                    new ModeOption(groupIcons[IdGroup.Food],"b.food",() => OpenFilterMenu(IdGroup.Food)),
                    new ModeOption(groupIcons[IdGroup.Slimes],"b.slimes",() => OpenFilterMenu(IdGroup.Slimes)),
                    new ModeOption(groupIcons[IdGroup.Resources],"b.resources",() => OpenFilterMenu(IdGroup.Resources)),
                    new ModeOption(miscIcon,"b.misc",() => OpenFilterMenu(IdGroup.Plorts)),
                    new ModeOption(disableIcon,"b.disable_filters",() => {filterExclude.Clear(); filterInclude.Clear(); filterGroups = IdGroup.None; OpenFilterMenu(); })
                });
            else if (menuType == IdGroup.None)
                foreach (var group in groupIcons)
                    options.Insert(0, new AdaptiveModeOption(
                        () => filterGroups.HasFlag(group.Key)
                            ? group.Value
                            : inactiveIcons[group.Value],
                        () => GameContext.Instance.MessageDirector.Get("ui", "b." + group.Key.ToString().ToLower()),
                        () =>
                        {
                            if (filterGroups.HasFlag(group.Key))
                                filterGroups &= ~group.Key;
                            else
                                filterGroups |= group.Key;
                        }));
            else
            {
                foreach (var item in itemIcons)
                    if (
                        menuType == IdGroup.Food
                        ? Identifiable.IsFood(item.Key) || Identifiable.IsChick(item.Key)
                        : menuType == IdGroup.Slimes
                        ? Identifiable.IsSlime(item.Key) || Identifiable.IsPlort(item.Key)
                        : menuType == IdGroup.Resources
                        ? Identifiable.IsCraft(item.Key)
                        : !Identifiable.IsFood(item.Key) && !Identifiable.IsChick(item.Key) && !Identifiable.IsSlime(item.Key) && !Identifiable.IsPlort(item.Key) && !Identifiable.IsCraft(item.Key)
                        )
                        options.Add(new AdaptiveModeOption(
                            () => filterInclude.Contains(item.Key)
                                ? item.Value
                                : filterExclude.Contains(item.Key)
                                    ? excludeIcons[item.Value]
                                    : inactiveIcons[item.Value],
                            () => Identifiable.GetName(item.Key),
                        () =>
                        {
                            if (filterInclude.Contains(item.Key))
                            {
                                filterExclude.Add(item.Key);
                                filterInclude.Remove(item.Key);
                            }
                            else if (filterExclude.Contains(item.Key))
                                filterExclude.Remove(item.Key);
                            else
                                filterInclude.Add(item.Key);
                        }));
                options.Sort((x, y) => string.Compare(x.GetName(), y.GetName()) > 0);
            }
            var flag = menuType == (IdGroup)(-1);
            Action close = () => ItemCollector.collectors.Do((x) => x.TryCollectInArea());
            if (!flag)
                close = () => OpenFilterMenu();
            CreateSelectionUI("t.filters", vacIcon, options, flag, close);
        }
    }


    public enum IdGroup
    {
        None = 0,
        Slimes = 1,
        Plorts = 2,
        Fruit = 4,
        Veggies = 8,
        Meat = 16,
        Food = 32,
        Resources = 64,
        Decorations = 128
    }

    [EnumHolder]
    public static class Ids
    {
        public static PlayerState.Upgrade AREA_PICKUP;
    }

    public class ItemCollector : SRBehaviour
    {
        public static List<ItemCollector> collectors = new List<ItemCollector>();
        public static List<Vacuumable> collected = new List<Vacuumable>();
        Func<Ammo> Storage;
        void Awake()
        {
            var player = SceneContext.Instance.PlayerState;
            if (player)
                Storage = () => player.Ammo;
        }
        void OnEnable() => collectors.Add(this);
        void OnDisable() => collectors.Remove(this);
        void OnDestroy() => collectors.Remove(this);
        void OnTriggerEnter(Collider other)
        {
            if (!enabled)
                return;
            var vac = other.GetComponentInParent<Vacuumable>();
            if (!vac || collected.Contains(vac))
                return;
            MaybePickup(vac);
        }

        public enum PickupResult
        {
            Fail,
            TemporaryFail,
            Success
        }
        public PickupResult MaybePickup(Vacuumable vac)
        {
            if (!vac || vac.size != Vacuumable.Size.NORMAL)
                return PickupResult.Fail;
            if (!SceneContext.Instance.GameModel.AllActors().ContainsKey(Identifiable.GetActorId(vac.gameObject)))
                return PickupResult.Fail;
            var id = vac.GetComponent<Identifiable>();
            if (!id || id.id == Identifiable.Id.NONE)
                return PickupResult.Fail;
            var ammo = Storage?.Invoke();
            var cycle = vac.GetComponent<ResourceCycle>();
	        if (ammo != null
                && id.id.IncludedInFilter()
                && (!cycle || cycle.GetState() == ResourceCycle.State.EDIBLE || cycle.GetState() == ResourceCycle.State.RIPE)
                && !(Config.IgnoreNewlyFired && vac.isLaunched())
		        && ammo.MaybeAddToSlot(id.id, id))
            {
                collected.Add(vac);
                collected.RemoveAll((x) => !x);
                SceneContext.Instance.PediaDirector.MaybeShowPopup(id.id);
                StartCoroutine(StartPulling(vac));
		        return PickupResult.Success;
            }
            return PickupResult.TemporaryFail;
        }
        public void TryCollectInArea()
        {
            if (!enabled)
                return;
            StartCoroutine(RetryArea());
        }
        int retry;
        IEnumerator RetryArea()
        {
            if (retry > 0)
            {
                retry = 2;
                yield break;
            }
            retry = 2;
            var c = GetComponents<Collider>().ToDictionary(x => x, x => x.transform.localScale);
            foreach (var i in c.Keys)
                i.transform.localScale = new Vector3(0, 0, 0);
            do
            {
                yield return new WaitForFixedUpdate();
                retry--;
            } while (retry > 0);
            foreach (var i in c)
                i.Key.transform.localScale = i.Value;
            yield break;
        }
        IEnumerator StartPulling(Vacuumable vacuumable)
        {
            vacuumable.GetComponent<ResourceCycle>()?.DetachFromJoint();
            foreach (var c in vacuumable.GetComponentsInChildren<Collider>())
                c.enabled = false;
            float fullScale = vacuumable.transform.localScale.x;
            TweenSettingsExtensions.SetEase(ShortcutExtensions.DOScale(vacuumable.gameObject.transform, fullScale * 0.1f, 0.2f), Ease.Linear);
            WeaponVacuum.MoveTowards moveTowards = vacuumable.gameObject.AddComponent<WeaponVacuum.MoveTowards>();
            moveTowards.dest = transform;
            SceneContext.Instance.GameModel.DestroyActorModel(vacuumable.gameObject);
            yield return new WaitForSeconds(0.2f);
            if (vacuumable)
            {
                SpawnAndPlayFX(Main.vacFx, gameObject, Vector3.zero, Quaternion.identity);
                Destroyer.Destroy(vacuumable.gameObject, "ItemCollector:StartPulling");
            }
            yield break;
        }
    }

    [ConfigFile("settings")]
    static class Config
    {
        public static bool IgnoreNewlyFired = true;
    }

    static class ExtentionMethods
    {
        public static bool Includes(this IdGroup group, Identifiable.Id id) =>
            (group.HasFlag(IdGroup.Slimes) && Identifiable.IsSlime(id))
            || (group.HasFlag(IdGroup.Plorts) && Identifiable.IsPlort(id))
            || (group.HasFlag(IdGroup.Fruit) && Identifiable.IsFruit(id))
            || (group.HasFlag(IdGroup.Veggies) && Identifiable.IsVeggie(id))
            || (group.HasFlag(IdGroup.Meat) && Identifiable.MEAT_CLASS.Contains(id))
            || (group.HasFlag(IdGroup.Food) && Identifiable.IsFood(id))
            || (group.HasFlag(IdGroup.Resources) && Identifiable.IsCraft(id) && Identifiable.IsNonSlimeResource(id))
            || (group.HasFlag(IdGroup.Decorations) && (Identifiable.IsEcho(id) || Identifiable.IsEchoNote(id) || Identifiable.IsOrnament(id)));
        public static bool IncludedInFilter(this Identifiable.Id id) => (Main.filterGroups.Includes(id) && !Main.filterExclude.Contains(id)) || Main.filterInclude.Contains(id);

        public static T[] Find<T>(this IEnumerable<T> c, params Predicate<T>[] preds)
        {
            var f = new T[preds.Length];
            if (preds.Length == 0)
                return f;
            foreach (var i in c)
            {
                var k = 0;
                for (int j = 0; j < preds.Length; j++)
                {
                    if (f[j] != null)
                    {
                        k++;
                        continue;
                    }
                    if (preds[j](i))
                    {
                        k++;
                        f[j] = i;
                    }
                }
                if (k == preds.Length)
                    continue;
            }
            return f;
        }
        public static void Sort<T>(this List<T> collection, Func<T, T, bool> comparison)
        {
            var o = collection.ToList();
            collection.Clear();
            foreach (var i in o)
            {
                var ind = collection.FindIndex((x) => comparison(x, i));
                if (ind == -1)
                    collection.Add(i);
                else
                    collection.Insert(ind, i);
            }
        }

        public static UpgradeDefinition Define(this PlayerState.Upgrade upgrade, Sprite sprite, int cost)
        {
            var o = ScriptableObject.CreateInstance<UpgradeDefinition>();
            o.upgrade = upgrade;
            o.icon = sprite;
            o.cost = cost;
            return o;
        }
    }

    [HarmonyPatch(typeof(MessageDirector), "LoadBundle")]
    static class Patch_Update
    {
        static void Postfix(MessageDirector __instance, string path, ref ResourceBundle __result)
        {
            if (__result != null && path == "ui")
            {
                var dictUI = __result.dict;
                dictUI["b.fruit"] = dictUI["m.drone.target.name.category_fruits"];
                dictUI["b.veggies"] = dictUI["m.drone.target.name.category_veggies"];
                dictUI["b.meat"] = dictUI["m.drone.target.name.category_meats"];
            }
        }
    }

    [HarmonyPatch(typeof(ResourceCycle),"Ripen")]
    static class Patch_RipenCycle
    {
        static void Postfix(ResourceCycle __instance)
        {
            var vac = __instance.GetComponent<Vacuumable>();
            if (vac)
                ItemCollector.collectors.Do((x) => x.TryCollectInArea());
        }
    }

    [HarmonyPatch(typeof(Vacuumable), "SetLaunched")]
    static class Patch_VacDelaunch
    {
        static void Postfix(Vacuumable __instance, bool launched)
        {
            if (!launched)
                ItemCollector.collectors.Do(x => x.TryCollectInArea());
        }
    }

    [HarmonyPatch(typeof(WeaponVacuum), "ExpelAmmo")]
    static class Patch_VacShoot
    {
        static void Postfix() => ItemCollector.collectors.Do(x => x.TryCollectInArea());
    }
}