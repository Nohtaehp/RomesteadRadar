using System;
using System.Collections.Generic;
using System.Text.Json;
using Candide.GameModels;
using Candide.Multiplayer;
using CandideServer;
using Shared;
using Shared.Combat;
using Shared.Data;
using Shared.Entity;
using Shared.Entity.Components;
using Shared.Models.Boss;
using Shared.Text;

namespace RomesteadRadar
{
    /// <summary>一条“已击杀生物”记录：实体基础 GUID + 显示名。</summary>
    internal sealed class KilledCreature
    {
        internal Guid BaseGuid;
        internal string Name;
    }

    /// <summary>伤害结算前抓取的击杀判定快照（目标可能随后被删除，不能再访问实体）。</summary>
    internal sealed class KillSnapshot
    {
        internal Guid BaseGuid;
        internal string Name;
        internal float PreHealth;
    }

    /// <summary>
    /// 记录本地玩家杀死过的生物。游戏本身没有保存击杀图鉴，
    /// 因此在伤害结算前后对比生命值：目标死亡且最后一击来自玩家时记录其基础实体 GUID。
    /// 记录写入 BepInEx 配置，按存档 GameId 分开保存，跨启动保留。
    /// </summary>
    internal static class KillTracker
    {
        /// <summary>存档 GameId → 该存档的击杀记录。</summary>
        private static readonly Dictionary<string, Dictionary<Guid, string>> KillsByGame =
            new Dictionary<string, Dictionary<Guid, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>旧版本（全局一份）的击杀记录，首次进入存档时迁移到该存档。</summary>
        private static readonly Dictionary<Guid, string> LegacyKills = new Dictionary<Guid, string>();
        private static bool _hasLegacyKills;

        internal static string CurrentGameKey
        {
            get
            {
                GameDescription description = GameState.GameDescription;
                if (description == null || description.GameId == Guid.Empty)
                    return null;
                return description.GameId.ToString("D");
            }
        }

