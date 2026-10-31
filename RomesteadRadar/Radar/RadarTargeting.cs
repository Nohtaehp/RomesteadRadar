using System;
using System.Collections.Generic;
using Candide.Entities.Controllers.Other;
using Candide.GameModels;
using Candide.GameModels.Models;
using CandideServer.Models.Worlds;
using Microsoft.Xna.Framework;
using Shared;
using Shared.Entity;
using Shared.Entity.Components;

namespace RomesteadRadar
{
    /// <summary>根据当前追踪的物品，在已加载的区块里找最近的“可收集来源”。</summary>
    internal static class RadarTargeting
    {
        /// <summary>
        /// 调试用：固定雷达搜索半径（单位：格，1 格 = 16 像素）。
        /// 设为 0 或负数表示不限制（仍只搜索当前已加载的区块）。
        /// </summary>
        internal static readonly float DebugScanRangeTiles = 0f;

        /// <summary>把调试半径换算成平方距离，供距离比较使用。</summary>
        private static float MaxScanDistanceSquared
        {
            get
            {
                if (DebugScanRangeTiles <= 0f)
                    return float.MaxValue;
                float pixels = DebugScanRangeTiles * 16f;
                return pixels * pixels;
            }
        }

        internal static bool HasAnySource(string itemId)
        {
            return ItemSources.HasAnySource(itemId);
        }

        internal static bool TryFindNearestItem(string itemId, Vector2 playerPosition, out Vector2 targetPosition)
        {
            targetPosition = Vector2.Zero;
            if (string.IsNullOrEmpty(itemId))
                return false;
            if (GameState.CurrentWorld == null || GameState.CurrentWorld.Type == WorldModel.WorldType.Interior)
                return false;

            ItemSources.SourceSpec spec = ItemSources.Get(itemId);
            float maxDistance = MaxScanDistanceSquared;
            float bestDistance = maxDistance;
            bool found = false;

            Guid outsideWorldId = GameState.OutsideWorldId;
            if (spec != null && spec.EntityGuids.Count > 0)
            {
                foreach (EntityWrapper entity in GameState.Entities.Values)
                {
                    if (entity == null || entity.WorldId != outsideWorldId)
                        continue;
                    if (!spec.EntityGuids.Contains(entity.BaseGuid))
                        continue;
                    if (spec.RequireInteractable && !entity.Mask.HasFlags(Component.Interactable))
                        continue;

                    Vector2 position = entity.Position2;
                    float distance = Vector2.DistanceSquared(position, playerPosition);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        targetPosition = position;
                        found = true;
                    }
                }
            }

