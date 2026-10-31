using System;
using System.Diagnostics;
using Candide;
using Candide.GameModels;
using Candide.World;
using CandideCreator.Shared.Graphics;
using CandideServer.Models.Worlds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Data;
using Shared.Models.Items;
using Shared.Text;

namespace RomesteadRadar
{
    /// <summary>
    /// 塞尔达式物品雷达：选中一种物品/生物/宝箱后，
    /// 玩家移动时每 1 秒在小地图上放出一道朝目标方向的扇形雷达波；站立不动则不显示。
    /// </summary>
    internal static class ItemRadar
    {
        internal enum TargetKind
        {
            Item,
            Creature,
            Chest
        }

        private const float PulseInterval = 1f;
        private const float PulseLifetime = 0.85f;
        private const float MoveThreshold = 0.5f;
        private const float MoveHoldTime = 0.35f;

        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        private static TargetKind _targetKind;
        private static string _targetItemId;
        private static Guid _targetCreatureGuid;

        private static Vector2 _lastPlayerPosition;
        private static float _movedSinceCheck;
        private static float _lastMoveTime = -999f;
        private static float _lastPulseTime = -999f;

        private static bool _pulseActive;
        private static Vector2 _pulseDirection = new Vector2(1f, 0f);
        private static float _lastDrawTime = -999f;

        internal static TargetKind CurrentTargetKind => _targetKind;

        internal static string TargetItemId => _targetItemId;

        internal static Guid TargetCreatureGuid => _targetCreatureGuid;

        internal static string TargetDisplayName
        {
            get
            {
                switch (_targetKind)
                {
                    case TargetKind.Item:
                        return GetItemDisplayName(_targetItemId);
                    case TargetKind.Creature:
                        return KillTracker.GetDisplayName(_targetCreatureGuid);
                    case TargetKind.Chest:
                        return Loc.T("未开启的宝箱", "Unopened Chest");
                    default:
                        return null;
                }
            }
        }

        /// <summary>物品显示名始终取游戏当前语言的本地化文本。</summary>
        internal static string GetItemDisplayName(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return null;

            ItemData itemData = ItemDataBase.GetItemDataOrNull(itemId);
            if (itemData != null)
            {
                string name = itemData.Name.GetTranslation();
                if (!string.IsNullOrEmpty(name) && !name.StartsWith("<", StringComparison.Ordinal))
                    return name;
            }
            return itemId;
        }

        internal static bool HasTarget
        {
            get
            {
                switch (_targetKind)
                {
                    case TargetKind.Item:
                        return !string.IsNullOrEmpty(_targetItemId);
                    case TargetKind.Creature:
                        return _targetCreatureGuid != Guid.Empty;
                    case TargetKind.Chest:
                        return true;
                    default:
                        return false;
                }
            }
        }

        internal static void SetItemTarget(string itemId)
        {
            _targetKind = TargetKind.Item;
            _targetItemId = itemId;
            _targetCreatureGuid = Guid.Empty;
            ResetPulse();
        }

        internal static void SetCreatureTarget(Guid baseGuid)
        {
            _targetKind = TargetKind.Creature;
            _targetItemId = null;
            _targetCreatureGuid = baseGuid;
            ResetPulse();
        }

        internal static void SetChestTarget()
        {
            _targetKind = TargetKind.Chest;
            _targetItemId = null;
            _targetCreatureGuid = Guid.Empty;
            ResetPulse();
        }

        internal static void ClearTarget()
        {
            _targetKind = TargetKind.Item;
            _targetItemId = null;
            _targetCreatureGuid = Guid.Empty;
            _pulseActive = false;
            _lastPulseTime = -999f;
        }

        private static void ResetPulse()
        {
            _pulseActive = false;
            _lastPulseTime = -999f;
            _lastMoveTime = (float)Clock.Elapsed.TotalSeconds;
            _movedSinceCheck = 0f;
            _lastDrawTime = -999f;
            _lastPlayerPosition = Globals.Game?.Player != null ? Globals.Game.Player.Position2 : Vector2.Zero;
        }