        private static readonly Dictionary<Guid, (string Chinese, string English)> KnownNames =
            new Dictionary<Guid, (string Chinese, string English)>
        {
            // 野生动物
            { EntityIds.Deer, ("鹿", "Deer") },
            { EntityIds.Frog, ("青蛙", "Frog") },
            { EntityIds.Rabbit, ("兔子", "Rabbit") },
            { EntityIds.Mouse, ("小老鼠", "Mouse") },
            { EntityIds.Bird, ("鸟", "Bird") },
            { EntityIds.GoatMale, ("公山羊", "Goat (Male)") },
            { EntityIds.GoatFemale, ("母山羊", "Goat (Female)") },
            { EntityIds.Squirrel, ("松鼠", "Squirrel") },
            { EntityIds.DeerWhite, ("白鹿", "White Deer") },
            { EntityIds.Turtle, ("乌龟", "Turtle") },
            { EntityIds.Rat, ("老鼠", "Rat") },
            { EntityIds.RatBig, ("大老鼠", "Big Rat") },
            { EntityIds.RatGiant, ("巨鼠", "Giant Rat") },

            // 常见敌人
            { EntityIds.Potman, ("陶罐人", "Potman") },
            { EntityIds.Boar, ("野猪", "Boar") },
            { EntityIds.Wolf, ("狼", "Wolf") },
            { EntityIds.WolfAlpha, ("头狼", "Alpha Wolf") },
            { EntityIds.Bear, ("熊", "Bear") },
            { EntityIds.BearCub, ("幼熊", "Bear Cub") },
            { EntityIds.Bushcrab, ("灌木蟹", "Bush Crab") },
            { EntityIds.Lizard, ("蜥蜴", "Lizard") },
            { EntityIds.Scarab, ("圣甲虫", "Scarab") },
            { EntityIds.CrabRock, ("岩蟹", "Rock Crab") },
            { EntityIds.ScorpionSmall, ("小蝎子", "Small Scorpion") },
            { EntityIds.ScorpionBig, ("大蝎子", "Large Scorpion") },
            { EntityIds.Vulture, ("秃鹫", "Vulture") },
            { EntityIds.Snake, ("蛇", "Snake") },
            { EntityIds.Medusa, ("美杜莎", "Medusa") },
            { EntityIds.Gorgon, ("戈耳工", "Gorgon") },
            { EntityIds.CrabBone, ("骨蟹", "Bone Crab") },

            // 火山
            { EntityIds.LavaConstruct, ("熔岩构造体", "Lava Construct") },
            { EntityIds.LavaSpitter, ("熔岩喷吐者", "Lava Spitter") },
            { EntityIds.Phoenix, ("凤凰", "Phoenix") },
            { EntityIds.SnakeLava, ("熔岩蛇", "Lava Snake") },
            { EntityIds.Ashman, ("灰烬人", "Ashman") },
            { EntityIds.FireElemental, ("火元素", "Fire Elemental") },
            { EntityIds.MasterEngineer, ("首席工程师", "Master Engineer") },
            { EntityIds.Engineer, ("工程师", "Engineer") },
            { EntityIds.TurtleExplosive, ("爆裂龟", "Explosive Turtle") },
            { EntityIds.LavaLizard, ("熔岩蜥蜴", "Lava Lizard") },

            // 亡灵 / 沼泽
            { EntityIds.SwampPlant, ("沼泽植物", "Swamp Plant") },
            { EntityIds.JumpingSkull, ("跳跃头骨", "Jumping Skull") },
            { EntityIds.Fallen, ("堕落者", "Fallen") },
            { EntityIds.FallenSword, ("堕落剑士", "Fallen Sword") },
            { EntityIds.FallenSpear, ("堕落矛兵", "Fallen Spear") },
            { EntityIds.FallenBloat, ("堕落胀体", "Fallen Bloat") },
            { EntityIds.FallenCrawler, ("堕落爬行者", "Fallen Crawler") },
            { EntityIds.FallenCenturion, ("堕落百夫长", "Fallen Centurion") },
            { EntityIds.SkeletonArcher, ("骷髅弓手", "Skeleton Archer") },
            { EntityIds.SkeletonWarrior, ("骷髅战士", "Skeleton Warrior") },
            { Guid.Parse("34652d96-74d3-412d-8c17-552eb295261f"), ("无咒骷髅战士", "Skeleton Warrior (No Curse)") },
            { EntityIds.Wraith, ("怨灵", "Wraith") },
            { EntityIds.BurningFallen, ("燃烧堕落者", "Burning Fallen") },
            { EntityIds.BurningFallenBloat, ("燃烧堕落胀体", "Burning Fallen Bloat") },
            { EntityIds.BurningFallenCrawler, ("燃烧堕落爬行者", "Burning Fallen Crawler") },
            { EntityIds.BurningFallenCenturion, ("燃烧堕落百夫长", "Burning Fallen Centurion") },
            { EntityIds.AshenSkeletonArcher, ("灰烬骷髅弓手", "Ashen Skeleton Archer") },
            { EntityIds.AshenSkeletonWarrior, ("灰烬骷髅战士", "Ashen Skeleton Warrior") },
            { EntityIds.AshenJumpingSkull, ("灰烬跳跃头骨", "Ashen Jumping Skull") },
            { EntityIds.AshenSkeletonJumpingSkull, ("灰烬骷髅跳跃头骨", "Ashen Skeleton Jumping Skull") },
            { Guid.Parse("f58b936c-e44a-4cdc-852c-7e344122e804"), ("迅捷跳跃头骨", "Fast Jumping Skull") },

            // 萨堤尔
            { EntityIds.Pyzifax, ("皮兹法克斯", "Pyzifax") },
            { Guid.Parse("c3a91e74-5b28-4f0d-9e6a-1d8f4b7c2a50"), ("狐猴精皮兹法克斯", "Pyzifax the Lemures") },
            { EntityIds.PyzifaxScout, ("皮兹法克斯斥候", "Pyzifax Scout") },
            { EntityIds.SatyrTinySpectator, ("小萨堤尔观众", "Satyr Spectator") },
            { EntityIds.SatyrBarbarianSpectator, ("萨堤尔蛮兵观众", "Satyr Barbarian Spectator") },
            { EntityIds.SatyrRangerSpectator, ("萨堤尔游侠观众", "Satyr Ranger Spectator") },
            { EntityIds.SatyrBruteSpectator, ("萨堤尔巨汉观众", "Satyr Brute Spectator") },
            { EntityIds.SatyrTiny, ("小萨堤尔", "Small Satyr") },
            { EntityIds.SatyrBarbarian, ("萨堤尔蛮兵", "Satyr Barbarian") },
            { EntityIds.SatyrRanger, ("萨堤尔游侠", "Satyr Ranger") },
            { EntityIds.SatyrBrute, ("萨堤尔巨汉", "Satyr Brute") },
            { EntityIds.SatyrWatchtower, ("萨堤尔瞭望塔", "Satyr Watchtower") },
            { EntityIds.GreySatyrTiny, ("灰萨堤尔小兵", "Grey Satyr Grunt") },
            { EntityIds.GreySatyrBarbarian, ("灰萨堤尔蛮兵", "Grey Satyr Barbarian") },
            { EntityIds.GreySatyrRanger, ("灰萨堤尔游侠", "Grey Satyr Ranger") },
            { EntityIds.GreySatyrBrute, ("灰萨堤尔巨汉", "Grey Satyr Brute") },

            // Boss（部分有独立 GUID）
            { EntityIds.OwlOfMinerva, ("密涅瓦守护者", "Minerva's Owl") },
            { Guid.Parse("f62bb201-544f-441f-8d54-3e3aafc2c06b"), ("独眼巨人", "Cyclops") },
            { Guid.Parse("d4519185-e658-4634-b247-913d691f6c4e"), ("塔洛斯原型机", "Talos Prototype") },
            { Guid.Parse("e36cb270-17b5-4ef7-b6b6-caddd673c9ca"), ("冥界诸神", "Di Inferi") },
            { Guid.Parse("8d437789-999f-4aa0-8e79-1c1fc2c97086"), ("阿拉伯大凤凰", "Phoenix of Arabia") }
        };