            // 已种植/野生的作物：成熟后才算可收集。
            string seedCropId = ItemSources.GetPlacedCropId(itemId);
            foreach (ClientCropModel crop in GameState.Crops.Values)
            {
                if (crop?.Data == null || !crop.IsGrown)
                    continue;
                bool isSeedTarget = seedCropId != null && crop.Data.Id == seedCropId;
                if (!isSeedTarget && !CropProduces(crop, itemId))
                    continue;

                Vector2 position = crop.WorldPosition;
                float distance = Vector2.DistanceSquared(position, playerPosition);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    targetPosition = position;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>在已加载的区块里找最近的、仍然存活的指定生物。</summary>
        internal static bool TryFindNearestCreature(Guid baseGuid, Vector2 playerPosition, out Vector2 targetPosition)
        {
            targetPosition = Vector2.Zero;
            if (baseGuid == Guid.Empty)
                return false;
            if (GameState.CurrentWorld == null || GameState.CurrentWorld.Type == WorldModel.WorldType.Interior)
                return false;

            Guid outsideWorldId = GameState.OutsideWorldId;
            float maxDistance = MaxScanDistanceSquared;
            float bestDistance = maxDistance;
            bool found = false;

            foreach (EntityWrapper entity in GameState.Entities.Values)
            {
                if (entity == null || entity.Removed || entity.WorldId != outsideWorldId)
                    continue;
                if (entity.BaseGuid != baseGuid)
                    continue;
                if (!entity.Mask.HasFlags(Component.Health) || entity.Health <= 0f)
                    continue;

                Vector2 position = entity.Position2;
                float distance = Vector2.DistanceSquared(position, playerPosition);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    targetPosition = position;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// 在大世界已加载区域里找最近的“未开启宝箱”。
        /// 只认真正的箱子实体（ChestController），并且：
        /// 1. 状态必须是 Closed(0) 或 Unlocked(1)，已清空的箱子不算；
        /// 2. 玩家自己放置的箱子（带 owner_character_id）不算；
        /// 3. 落在任意城镇 TileBounds 内的箱子不算。
        /// </summary>
        internal static bool TryFindNearestChest(Vector2 playerPosition, out Vector2 targetPosition)
        {
            targetPosition = Vector2.Zero;
            if (GameState.CurrentWorld == null || GameState.CurrentWorld.Type == WorldModel.WorldType.Interior)
                return false;

            Guid outsideWorldId = GameState.OutsideWorldId;
            float maxDistance = MaxScanDistanceSquared;
            float bestDistance = maxDistance;
            bool found = false;

            foreach (EntityWrapper entity in GameState.Entities.Values)
            {
                if (entity == null || entity.Removed || entity.WorldId != outsideWorldId)
                    continue;
                if (!EntityIds.ChestIds.Contains(entity.BaseGuid))
                    continue;

                // 隐藏宝箱（可破坏的枯树/石堆等）没有 ChestController，也不存在开箱状态，排除。
                if (!(entity.Controller is ChestController chest))
                    continue;
                // 0 = Closed（未开锁），1 = Unlocked（已解锁但仍有战利品）。
                if (chest.State != 0 && chest.State != 1)
                    continue;
                // 玩家自己放置的箱子（城镇储物箱等）不参与追踪。
                if (!string.IsNullOrEmpty(chest.Parameters.GetStringNullIfEmpty("owner_character_id")))
                    continue;

                Vector2 position = entity.Position2;
                if (IsInsideAnyTown(position))
                    continue;

                float distance = Vector2.DistanceSquared(position, playerPosition);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    targetPosition = position;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>大世界坐标（像素）是否落在某个已知城镇的 TileBounds 内。</summary>
        private static bool IsInsideAnyTown(Vector2 worldPosition)
        {
            Vector2 tilePosition = worldPosition / 16f;
            foreach (ClientTownModel town in GameState.Towns.Values)
            {
                if (town?.Model != null && town.Model.TileBounds.Contains(tilePosition))
                    return true;
            }
            return false;
        }

        private static bool CropProduces(ClientCropModel crop, string itemId)
        {
            if (crop?.Data == null)
                return false;
            if (crop.Data.OnRemovedDropItemId == itemId)
                return true;
            return DropTableContains(crop.Data.DropTable, itemId, 0)
                || DropTableContains(crop.Data.WildDropTable, itemId, 0);
        }

        private static bool DropTableContains(Shared.Models.DropTables.DropTableSystem table, string itemId, int depth)
        {
            if (table == null || depth > 3)
                return false;

            Shared.Models.DropTables.ItemDrop[] drops = table._itemDrops;
            if (drops != null)
            {
                for (int i = 0; i < drops.Length; i++)
                {
                    if (drops[i].ItemId == itemId)
                        return true;
                    if (MatchesNestedDropTable(drops[i], itemId, depth))
                        return true;
                }
            }

            if (table._dropTableSlots != null)
            {
                foreach (Shared.Models.DropTables.DropTableSlot slot in table._dropTableSlots.Values)
                {
                    Shared.Models.DropTables.ItemDrop[] slotDrops = slot.ItemDrops;
                    if (slotDrops == null)
                        continue;
                    for (int i = 0; i < slotDrops.Length; i++)
                    {
                        if (slotDrops[i].ItemId == itemId)
                            return true;
                        if (MatchesNestedDropTable(slotDrops[i], itemId, depth))
                            return true;
                    }
                }
            }

            return false;
        }

        private static bool MatchesNestedDropTable(Shared.Models.DropTables.ItemDrop drop, string itemId, int depth)
        {
            if (string.IsNullOrEmpty(drop.DropTableId))
                return false;
            CandideServer.Data.ServerDataSetups.ServerDropTableData nested =
                CandideServer.Database.ServerDropTablesDataBase.GetDropTableItemListOrNull(drop.DropTableId);
            return nested != null && DropTableContains(nested.DropTable, itemId, depth + 1);
        }
    }
}
