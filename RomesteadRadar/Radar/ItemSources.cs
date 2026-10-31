using System;
using System.Collections.Generic;
using Candide.Database.Doodad;
using CandideServer.Data.DataModels;
using CandideServer.Data.ServerDataSetups;
using CandideServer.Database;
using CandideCreator.Shared.EntityComponentStuff;
using CandideCreator.Shared.Forms;
using CandideCreator.Shared.Helpers;
using CandideCreator.Shared.Models;
using Shared;
using Shared.Combat.Spells.Parameters;
using Shared.Data;
using Shared.Data.DataModels;
using Shared.Models.DropTables;
using Shared.Models.Items;

namespace RomesteadRadar
{
    /// <summary>
    /// 物品 → 可采集来源（实体 GUID / 作物 ID）的映射。
    ///
    /// 映射来源分三层：
    /// 1. 手工兜底（橄榄树、果树、矿脉、树木等）；
    /// 2. 读取游戏 doodad 数据里的 ServerController 参数（fruit / berry / droptable_id / 物品 ID）；
    /// 3. 用配方反推：可堆叠的食物/材料如果没有直接来源，就向上找原料的来源。
    /// </summary>
    internal static class ItemSources
    {
        internal sealed class SourceSpec
        {
            public readonly HashSet<Guid> EntityGuids = new HashSet<Guid>();

            public readonly HashSet<string> CropIds = new HashSet<string>();

            /// <summary>果实/浆果/草药这类来源，只有当前可交互（成熟/未采摘）时才算。</summary>
            public bool RequireInteractable;
        }

        /// <summary>
        /// 雷达目标清单.html 中标记为“✅ 可寻找”的物品。
        /// 不在这个清单里的物品（包括待决定和不支持的）不会出现在物品雷达里。
        /// </summary>
        private static readonly HashSet<string> SummarySupportedItems = new HashSet<string>
        {
            "material:linen", "material:ember_orchid", "material:ember_cloth", "material:wheat",
            "material:herb:bay_leaf", "material:wood_plank", "material:wood_stick", "material:olive_oil",
            "material:olive_paste", "material:coal", "material:flint_shard", "material:flint:pilum_head",
            "material:copper_needle", "material:copper:pilum_head", "material:copper_nails", "material:copper_bar",
            "material:herb:pepper", "material:aloe_vera", "material:herb:coriander", "material:grape_paste",
            "material:herb:mint", "material:gold_bar", "material:iron:pilum_head", "material:iron_rod",
            "material:iron_needle", "material:iron_nails", "material:iron_bar", "material:silver:pilum_head",
            "material:silver_bar", "material:tin_bar", "material:bronze_needle", "material:bronze:pilum_head",
            "material:bronze_nails", "material:bronze_bar", "material:flour",
            "food:phoenix_and_olives", "food:cabbage", "food:pinsa", "food:cheese",
            "food:dulca_domestica", "food:libum", "food:apricot", "food:minutal",
            "food:pine_nuts", "food:olive", "food:mushroom:poison", "food:deepfried_fish",
            "food:crostata", "food:minutal_vulcanum", "food:mushroom:boletus", "food:gustum_de_praecoquis",
            "food:berry:white_currant", "food:emperors_salad", "food:berry:red_currant", "food:meatballs",
            "food:globi", "food:apple", "food:patina_de_malis", "food:minutal_matianum",
            "food:moretum", "food:grape", "food:wine", "food:shroom_bar",
            "food:mushroom_skewer", "food:scorpion_skewer", "food:salamander_skewer", "food:bread",
            "food:mushroom:chanterelle", "food:berry:blackberry",
            "seed:flax", "seed:ember_orchid", "seed:cabbage", "seed:cumin",
            "seed:wheat", "seed:bay_leaf", "seed:tree:apricot", "seed:tree:pine",
            "seed:tree", "seed:tree:palm", "seed:olive", "seed:mushroom:amanita",
            "seed:mushroom:boletus", "seed:berry:whitecurrant", "seed:berry:redcurrant", "seed:pepper",
            "seed:aloe_vera", "seed:coriander", "seed:tree:apple", "seed:grape",
            "seed:mint", "seed:mushroom:chanterelle", "seed:berry:blackberry"
        };

        private static readonly Dictionary<string, SourceSpec> Map = new Dictionary<string, SourceSpec>();

        private static readonly Dictionary<string, List<ItemAmount[]>> RecipesByOutput = new Dictionary<string, List<ItemAmount[]>>();