        internal static void Initialize(string serialized)
        {
            KillsByGame.Clear();
            LegacyKills.Clear();
            _hasLegacyKills = false;
            if (string.IsNullOrWhiteSpace(serialized))
                return;

            string trimmed = serialized.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                LoadPerGame(trimmed);
                return;
            }

            // 旧格式：全局一份，guid|name;guid|name……
            string[] entries = trimmed.Split(';');
            for (int i = 0; i < entries.Length; i++)
            {
                string entry = entries[i];
                if (string.IsNullOrEmpty(entry))
                    continue;

                int separator = entry.IndexOf('|');
                string guidText = separator >= 0 ? entry.Substring(0, separator) : entry;
                string name = separator >= 0 ? entry.Substring(separator + 1) : string.Empty;
                if (Guid.TryParse(guidText, out Guid guid))
                    LegacyKills[guid] = name;
            }
            _hasLegacyKills = LegacyKills.Count > 0;
        }

        private static void LoadPerGame(string serialized)
        {
            try
            {
                Dictionary<string, Dictionary<string, string>> stored =
                    JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(serialized);
                if (stored == null)
                    return;

                foreach (KeyValuePair<string, Dictionary<string, string>> game in stored)
                {
                    if (string.IsNullOrEmpty(game.Key) || game.Value == null)
                        continue;

                    var kills = new Dictionary<Guid, string>();
                    foreach (KeyValuePair<string, string> pair in game.Value)
                    {
                        if (Guid.TryParse(pair.Key, out Guid guid))
                            kills[guid] = pair.Value ?? string.Empty;
                    }
                    if (kills.Count > 0)
                        KillsByGame[game.Key] = kills;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning("Failed to read per-save kill records: " + ex.Message);
            }
        }

        /// <summary>当前存档的击杀记录；不在游戏内（没有存档 ID）时返回 null。</summary>
        private static Dictionary<Guid, string> GetCurrentKills()
        {
            string gameKey = CurrentGameKey;
            if (gameKey == null)
                return null;

            if (_hasLegacyKills)
            {
                _hasLegacyKills = false;
                Dictionary<Guid, string> migrated = GetOrCreateKills(gameKey);
                foreach (KeyValuePair<Guid, string> pair in LegacyKills)
                {
                    if (!migrated.ContainsKey(pair.Key))
                        migrated[pair.Key] = pair.Value;
                }
                LegacyKills.Clear();
                Save();
            }

            return GetOrCreateKills(gameKey);
        }

        private static Dictionary<Guid, string> GetOrCreateKills(string gameKey)
        {
            if (!KillsByGame.TryGetValue(gameKey, out Dictionary<Guid, string> kills))
            {
                kills = new Dictionary<Guid, string>();
                KillsByGame[gameKey] = kills;
            }
            return kills;
        }

        /// <summary>伤害结算前调用：如果目标是玩家击杀的生物，抓取必要信息。</summary>
        internal static KillSnapshot CreateSnapshot(DamageInfo damageInfo)
        {
            EntityWrapper victim = damageInfo?.Victim;
            if (victim == null || victim.Removed || victim.Id == default(Guid))
                return null;
            if (!victim.Mask.HasFlags(Component.Health))
                return null;

            float preHealth = victim.Health;
            if (preHealth <= 0f)
                return null;
            if (!IsCreature(victim))
                return null;

            EntityWrapper source = damageInfo.SourceEntity;
            if (source == null)
                return null;

            bool byPlayer = false;
            if (GameState.LocalPlayer?.Character?.Character != null)
                byPlayer = PlayerHelper.IsLocalPlayer(source);
            if (!byPlayer && source.Mask.HasFlags(Component.Faction)
                && source.FactionMask.HasFlags(FactionMask.Player))
                byPlayer = true;
            if (!byPlayer)
                return null;

            Guid baseGuid = victim.BaseGuid;
            if (baseGuid == default(Guid))
                return null;

            return new KillSnapshot
            {
                BaseGuid = baseGuid,
                Name = Sanitize(victim.Name),
                PreHealth = preHealth
            };
        }

        /// <summary>伤害结算后调用：伤害足以打死目标时记录。</summary>
        internal static void TryRecord(KillSnapshot snapshot, float damageDealt)
        {
            if (snapshot == null || damageDealt <= 0f)
                return;
            if (snapshot.PreHealth - damageDealt > 0.01f)
                return;

            Dictionary<Guid, string> kills = GetCurrentKills();
            if (kills == null)
                return;

            if (kills.TryGetValue(snapshot.BaseGuid, out string existing) && !string.IsNullOrEmpty(existing))
                return;

            kills[snapshot.BaseGuid] = snapshot.Name;
            Save();
        }

        internal static IReadOnlyList<KilledCreature> GetEntries()
        {
            Dictionary<Guid, string> kills = GetCurrentKills();
            int count = kills?.Count ?? 0;
            var list = new List<KilledCreature>(count);
            if (kills == null)
                return list;

            foreach (KeyValuePair<Guid, string> pair in kills)
            {
                list.Add(new KilledCreature
                {
                    BaseGuid = pair.Key,
                    Name = GetDisplayName(pair.Key, pair.Value)
                });
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return list;
        }

        internal static string GetDisplayName(Guid baseGuid)
        {
            Dictionary<Guid, string> kills = GetCurrentKills();
            string fallback = null;
            kills?.TryGetValue(baseGuid, out fallback);
            return GetDisplayName(baseGuid, fallback);
        }

        internal static string GetDisplayName(Guid baseGuid, string fallback)
        {
            // 优先用游戏本地化表里的名称：Boss 名称随游戏语言变化，
            // 少数实体的 Name 本身也是本地化键（例如 Actor_UnarmedSatyr）。
            if (TryGetGameName(baseGuid, fallback, out string gameName))
                return gameName;

            // 普通动物/怪物在游戏本地化表里没有名称，保留中英文回退。
            if (KnownNames.TryGetValue(baseGuid, out var known))
                return Loc.IsChinese ? known.Chinese : known.English;
            if (!string.IsNullOrEmpty(fallback))
            {
                string name = fallback.Trim();
                if (name.Length > 0)
                    return char.ToUpperInvariant(name[0]) + name.Substring(1);
            }
            return baseGuid.ToString();
        }

        private static bool TryGetGameName(Guid baseGuid, string fallback, out string name)
        {
            name = null;

            // BossEntityBaseId 对应 BossDataBase 里的本地化名称。
            // 同一 GUID 可能同时有“???”条目和正式条目，取最后一个有效名称。
            foreach (BossData boss in BossDataBase.DataMap.Values)
            {
                if (boss == null || boss.BossEntityBaseId != baseGuid)
                    continue;

                string translated = boss.Name.GetTranslation();
                if (string.IsNullOrEmpty(translated)
                    || translated.StartsWith("<", StringComparison.Ordinal)
                    || translated.Trim('?', '？').Length == 0)
                    continue;

                name = translated;
            }
            if (name != null)
                return true;

            // 非 Boss 实体：如果游戏把实体名当作本地化键，就直接取当前语言文本。
            if (!string.IsNullOrEmpty(fallback)
                && StringDefinitions.TryGetString(fallback, out string localized)
                && !string.IsNullOrEmpty(localized))
            {
                name = localized;
                return true;
            }
            return false;
        }

        internal static bool HasRecords
        {
            get
            {
                Dictionary<Guid, string> kills = GetCurrentKills();
                return kills != null && kills.Count > 0;
            }
        }

        private static bool IsCreature(EntityWrapper entity)
        {
            if (entity.Mask.HasFlags(Component.Faction))
            {
                FactionMask faction = entity.FactionMask;
                // 玩家 / 自己的召唤物不是“被击杀的生物”。
                if (faction.HasFlags(FactionMask.Player))
                    return false;
                if (faction != FactionMask.None)
                    return true;
            }

            Guid baseGuid = entity.BaseGuid;
            for (int i = 0; i < EntityIds.AllCreatureGuids.Length; i++)
            {
                if (EntityIds.AllCreatureGuids[i] == baseGuid)
                    return true;
            }
            return false;
        }

        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            return text.Replace('|', ' ').Replace(';', ' ').Trim();
        }

        private static void Save()
        {
            if (Plugin.KilledCreatures == null)
                return;

            var store = new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, Dictionary<Guid, string>> game in KillsByGame)
            {
                if (game.Value == null || game.Value.Count == 0)
                    continue;

                var names = new Dictionary<string, string>(game.Value.Count);
                foreach (KeyValuePair<Guid, string> pair in game.Value)
                    names[pair.Key.ToString("D")] = pair.Value ?? string.Empty;
                store[game.Key] = names;
            }

            Plugin.KilledCreatures.Value = JsonSerializer.Serialize(store);
        }
    }
}