        internal static void DrawMinimapPulse(WorldMap worldMap, SpriteBatch batch, Vector2 mapPosition, Vector2 mapSize, float scale, Vector2 centerTile)
        {
            if (!Plugin.RadarEnabled || !HasTarget || worldMap == null || batch == null)
                return;

            if (Globals.Game?.Player == null)
                return;
            if (GameState.CurrentWorld == null || GameState.CurrentWorld.Type == WorldModel.WorldType.Interior)
                return;

            float now = (float)Clock.Elapsed.TotalSeconds;
            Vector2 playerPosition = Globals.Game.Player.Position2;

            // 小地图中断过（隐藏/切场景）时，重新从当前位置计步，避免误判为移动。
            if (now - _lastDrawTime > 1f)
            {
                _lastPlayerPosition = playerPosition;
                _movedSinceCheck = 0f;
                _lastMoveTime = now - 999f;
            }
            _lastDrawTime = now;

            float moved = Vector2.Distance(playerPosition, _lastPlayerPosition);
            _lastPlayerPosition = playerPosition;
            _movedSinceCheck += moved;
            if (_movedSinceCheck > MoveThreshold)
            {
                _movedSinceCheck = 0f;
                _lastMoveTime = now;
            }

            // 不移动就不显示雷达波。
            bool moving = now - _lastMoveTime < MoveHoldTime;
            if (!moving)
            {
                _pulseActive = false;
                return;
            }

            // 每 1 秒重新找一次最近的来源，并发射一圈方向波。
            if (now - _lastPulseTime >= PulseInterval)
            {
                _lastPulseTime = now;
                _pulseActive = false;

                Vector2 targetPosition;
                bool found;
                if (_targetKind == TargetKind.Creature)
                {
                    found = RadarTargeting.TryFindNearestCreature(_targetCreatureGuid, playerPosition, out targetPosition);
                }
                else if (_targetKind == TargetKind.Chest)
                {
                    found = RadarTargeting.TryFindNearestChest(playerPosition, out targetPosition);
                }
                else
                {
                    found = RadarTargeting.TryFindNearestItem(_targetItemId, playerPosition, out targetPosition);
                }
                if (found)
                {
                    Vector2 delta = targetPosition - playerPosition;
                    if (delta.LengthSquared() >= 0.0001f)
                    {
                        _pulseDirection = Vector2.Normalize(delta);
                        _pulseActive = true;
                    }
                }
            }

            if (!_pulseActive)
                return;

            float age = now - _lastPulseTime;
            if (age > PulseLifetime)
                return;

            Vector2 roundedCenter = Vector2.Round(centerTile * scale) / scale;
            Matrix transform = worldMap.GetTilePosToScreenPosTransform(mapPosition, mapSize, roundedCenter, scale);
            Vector2 center = Vector2.Transform(playerPosition / 16f, transform);

            float progress = MathHelper.Clamp(age / PulseLifetime, 0f, 1f);
            float eased = 1f - (1f - progress) * (1f - progress);
            float maxRadius = Math.Min(mapSize.X, mapSize.Y) * 0.55f;
            float radius = 8f + (maxRadius - 8f) * eased;
            float alpha = 1f - eased;
            if (alpha <= 0.02f)
                return;

            Color ringColor = new Color(120, 220, 255);
            ringColor.A = (byte)(alpha * 165f);
            Color arcColor = new Color(200, 250, 255);
            arcColor.A = (byte)(alpha * 255f);

            // 只朝目标方向展开的扇形雷达波：前沿弧 + 内侧弧 + 两条侧边，不再画整圈。
            float angle = MathF.Atan2(_pulseDirection.Y, _pulseDirection.X);
            float halfAngle = MathHelper.ToRadians(32f);
            float thickness = 2.5f + (1f - eased) * 2f;

            DrawSectorWave(batch, center, radius, angle, halfAngle, arcColor, ringColor, thickness);
        }

        /// <summary>画一个朝指定方向张开的扇形波（前沿弧、内侧回声弧、两条侧边）。</summary>
        private static void DrawSectorWave(SpriteBatch batch, Vector2 center, float radius, float angle, float halfAngle, Color arcColor, Color edgeColor, float thickness)
        {
            DrawArc(batch, center, radius, angle - halfAngle, angle + halfAngle, arcColor, thickness + 1.5f, 16);

            // 内侧回声弧，让扇形更像一圈向外扩散的波。
            if (radius > 24f)
            {
                Color innerColor = new Color(edgeColor.R, edgeColor.G, edgeColor.B, (byte)(edgeColor.A * 0.6f));
                DrawArc(batch, center, radius * 0.62f, angle - halfAngle, angle + halfAngle, innerColor, Math.Max(1.5f, thickness * 0.7f), 12);
            }

            // 两条侧边从中心连到前沿弧两端，强化“扇形”轮廓。
            Vector2 leftDirection = new Vector2(MathF.Cos(angle - halfAngle), MathF.Sin(angle - halfAngle));
            Vector2 rightDirection = new Vector2(MathF.Cos(angle + halfAngle), MathF.Sin(angle + halfAngle));
            batch.DrawLine(center, center + leftDirection * radius, edgeColor, 2f);
            batch.DrawLine(center, center + rightDirection * radius, edgeColor, 2f);
        }

        private static void DrawArc(SpriteBatch batch, Vector2 center, float radius, float startAngle, float endAngle, Color color, float thickness, int segments)
        {
            Vector2 previous = center + new Vector2(MathF.Cos(startAngle), MathF.Sin(startAngle)) * radius;
            for (int i = 1; i <= segments; i++)
            {
                float t = startAngle + (endAngle - startAngle) * i / segments;
                Vector2 current = center + new Vector2(MathF.Cos(t), MathF.Sin(t)) * radius;
                batch.DrawLine(previous, current, color, thickness);
                previous = current;
            }
        }
    }
}