        private static bool _built;

        private static readonly Guid[] TreeGuids =
        {
            EntityIds.TreeEntityGuid,
            EntityIds.SkinnyTreeGuid,
            EntityIds.CypressGuid,
            EntityIds.SwampTreeGuid,
            EntityIds.PalmTreeEntityGuid,
            EntityIds.PineTreeEntityGuid,
            EntityIds.OliveTreeEntityGuid,
            EntityIds.AppleTreeEntityGuid,
            EntityIds.ApricotTreeEntityGuid
        };

        private static readonly Guid[] BerryPatchGuids =
        {
            Guid.Parse("da6343e0-4bf0-417d-967b-115546d746c7"), // nature/berry_patch_small
            Guid.Parse("d8d94a2f-99f9-4fc3-8cb1-3ee6cccabffc")  // nature/berry_patch_big
        };

        /// <summary>确保映射已经构建；数据表在游戏启动后才加载，因此延迟到第一次使用。</summary>
        internal static void EnsureBuilt()
        {
            if (_built)
                return;

            Map.Clear();
            RecipesByOutput.Clear();

            AddStaticMappings();
            AddCropMappings();
            ScanDoodads();
            BuildRecipeIndex();

            // 先标记完成，避免配方反推里调用 Get() 时再次进入 EnsureBuilt。
            _built = true;
            ResolveRecipeSources();
        }

        internal static SourceSpec Get(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return null;

            EnsureBuilt();
            return Map.TryGetValue(itemId, out SourceSpec spec) ? spec : null;
        }

        /// <summary>种子类物品对应的作物 ID（例如 seed:olive → crop:olive）。</summary>
        internal static string GetPlacedCropId(string itemId)
        {
            ItemData item = ItemDataBase.GetItemDataOrNull(itemId);
            if (item?.Usable?.SpellTypeArgs is PlaceCropSpellArgs args)
                return args.CropId;
            return null;
        }

        internal static bool HasAnySource(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || !SummarySupportedItems.Contains(itemId))
                return false;

            EnsureBuilt();
            SourceSpec spec = Map.TryGetValue(itemId, out SourceSpec value) ? value : null;
            if (spec != null && (spec.EntityGuids.Count > 0 || spec.CropIds.Count > 0))
                return true;
            return GetPlacedCropId(itemId) != null;
        }

        private static SourceSpec SpecFor(string itemId)
        {
            if (!Map.TryGetValue(itemId, out SourceSpec spec))
            {
                spec = new SourceSpec();
                Map[itemId] = spec;
            }
            return spec;
        }

        private static void AddEntity(string itemId, Guid guid, bool requireInteractable)
        {
            SourceSpec spec = SpecFor(itemId);
            spec.EntityGuids.Add(guid);
            spec.RequireInteractable |= requireInteractable;
        }

        private static void AddCrop(string itemId, string cropId)
        {
            if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(cropId))
                return;
            SpecFor(itemId).CropIds.Add(cropId);
        }

        private static void AddStaticMappings()
        {
            // 所有树都能提供木棍/木材；木棍是砍树直接掉的。
            for (int i = 0; i < TreeGuids.Length; i++)
            {
                AddEntity("material:wood_stick", TreeGuids[i], false);
                AddEntity("material:wood_plank", TreeGuids[i], false);
                AddEntity("seed:tree", TreeGuids[i], false);
            }

            // 果树：果实和对应的树苗都定位到“能收集的果树”。
            AddEntity("food:olive", EntityIds.OliveTreeEntityGuid, true);
            AddEntity("seed:olive", EntityIds.OliveTreeEntityGuid, true);
            AddEntity("food:apple", EntityIds.AppleTreeEntityGuid, true);
            AddEntity("seed:tree:apple", EntityIds.AppleTreeEntityGuid, true);
            AddEntity("food:apricot", EntityIds.ApricotTreeEntityGuid, true);
            AddEntity("seed:tree:apricot", EntityIds.ApricotTreeEntityGuid, true);
            AddEntity("food:pine_nuts", EntityIds.PineTreeEntityGuid, true);
            AddEntity("seed:tree:pine", EntityIds.PineTreeEntityGuid, true);
            AddEntity("seed:tree:palm", EntityIds.PalmTreeEntityGuid, true);

            // 浆果：野外浆果丛。
            for (int i = 0; i < BerryPatchGuids.Length; i++)
            {
                AddEntity("food:berry:blackberry", BerryPatchGuids[i], true);
                AddEntity("food:berry:red_currant", BerryPatchGuids[i], true);
                AddEntity("food:berry:white_currant", BerryPatchGuids[i], true);
            }

            // 芦荟。
            AddEntity("material:aloe_vera", EntityIds.AloeBush, true);
            AddEntity("seed:aloe_vera", EntityIds.AloeBush, true);

            // 矿物。
            AddEntity("material:flint_shard", EntityIds.FlintEntityGuid, false);
            AddEntity("material:coal", EntityIds.CoalVeinGuid, false);
            AddEntity("material:copper_bar", EntityIds.CopperVeinGuid, false);
            AddEntity("material:tin_bar", EntityIds.TinVeinGuid, false);
            AddEntity("material:iron_bar", EntityIds.IronVeinGuid, false);
            AddEntity("material:bronze_bar", EntityIds.CopperVeinGuid, false);
            AddEntity("material:bronze_bar", EntityIds.TinVeinGuid, false);
            AddEntity("material:silver_bar", Guid.Parse("f245503c-a248-4e05-a229-93cf2acbad36"), false);
            AddEntity("material:gold_bar", Guid.Parse("5753e527-3bc0-4d6f-a27a-bc1654670bec"), false);
        }

        /// <summary>用作物数据把“产物 → 作物 ID”以及野外作物实体连起来。</summary>
        private static void AddCropMappings()
        {
            foreach (CropData crop in CropDataBase.CropDataMap.Values)
            {
                AddCropFromDropTable(crop.DropTable, crop.Id);
                AddCropFromDropTable(crop.WildDropTable, crop.Id);

                if (!string.IsNullOrEmpty(crop.OnRemovedDropItemId))
                    AddCrop(crop.OnRemovedDropItemId, crop.Id);

                foreach (ItemData seed in ItemDataBase.DataMap.Values)
                {
                    if (seed.Usable?.SpellTypeArgs is PlaceCropSpellArgs args && args.CropId == crop.Id)
                        AddCrop(seed.Id, crop.Id);
                }
            }

            // EntityIds.CropEntities：GUID → crop ID，用于把野外作物实体的 GUID 加到产物上。
            foreach (KeyValuePair<Guid, string> pair in EntityIds.CropEntities)
            {
                CropData crop = CropDataBase.GetCropOrNull(pair.Value);
                if (crop == null)
                    continue;

                HashSet<string> produced = new HashSet<string>();
                CollectDropTableItems(crop.DropTable, produced, 0);
                CollectDropTableItems(crop.WildDropTable, produced, 0);
                if (!string.IsNullOrEmpty(crop.OnRemovedDropItemId))
                    produced.Add(crop.OnRemovedDropItemId);

                foreach (string itemId in produced)
                    AddEntity(itemId, pair.Key, true);
            }
        }

        private static void AddCropFromDropTable(DropTableSystem table, string cropId)
        {
            if (table == null)
                return;
            HashSet<string> items = new HashSet<string>();
            CollectDropTableItems(table, items, 0);
            foreach (string itemId in items)
                AddCrop(itemId, cropId);
        }

        /// <summary>扫描所有 doodad 的 ServerController 参数，找出控制器直接配置的物品 / 果实 / 浆果 / 掉落表。</summary>
        private static void ScanDoodads()
        {
            HashSet<Guid> scanned = new HashSet<Guid>();

            foreach (DoodadDatabase database in DoodadDatabaseManager.Databases.Values)
            {
                foreach (BaseDoodad doodad in database.GuidMap.Values)
                {
                    if (scanned.Add(doodad.Guid))
                        ScanDoodad(doodad);
                }
            }

            // 有些 doodad 只登记在 BaseDoodads 里。
            foreach (BaseDoodad doodad in DoodadDatabaseManager.BaseDoodads.Values)
            {
                if (scanned.Add(doodad.Guid))
                    ScanDoodad(doodad);
            }
        }

        private static void ScanDoodad(BaseDoodad doodad)
        {
            if (!doodad.EntityComponents.TryGetValue("Controller", out EntityComponent component) || !component.IsActive)
                return;
            if (!component.Fields.TryGetValue("ServerController", out EntityComponentField field))
                return;
            if (!(field.Value is ControllerFormValue controller))
                return;

            string controllerName = controller.Name ?? string.Empty;
            if (!IsResourceController(controllerName))
                return;

            Parameters parameters = controller.Parameters;
            bool requireInteractable = IsReadyGatedController(controllerName);

            if (controllerName.Contains("FruitTree"))
            {
                string fruit = parameters.GetString("fruit", string.Empty);
                string fruitItem = FruitItemId(fruit);
                if (fruitItem != null)
                    AddEntity(fruitItem, doodad.Guid, true);
            }

            if (controllerName.Contains("BerryBush"))
            {
                string berry = parameters.GetString("berry", string.Empty);
                if (string.IsNullOrEmpty(berry) || berry == "None")
                {
                    // 随机浆果类型：三种浆果都指向这株灌木。
                    AddEntity("food:berry:white_currant", doodad.Guid, true);
                    AddEntity("food:berry:red_currant", doodad.Guid, true);
                    AddEntity("food:berry:blackberry", doodad.Guid, true);
                }
                else
                {
                    string berryItem = BerryItemId(berry);
                    if (berryItem != null)
                        AddEntity(berryItem, doodad.Guid, true);
                }
            }

            // 参数里直接写着物品 ID 的控制器（例如某些资源/植物）。
            foreach (KeyValuePair<string, string> kv in parameters.Dictionary)
            {
                string value = kv.Value;
                if (!string.IsNullOrEmpty(value) && ItemDataBase.GetItemDataOrNull(value) != null)
                    AddEntity(value, doodad.Guid, requireInteractable);
            }

            // 掉落表（仅主机/单机端有服务端掉落表数据）。
            string dropTableId = parameters.GetString("droptable_id", null);
            if (string.IsNullOrEmpty(dropTableId))
                dropTableId = parameters.GetString("droptable", null);
            if (!string.IsNullOrEmpty(dropTableId))
                AddDropTableItems(dropTableId, doodad.Guid, requireInteractable, 0);
        }

        private static bool IsResourceController(string name)
        {
            return ContainsAny(name, "Tree", "Bush", "Plant", "Harvest", "Vein", "Rock", "Resource", "Geode", "Sulphur", "Vent", "Berry", "Fruit", "Beehive", "Crop");
        }

        private static bool IsReadyGatedController(string name)
        {
            return ContainsAny(name, "FruitTree", "BerryBush", "Harvestable", "Bush", "SmallPlant", "SpikyBush", "Beehive", "FruitSpawner");
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            for (int i = 0; i < keywords.Length; i++)
            {
                if (text.Contains(keywords[i]))
                    return true;
            }
            return false;
        }

        private static string FruitItemId(string fruit)
        {
            switch (fruit)
            {
                case "Olives":
                    return "food:olive";
                case "Grapes":
                    return "food:grape";
                case "Apples":
                    return "food:apple";
                case "Apricot":
                    return "food:apricot";
                case "Pine":
                    return "food:pine_nuts";
                default:
                    return null;
            }
        }

        private static string BerryItemId(string berry)
        {
            switch (berry)
            {
                case "WhiteCurrant":
                    return "food:berry:white_currant";
                case "RedCurrant":
                    return "food:berry:red_currant";
                case "BlackBerry":
                    return "food:berry:blackberry";
                default:
                    return null;
            }
        }

        private static void AddDropTableItems(string dropTableId, Guid guid, bool requireInteractable, int depth)
        {
            if (depth > 3)
                return;

            ServerDropTableData table = ServerDropTablesDataBase.GetDropTableItemListOrNull(dropTableId);
            if (table == null)
                return;

            HashSet<string> items = new HashSet<string>();
            CollectDropTableItems(table.DropTable, items, depth);
            foreach (string itemId in items)
                AddEntity(itemId, guid, requireInteractable);
        }

        private static void CollectDropTableItems(DropTableSystem table, HashSet<string> items, int depth)
        {
            if (table == null || depth > 3)
                return;

            ItemDrop[] drops = table._itemDrops;
            if (drops != null)
            {
                for (int i = 0; i < drops.Length; i++)
                    CollectDropItem(drops[i], items, depth);
            }

            if (table._dropTableSlots != null)
            {
                foreach (DropTableSlot slot in table._dropTableSlots.Values)
                {
                    ItemDrop[] slotDrops = slot.ItemDrops;
                    if (slotDrops == null)
                        continue;
                    for (int i = 0; i < slotDrops.Length; i++)
                        CollectDropItem(slotDrops[i], items, depth);
                }
            }
        }

        private static void CollectDropItem(ItemDrop drop, HashSet<string> items, int depth)
        {
            if (!string.IsNullOrEmpty(drop.ItemId) && drop.ItemId != "(empty)" && ItemDataBase.GetItemDataOrNull(drop.ItemId) != null)
                items.Add(drop.ItemId);

            if (!string.IsNullOrEmpty(drop.DropTableId))
            {
                ServerDropTableData nested = ServerDropTablesDataBase.GetDropTableItemListOrNull(drop.DropTableId);
                if (nested != null)
                    CollectDropTableItems(nested.DropTable, items, depth + 1);
            }
        }

        private static void BuildRecipeIndex()
        {
            foreach (ItemRecipe recipe in ItemRecipeDataBase.ItemRecipeDataMap.Values)
            {
                if (string.IsNullOrEmpty(recipe.CraftedItemId))
                    continue;
                AddRecipeIngredients(recipe.CraftedItemId, recipe.ItemsAmounts);
            }

            // 市民工作配方（例如橄榄 → 橄榄泥、橄榄泥 → 橄榄油）也参与“成品 → 原料来源”的反推。
            foreach (WorkRecipeModel recipe in WorkRecipeDataBase.DataMap.Values)
            {
                if (recipe.Output.Type != WorkOrderType.Item || string.IsNullOrEmpty(recipe.Output.CraftedId))
                    continue;
                AddRecipeIngredients(recipe.Output.CraftedId, recipe.ItemIngredients);
            }
        }

        private static void AddRecipeIngredients(string craftedItemId, ItemAmount[] ingredients)
        {
            if (ingredients == null || ingredients.Length == 0)
                return;

            if (!RecipesByOutput.TryGetValue(craftedItemId, out List<ItemAmount[]> list))
            {
                list = new List<ItemAmount[]>();
                RecipesByOutput[craftedItemId] = list;
            }
            list.Add(ingredients);
        }

        /// <summary>食物/材料类产物：向上找原料的来源（橄榄油 → 橄榄泥 → 橄榄 → 橄榄树）。</summary>
        private static void ResolveRecipeSources()
        {
            HashSet<string> visiting = new HashSet<string>();
            foreach (string itemId in ItemDataBase.DataMap.Keys)
            {
                ItemData data = ItemDataBase.GetItemDataOrNull(itemId);
                if (data == null)
                    continue;
                if ((data.Flags & (ItemFlag.Food | ItemFlag.Material | ItemFlag.Seed | ItemFlag.Offering)) == 0)
                    continue;
                // 装备/武器即使带 Material 标记，也是制成品，不应拿原料来源当成它的来源。
                if (data.Equippable != null)
                    continue;
                ResolveItem(itemId, 0, visiting);
            }
        }

        private static bool ResolveItem(string itemId, int depth, HashSet<string> visiting)
        {
            if (depth > 4 || !visiting.Add(itemId))
                return HasDirectSource(itemId);

            bool found = HasDirectSource(itemId);

            if (RecipesByOutput.TryGetValue(itemId, out List<ItemAmount[]> recipes))
            {
                for (int r = 0; r < recipes.Count; r++)
                {
                    ItemAmount[] ingredients = recipes[r];
                    if (ingredients == null)
                        continue;
                    for (int i = 0; i < ingredients.Length; i++)
                    {
                        if (!ResolveItem(ingredients[i].ItemId, depth + 1, visiting))
                            continue;

                        SourceSpec ingredientSpec = Map.TryGetValue(ingredients[i].ItemId, out SourceSpec value) ? value : null;
                        if (ingredientSpec == null)
                            continue;
                        if (ingredientSpec.EntityGuids.Count == 0 && ingredientSpec.CropIds.Count == 0)
                            continue;

                        SourceSpec productSpec = SpecFor(itemId);
                        productSpec.EntityGuids.UnionWith(ingredientSpec.EntityGuids);
                        productSpec.CropIds.UnionWith(ingredientSpec.CropIds);
                        productSpec.RequireInteractable |= ingredientSpec.RequireInteractable;
                        found = true;
                    }
                }
            }

            visiting.Remove(itemId);
            return found;
        }

        private static bool HasDirectSource(string itemId)
        {
            SourceSpec spec = Map.TryGetValue(itemId, out SourceSpec value) ? value : null;
            return spec != null && (spec.EntityGuids.Count > 0 || spec.CropIds.Count > 0);
        }
    }
}
